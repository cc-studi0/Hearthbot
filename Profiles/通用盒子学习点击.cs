using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class TeacherDrivenClickProfile : ClickProfile
{
    private const string LogPrefix = "[ClickTeacher]";
    private const int TeacherFreshSeconds = 8;
    private const int RepeatDedupWindowMs = 2000;
    private const int UnresolvedSuppressWindowMs = 5000;
    private const int UnresolvedEscalationThreshold = 3;
    private const int DefaultWaitMs = 500;
    private const int FriendlyBoardLimit = 7;

    private string _lastActionSignature = string.Empty;
    private DateTime _lastActionUtc = DateTime.MinValue;
    // 相同老师动作连续无法映射时，短时间内先跳过它，避免整回合反复卡在同一个 unresolved 上。
    private string _lastUnresolvedSignature = string.Empty;
    private DateTime _lastUnresolvedUtc = DateTime.MinValue;
    private int _lastUnresolvedCount;

    public ClickInstruction GetNextClick(Board board)
    {
        if (board == null || !board.IsOwnTurn)
            return ClickInstruction.Wait(DefaultWaitMs);

        DecisionTeacherHintState state = DecisionStateExtractor.LoadTeacherHint();
        if (state == null)
            return ClickInstruction.Wait(DefaultWaitMs);

        if (!state.IsFresh(TeacherFreshSeconds)
            || !string.Equals(state.Status, "ok", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.Stage, "play", StringComparison.OrdinalIgnoreCase)
            || !state.MatchesProfileLoose(SafeCurrentProfileName()))
        {
            return ClickInstruction.Wait(DefaultWaitMs);
        }

        ClickInstruction chooseFallback = BuildChooseInstruction(board, null, state);
        if (chooseFallback != null)
            return chooseFallback;

        DecisionTeacherActionStep[] candidateSteps = GetExecutableCandidateSteps(board, state);
        bool unresolvedAttackIntent = false;
        for (int i = 0; i < candidateSteps.Length; i++)
        {
            DecisionTeacherActionStep step = candidateSteps[i];
            if (step == null)
                continue;

            string signature = BuildActionSignature(step);
            if (ShouldSkipUnresolvedStep(signature))
            {
                Log("YIELD " + DecisionStateExtractor.DescribeUnresolvedSuppressedEvent(signature, _lastUnresolvedCount));
                continue;
            }

            Log("STEP " + DecisionStateExtractor.DescribeStepPreview(
                signature,
                step.IsRequired,
                step.Score,
                step.Confidence,
                DescribeStepSource(step, i)));
            if (!string.IsNullOrWhiteSpace(_lastActionSignature)
                && string.Equals(_lastActionSignature, signature, StringComparison.Ordinal)
                && _lastActionUtc.AddMilliseconds(RepeatDedupWindowMs) > DateTime.UtcNow)
            {
                Log("WAIT " + DecisionStateExtractor.DescribeDedupWait(signature));
                return ClickInstruction.Wait(DefaultWaitMs);
            }

            ClickInstruction instruction = BuildInstruction(board, step, state);
            if (instruction != null)
            {
                _lastActionSignature = signature;
                _lastActionUtc = DateTime.UtcNow;
                if (string.Equals(_lastUnresolvedSignature, signature, StringComparison.Ordinal))
                {
                    _lastUnresolvedSignature = string.Empty;
                    _lastUnresolvedUtc = DateTime.MinValue;
                    _lastUnresolvedCount = 0;
                }
                return instruction;
            }

            if (IsAttackStep(step))
                unresolvedAttackIntent = true;

            RegisterUnresolvedStep(signature, step, i, candidateSteps.Length);
        }

        if (state.WantsEndTurn())
        {
            // 老师文本里同时出现结束回合和攻击时，优先等攻击映射刷新，避免直接空过。
            if (unresolvedAttackIntent)
            {
                Log("WAIT " + DecisionStateExtractor.DescribeEndTurnSuppressedWait(true));
                return ClickInstruction.Wait(DefaultWaitMs);
            }

            Log("ACTION End() | teacher=endturn");
            return ClickInstruction.End();
        }

        if (state.HasDiscoverPick)
            Log("WAIT discover_pick=" + state.DiscoverPickId + " | 缺少可执行 choice 实体/index");

        return ClickInstruction.Wait(DefaultWaitMs);
    }

    private static DecisionTeacherActionStep[] GetExecutableCandidateSteps(Board board, DecisionTeacherHintState state)
    {
        List<DecisionTeacherActionStep> candidates = new List<DecisionTeacherActionStep>();
        AppendUniqueCandidateStep(candidates, GetPrimaryRequiredStep(state));
        AppendUniqueCandidateStep(candidates, DecisionStateExtractor.TryBuildFallbackMatchStep(state));
        AppendUniqueCandidateStep(candidates, DecisionStateExtractor.TryBuildFallbackLineStep(board, state));
        return candidates.ToArray();
    }

    private static void AppendUniqueCandidateStep(List<DecisionTeacherActionStep> candidates, DecisionTeacherActionStep step)
    {
        if (candidates == null || step == null)
            return;

        string signature = BuildActionSignature(step);
        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(BuildActionSignature(candidates[i]), signature, StringComparison.Ordinal))
                return;
        }

        candidates.Add(step);
    }

    private static string DescribeStepSource(DecisionTeacherActionStep step, int index)
    {
        if (step == null)
            return "unknown";

        if (!string.IsNullOrWhiteSpace(step.PlanSource))
            return step.PlanSource;

        return index <= 0 ? "teacher_step" : "teacher_fallback";
    }

    private static bool IsAttackStep(DecisionTeacherActionStep step)
    {
        return step != null
            && string.Equals(step.ActionType ?? string.Empty, "Attack", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldSkipUnresolvedStep(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature)
            || !string.Equals(_lastUnresolvedSignature, signature, StringComparison.Ordinal)
            || _lastUnresolvedCount < UnresolvedEscalationThreshold)
        {
            return false;
        }

        return _lastUnresolvedUtc.AddMilliseconds(UnresolvedSuppressWindowMs) > DateTime.UtcNow;
    }

    private void RegisterUnresolvedStep(string signature, DecisionTeacherActionStep step, int candidateIndex, int candidateCount)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return;

        if (string.Equals(_lastUnresolvedSignature, signature, StringComparison.Ordinal)
            && _lastUnresolvedUtc.AddMilliseconds(UnresolvedSuppressWindowMs) > DateTime.UtcNow)
        {
            _lastUnresolvedCount++;
        }
        else
        {
            _lastUnresolvedSignature = signature;
            _lastUnresolvedCount = 1;
        }

        _lastUnresolvedUtc = DateTime.UtcNow;

        string level = _lastUnresolvedCount >= UnresolvedEscalationThreshold ? "YIELD" : "WAIT";
        Log(level + " " + DecisionStateExtractor.DescribeUnresolvedStepEvent(
            signature,
            _lastUnresolvedCount,
            DescribeStepSource(step, candidateIndex),
            candidateIndex,
            candidateCount));
    }

    private static DecisionTeacherActionStep GetPrimaryRequiredStep(DecisionTeacherHintState state)
    {
        if (state == null || state.ActionSteps == null || state.ActionSteps.Count == 0)
            return null;

        DecisionTeacherActionStep best = null;
        int minSequence = int.MaxValue;
        foreach (DecisionTeacherActionStep candidate in state.ActionSteps)
        {
            if (candidate == null || !candidate.IsRequired || string.IsNullOrWhiteSpace(candidate.ActionType))
                continue;

            int sequence = candidate.Sequence > 0 ? candidate.Sequence : 1;
            if (sequence < minSequence)
            {
                minSequence = sequence;
                best = candidate;
                continue;
            }

            if (sequence != minSequence)
                continue;

            if (best == null
                || candidate.Confidence > best.Confidence
                || (candidate.Confidence == best.Confidence && candidate.Score > best.Score))
            {
                best = candidate;
            }
        }

        return best;
    }

    private ClickInstruction BuildInstruction(Board board, DecisionTeacherActionStep step, DecisionTeacherHintState state)
    {
        string actionType = step.ActionType == null ? string.Empty : step.ActionType.Trim();
        if (string.Equals(actionType, "PlayCard", StringComparison.OrdinalIgnoreCase))
            return BuildPlayCardInstruction(board, step);

        if (string.Equals(actionType, "Attack", StringComparison.OrdinalIgnoreCase))
            return BuildAttackInstruction(board, step);

        if (string.Equals(actionType, "UseHeroPower", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "HeroPower", StringComparison.OrdinalIgnoreCase))
            return BuildHeroPowerInstruction(board, step);

        if (string.Equals(actionType, "UseLocation", StringComparison.OrdinalIgnoreCase))
            return BuildUseLocationInstruction(board, step);

        if (string.Equals(actionType, "Choose", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "Choice", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "DiscoverPick", StringComparison.OrdinalIgnoreCase))
            return BuildChooseInstruction(board, step, state);

        if (string.Equals(actionType, "EndTurn", StringComparison.OrdinalIgnoreCase))
        {
            Log("ACTION End() | step=endturn");
            return ClickInstruction.End();
        }

        Log("WAIT " + DecisionStateExtractor.DescribeUnsupportedActionWait(actionType));
        return null;
    }

    private ClickInstruction BuildPlayCardInstruction(Board board, DecisionTeacherActionStep step)
    {
        Card sourceCard = ResolveHandCard(board, step.SourceCardId, step.SourceSlot);
        if (sourceCard == null || sourceCard.Template == null)
            return null;

        int boardIndex = ResolveBoardIndex(board, step.BoardSlot, sourceCard);
        Card target = ResolveTarget(board, step.TargetKind, step.TargetCardId, step.TargetSlot);

        Log("ACTION " + DecisionStateExtractor.DescribePlayCardAction(
            sourceCard.Id,
            sourceCard.Template.Id,
            step.SourceSlot,
            boardIndex,
            DescribeTarget(step.TargetKind, target, step.TargetCardId)));

        if (target != null)
        {
            if (sourceCard.Type == Card.CType.MINION)
                return ClickInstruction.PlayCard(sourceCard.Id, boardIndex: boardIndex, targetEntityId: target.Id);
            return ClickInstruction.PlayCard(sourceCard.Id, targetEntityId: target.Id);
        }

        if (sourceCard.Type == Card.CType.MINION)
            return ClickInstruction.PlayCard(sourceCard.Id, boardIndex: boardIndex);

        return ClickInstruction.PlayCard(sourceCard.Id);
    }

    private ClickInstruction BuildAttackInstruction(Board board, DecisionTeacherActionStep step)
    {
        Card source = ResolveAttackSource(board, step.SourceKind, step.SourceCardId, step.SourceSlot);
        Card target = ResolveTarget(board, step.TargetKind, step.TargetCardId, step.TargetSlot);
        if (source == null || target == null)
        {
            Log("WAIT " + DecisionStateExtractor.DescribeAttackUnresolved(
                step.SourceKind,
                step.SourceCardId,
                step.SourceSlot,
                step.TargetKind,
                step.TargetCardId,
                step.TargetSlot,
                source != null ? source.Id : 0,
                target != null ? target.Id : 0));
            return null;
        }

        Log("ACTION " + DecisionStateExtractor.DescribeAttackAction(
            source.Id,
            source.Template != null ? source.Template.Id : default(Card.Cards),
            target.Id,
            DescribeTarget(step.TargetKind, target, step.TargetCardId),
            step.SourceSlot,
            step.TargetSlot));
        return ClickInstruction.Attack(source.Id, target.Id);
    }

    private ClickInstruction BuildHeroPowerInstruction(Board board, DecisionTeacherActionStep step)
    {
        if (!CanUseHeroPowerNow(board))
            return null;

        Card target = ResolveTarget(board, step.TargetKind, step.TargetCardId, step.TargetSlot);
        if (target != null)
        {
            Log("ACTION " + DecisionStateExtractor.DescribeHeroPowerAction(target.Id, DescribeTarget(step.TargetKind, target, step.TargetCardId)));
            return ClickInstruction.HeroPower(target.Id);
        }

        Log("ACTION " + DecisionStateExtractor.DescribeHeroPowerAction(0, null));
        return ClickInstruction.HeroPower();
    }

    private ClickInstruction BuildUseLocationInstruction(Board board, DecisionTeacherActionStep step)
    {
        Card location = ResolveFriendlyLocation(board, step.SourceCardId, step.SourceSlot);
        Card target = ResolveTarget(board, step.TargetKind, step.TargetCardId, step.TargetSlot);
        if (location == null || target == null)
            return null;

        Log("ACTION " + DecisionStateExtractor.DescribeUseLocationAction(
            location.Id,
            location.Template != null ? location.Template.Id : default(Card.Cards),
            target.Id,
            DescribeTarget(step.TargetKind, target, step.TargetCardId)));
        return ClickInstruction.UseLocation(location.Id, target.Id);
    }

    private ClickInstruction BuildChooseInstruction(Board board, DecisionTeacherActionStep step, DecisionTeacherHintState state)
    {
        int preferredChoiceIndex = 0;
        if (step != null && step.ChoiceIndex > 0)
            preferredChoiceIndex = step.ChoiceIndex;
        else if (state != null && state.PreferredChoiceIndex > 0)
            preferredChoiceIndex = state.PreferredChoiceIndex;

        List<Card.Cards> preferredCardIds = DecisionStateExtractor.GetPreferredChoiceCardIds(step, state);
        string preferenceSummary = DecisionStateExtractor.DescribeChoicePreference(preferredChoiceIndex, preferredCardIds);

        List<DecisionChoiceRuntimeOption> options;
        bool hasOptions = DecisionStateExtractor.TryGetCurrentChoiceOptions(board, out options) && options != null && options.Count > 0;
        Log("DISCOVER " + DecisionStateExtractor.DescribeChoiceOptionsSnapshot(options)
            + " " + preferenceSummary);

        if (hasOptions && preferredChoiceIndex > 0)
        {
            DecisionChoiceRuntimeOption indexedOption = options.FirstOrDefault(option => option != null && option.ChoiceIndex == preferredChoiceIndex);
            if (indexedOption != null)
            {
                if (indexedOption.EntityId > 0)
                {
                    Log("ACTION " + DecisionStateExtractor.DescribeChooseDecision(true, indexedOption.EntityId, "index->entity", preferredChoiceIndex, indexedOption.CardId));
                    return ClickInstruction.Choose(indexedOption.EntityId);
                }

                int choiceIndex = Math.Max(0, preferredChoiceIndex - 1);
                Log("ACTION " + DecisionStateExtractor.DescribeChooseDecision(false, choiceIndex, "index_match_without_entity", preferredChoiceIndex, indexedOption.CardId));
                return ClickInstruction.ChooseByIndex(choiceIndex);
            }

            Log("DISCOVER choice_index=" + preferredChoiceIndex + " | 未匹配到对应运行时选项，继续尝试按卡牌匹配");
        }

        if (hasOptions && preferredCardIds.Count > 0)
        {
            List<DecisionChoiceRuntimeOption> matches = options
                .Where(option => option != null && option.CardId != default(Card.Cards) && preferredCardIds.Contains(option.CardId))
                .ToList();

            if (matches.Count == 1)
            {
                DecisionChoiceRuntimeOption match = matches[0];
                if (match.EntityId > 0)
                {
                    Log("ACTION " + DecisionStateExtractor.DescribeChooseDecision(true, match.EntityId, "unique_card_match", preferredChoiceIndex, match.CardId));
                    return ClickInstruction.Choose(match.EntityId);
                }

                if (match.ChoiceIndex > 0)
                {
                    int choiceIndex = Math.Max(0, match.ChoiceIndex - 1);
                    Log("ACTION " + DecisionStateExtractor.DescribeChooseDecision(false, choiceIndex, "unique_card_match_without_entity", match.ChoiceIndex, match.CardId));
                    return ClickInstruction.ChooseByIndex(choiceIndex);
                }
            }
            else if (matches.Count > 1)
            {
                Log("DISCOVER preferred_card_matches=" + matches.Count + " | 卡牌匹配不唯一，回退 index");
            }
            else
            {
                Log("DISCOVER preferred_card_matches=0 | 没找到 discover_pick 对应运行时选项");
            }
        }

        if (preferredChoiceIndex > 0)
        {
            int choiceIndex = Math.Max(0, preferredChoiceIndex - 1);
            Log("ACTION " + DecisionStateExtractor.DescribeChooseDecision(false, choiceIndex, "fallback_index_only", preferredChoiceIndex, default(Card.Cards)));
            return ClickInstruction.ChooseByIndex(choiceIndex);
        }

        Log("DISCOVER unresolved | 无实体、无唯一卡牌匹配、无可用 choice_index");
        return null;
    }

    private static Card ResolveHandCard(Board board, Card.Cards cardId, int slot)
    {
        return DecisionStateExtractor.ResolveHandCard(board, cardId, slot, board != null ? board.ManaAvailable : 0);
    }

    private static Card ResolveAttackSource(Board board, string sourceKind, Card.Cards sourceCardId, int sourceSlot)
    {
        return DecisionStateExtractor.ResolveAttackSource(board, sourceKind, sourceCardId, sourceSlot);
    }

    private static Card ResolveFriendlyLocation(Board board, Card.Cards sourceCardId, int sourceSlot)
    {
        return DecisionStateExtractor.ResolveFriendlyLocation(board, sourceCardId, sourceSlot);
    }

    private static Card ResolveTarget(Board board, string targetKind, Card.Cards targetCardId, int targetSlot)
    {
        return DecisionStateExtractor.ResolveSemanticTarget(board, targetKind, targetCardId, targetSlot);
    }

    private static bool CanUseHeroPowerNow(Board board)
    {
        return DecisionCardGuards.CanUseHeroPowerNow(board, board != null ? board.ManaAvailable : 0);
    }

    private static bool IsPlayableHandCard(Board board, Card card)
    {
        return DecisionCardGuards.IsPlayableHandCard(board, card);
    }

    private static int ResolveBoardIndex(Board board, int teacherBoardSlot, Card sourceCard)
    {
        // 单文件 Profile 内部的落点计算实现在 DecisionCardGuards，避免引用不存在的 extractor 成员导致编译失败。
        return DecisionCardGuards.ResolveBoardIndex(board, teacherBoardSlot, sourceCard);
    }

    private static bool IsAttackSourceReady(Card card)
    {
        return DecisionCardGuards.IsAttackSourceReady(card);
    }

    private static bool IsLocationCard(Card card)
    {
        return DecisionCardGuards.IsLocationCard(card);
    }

    private static bool CardMatches(Card card, Card.Cards cardId)
    {
        return DecisionCardGuards.CardMatchesTemplate(card, cardId);
    }

    private static string SafeCurrentProfileName()
    {
        try
        {
            string profile = Bot.CurrentProfile();
            return string.IsNullOrWhiteSpace(profile) ? string.Empty : profile.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DescribeCard(Card.Cards cardId)
    {
        return DecisionStateExtractor.DescribeCardId(cardId);
    }

    private static string DescribeTarget(string targetKind, Card target, Card.Cards fallbackId)
    {
        return DecisionStateExtractor.DescribeTarget(targetKind, target, fallbackId);
    }

    private static string BuildActionSignature(DecisionTeacherActionStep step)
    {
        if (step == null)
            return string.Empty;

        return string.Join("|", new[]
        {
            step.ChainId ?? string.Empty,
            (step.Sequence > 0 ? step.Sequence : 1).ToString(CultureInfo.InvariantCulture),
            step.ActionType ?? string.Empty,
            step.SourceKind ?? string.Empty,
            step.SourceCardId.ToString(),
            step.SourceSlot.ToString(CultureInfo.InvariantCulture),
            step.BoardSlot.ToString(CultureInfo.InvariantCulture),
            step.ChoiceIndex.ToString(CultureInfo.InvariantCulture),
            step.TargetKind ?? string.Empty,
            step.TargetCardId.ToString(),
            step.TargetSlot.ToString(CultureInfo.InvariantCulture)
        });
    }

    // 单文件编译兼容：点击 Profile 运行时不会自动包含 SupportSources，
    // 因此在此内置最小决策状态模型与解析器，避免运行时编译缺少引用。
    private sealed class DecisionTeacherActionStep
    {
        public int Sequence;
        public string ChainId = string.Empty;
        public string PlanSource = string.Empty;
        public string ActionType = string.Empty;
        public bool IsRequired;
        public string SourceKind = string.Empty;
        public Card.Cards SourceCardId = default(Card.Cards);
        public int SourceSlot;
        public string DeliveryMode = string.Empty;
        public int BoardSlot;
        public int ChoiceIndex;
        public string TargetKind = string.Empty;
        public Card.Cards TargetCardId = default(Card.Cards);
        public int TargetSlot;
        public double Score;
        public double Confidence;
    }

    private sealed class DecisionChoiceRuntimeOption
    {
        public int EntityId;
        public int ChoiceIndex;
        public Card.Cards CardId = default(Card.Cards);
    }

    private sealed class DecisionTeacherHintState
    {
        public DateTime TimestampUtc = DateTime.MinValue;
        public string Status = string.Empty;
        public string Stage = string.Empty;
        public string SBProfile = string.Empty;
        public readonly List<string> Lines = new List<string>();
        public readonly List<DecisionTeacherActionStep> ActionSteps = new List<DecisionTeacherActionStep>();
        public bool HasDiscoverPick;
        public Card.Cards DiscoverPickId = default(Card.Cards);
        public int PreferredChoiceIndex;

        public bool IsFresh(int maxAgeSeconds)
        {
            if (TimestampUtc == DateTime.MinValue)
                return false;

            return TimestampUtc >= DateTime.UtcNow.AddSeconds(-Math.Max(1, maxAgeSeconds));
        }

        public bool MatchesProfileLoose(string expectedProfileName)
        {
            string expected = NormalizeProfileName(expectedProfileName);
            if (string.IsNullOrWhiteSpace(expected))
                return true;

            string actual = NormalizeProfileName(SBProfile);
            if (string.IsNullOrWhiteSpace(actual))
                return true;

            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        public bool WantsEndTurn()
        {
            if (Lines == null || Lines.Count == 0)
                return false;

            string text = NormalizeText(string.Join(" ", Lines));
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains(NormalizeText("结束回合"))
                || text.Contains(NormalizeText("回合结束"))
                || text.Contains("endturn")
                || text.Contains(NormalizeText("end turn"));
        }

        private static string NormalizeText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            return raw.Trim().Replace(" ", string.Empty).ToLowerInvariant();
        }

        private static string NormalizeProfileName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string normalized = raw.Trim().Replace('/', '\\');
            string fileName = Path.GetFileName(normalized);
            return string.IsNullOrWhiteSpace(fileName) ? normalized : fileName.Trim();
        }
    }

    private static class DecisionStateExtractor
    {
        private static readonly object Sync = new object();
        private static DateTime _lastTeacherLoadUtc = DateTime.MinValue;
        private static string _cachedTeacherRaw = string.Empty;
        private static DecisionTeacherHintState _cachedTeacherState;

        private static string TeacherStateFilePath
        {
            get
            {
                string runtimeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");
                string primaryPath = Path.Combine(runtimeDir, "decision_teacher_state.txt");
                string legacyPath = Path.Combine(runtimeDir, "netease_box_ocr_state.txt");

                if (File.Exists(primaryPath))
                    return primaryPath;

                if (File.Exists(legacyPath))
                    return legacyPath;

                return primaryPath;
            }
        }

        public static DecisionTeacherHintState LoadTeacherHint()
        {
            string path = TeacherStateFilePath;
            if (!File.Exists(path))
                return new DecisionTeacherHintState();

            try
            {
                string raw = File.ReadAllText(path);
                lock (Sync)
                {
                    if (_cachedTeacherState != null
                        && _cachedTeacherRaw == raw
                        && _lastTeacherLoadUtc.AddMilliseconds(800) > DateTime.UtcNow)
                    {
                        return _cachedTeacherState;
                    }

                    DecisionTeacherHintState parsed = ParseTeacherHintState(raw);
                    _cachedTeacherRaw = raw;
                    _cachedTeacherState = parsed;
                    _lastTeacherLoadUtc = DateTime.UtcNow;
                    return parsed;
                }
            }
            catch
            {
                return new DecisionTeacherHintState();
            }
        }

        public static List<Card.Cards> GetPreferredChoiceCardIds(DecisionTeacherActionStep step, DecisionTeacherHintState state)
        {
            List<Card.Cards> ids = new List<Card.Cards>();
            if (state != null && state.HasDiscoverPick)
                AddChoiceCardId(ids, state.DiscoverPickId);

            if (step != null)
            {
                AddChoiceCardId(ids, step.TargetCardId);

                string sourceKind = step.SourceKind ?? string.Empty;
                if (string.Equals(sourceKind, "choice", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sourceKind, "option", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sourceKind, "discover_choice", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sourceKind, "discover_option", StringComparison.OrdinalIgnoreCase))
                {
                    AddChoiceCardId(ids, step.SourceCardId);
                }
            }

            return ids;
        }

        public static DecisionTeacherActionStep TryBuildFallbackMatchStep(DecisionTeacherHintState state)
        {
            if (state == null)
                return null;

            DecisionTeacherActionStep heroPowerStep = null;
            DecisionTeacherActionStep attackStep = null;
            for (int i = 0; i < state.ActionSteps.Count; i++)
            {
                DecisionTeacherActionStep candidate = state.ActionSteps[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.ActionType))
                    continue;

                string actionType = candidate.ActionType.Trim();
                if (heroPowerStep == null
                    && (string.Equals(actionType, "UseHeroPower", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(actionType, "HeroPower", StringComparison.OrdinalIgnoreCase)))
                {
                    heroPowerStep = CloneStep(candidate, "teacher_match_fallback");
                }

                if (attackStep == null
                    && string.Equals(actionType, "Attack", StringComparison.OrdinalIgnoreCase))
                {
                    attackStep = CloneStep(candidate, "teacher_match_fallback");
                }
            }

            if (heroPowerStep != null)
                return heroPowerStep;
            if (attackStep != null)
                return attackStep;

            return null;
        }

        public static DecisionTeacherActionStep TryBuildFallbackLineStep(Board board, DecisionTeacherHintState state)
        {
            if (board == null || state == null || state.Lines == null || state.Lines.Count == 0)
                return null;

            string text = string.Join(" ", state.Lines).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            Card namedHandCard = ResolveNamedCard(board.Hand, text, card => DecisionCardGuards.IsPlayableHandCard(board, card));
            if (namedHandCard != null && namedHandCard.Template != null)
            {
                DecisionTeacherActionStep step = new DecisionTeacherActionStep();
                step.Sequence = 1;
                step.PlanSource = "teacher_line_fallback";
                step.ActionType = "PlayCard";
                step.SourceKind = "card";
                step.SourceCardId = namedHandCard.Template.Id;
                step.SourceSlot = ResolveHandSlot(board, namedHandCard);
                step.Score = 1d;

                string targetKind;
                Card target;
                if (TryResolveNamedTarget(board, text, out targetKind, out target))
                {
                    step.TargetKind = targetKind;
                    step.TargetCardId = target != null && target.Template != null ? target.Template.Id : default(Card.Cards);
                    step.TargetSlot = ResolveTargetSlot(board, targetKind, target);
                }

                return step;
            }

            if (text.IndexOf("技能", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("英雄技能", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("hero power", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                DecisionTeacherActionStep heroPower = new DecisionTeacherActionStep();
                heroPower.Sequence = 1;
                heroPower.PlanSource = "teacher_line_fallback";
                heroPower.ActionType = "UseHeroPower";
                heroPower.SourceKind = "hero_power";
                heroPower.Score = 1d;

                string targetKind;
                Card target;
                if (TryResolveNamedTarget(board, text, out targetKind, out target))
                {
                    heroPower.TargetKind = targetKind;
                    heroPower.TargetCardId = target != null && target.Template != null ? target.Template.Id : default(Card.Cards);
                    heroPower.TargetSlot = ResolveTargetSlot(board, targetKind, target);
                }

                return heroPower;
            }

            return null;
        }

        public static bool TryGetCurrentChoiceOptions(Board board, out List<DecisionChoiceRuntimeOption> options)
        {
            options = new List<DecisionChoiceRuntimeOption>();
            if (board == null)
                return false;

            string[] candidatePropertyNames = new[]
            {
                "Choices",
                "ChoiceCards",
                "DiscoverChoices",
                "DiscoverCards",
                "CurrentChoices"
            };

            for (int i = 0; i < candidatePropertyNames.Length; i++)
            {
                object raw = TryGetPropertyObject(board, candidatePropertyNames[i]);
                if (raw == null)
                    continue;

                if (TryConvertChoiceCollection(raw, options) && options.Count > 0)
                    return true;

                options.Clear();
            }

            return false;
        }

        public static string DescribeChoiceOption(DecisionChoiceRuntimeOption option)
        {
            if (option == null)
                return "null";

            return "idx=" + option.ChoiceIndex.ToString(CultureInfo.InvariantCulture)
                + "/entity=" + option.EntityId.ToString(CultureInfo.InvariantCulture)
                + "/card=" + DescribeCardId(option.CardId);
        }

        public static string DescribeChoicePreference(int preferredChoiceIndex, IEnumerable<Card.Cards> preferredCardIds)
        {
            List<Card.Cards> ids = preferredCardIds == null
                ? new List<Card.Cards>()
                : preferredCardIds.Where(id => id != default(Card.Cards)).Distinct().ToList();
            string preferredCardSummary = ids.Count > 0
                ? string.Join(",", ids.Select(DescribeCardId))
                : "none";
            return "choice_index=" + preferredChoiceIndex.ToString(CultureInfo.InvariantCulture)
                + " preferred_cards=" + preferredCardSummary;
        }

        public static string DescribeChoiceOptionsSnapshot(IList<DecisionChoiceRuntimeOption> options)
        {
            if (options == null || options.Count == 0)
                return "options=0 | 未暴露可反射 choice 集合";

            return "options=" + options.Count.ToString(CultureInfo.InvariantCulture)
                + " | " + string.Join(" | ", options.Where(option => option != null).Select(DescribeChoiceOption));
        }

        public static string DescribeUnresolvedStepEvent(string signature, int unresolvedCount, string source, int candidateIndex, int candidateCount)
        {
            return "unresolved=" + (signature ?? string.Empty)
                + " | count=" + Math.Max(0, unresolvedCount).ToString(CultureInfo.InvariantCulture)
                + " | source=" + (source ?? string.Empty)
                + " | index=" + Math.Max(1, candidateIndex + 1).ToString(CultureInfo.InvariantCulture)
                + "/" + Math.Max(1, candidateCount).ToString(CultureInfo.InvariantCulture);
        }

        public static string DescribeUnresolvedSuppressedEvent(string signature, int unresolvedCount)
        {
            return "unresolved_suppressed=" + (signature ?? string.Empty)
                + " | count=" + Math.Max(0, unresolvedCount).ToString(CultureInfo.InvariantCulture);
        }

        public static string DescribeStepPreview(string signature, bool isRequired, double score, double confidence, string source)
        {
            return (signature ?? string.Empty)
                + " | required=" + (isRequired ? "1" : "0")
                + " | score=" + score.ToString("0.000", CultureInfo.InvariantCulture)
                + " | confidence=" + confidence.ToString("0.000", CultureInfo.InvariantCulture)
                + " | source=" + (source ?? string.Empty);
        }

        public static string DescribeChooseDecision(bool useEntity, int value, string reason, int preferredChoiceIndex, Card.Cards cardId)
        {
            string actionName = useEntity ? "Choose" : "ChooseByIndex";
            string message = actionName + "(" + value.ToString(CultureInfo.InvariantCulture) + ")"
                + " | reason=" + (reason ?? string.Empty);
            if (preferredChoiceIndex > 0)
                message += " choice_index=" + preferredChoiceIndex.ToString(CultureInfo.InvariantCulture);
            if (cardId != default(Card.Cards))
                message += " card=" + DescribeCardId(cardId);
            return message;
        }

        public static string DescribePlayCardAction(int entityId, Card.Cards cardId, int sourceSlot, int boardIndex, string targetDescription)
        {
            return "PlayCard id=" + entityId.ToString(CultureInfo.InvariantCulture)
                + " card=" + DescribeCardId(cardId)
                + " slot=" + sourceSlot.ToString(CultureInfo.InvariantCulture)
                + " boardIndex=" + boardIndex.ToString(CultureInfo.InvariantCulture)
                + " target=" + (targetDescription ?? "none");
        }

        public static string DescribeAttackAction(int sourceEntityId, Card.Cards sourceCardId, int targetEntityId, string targetDescription, int sourceSlot, int targetSlot)
        {
            return "Attack source=" + sourceEntityId.ToString(CultureInfo.InvariantCulture)
                + " card=" + DescribeCardId(sourceCardId)
                + " target=" + targetEntityId.ToString(CultureInfo.InvariantCulture)
                + " -> " + (targetDescription ?? "none")
                + " | sourceSlot=" + sourceSlot.ToString(CultureInfo.InvariantCulture)
                + " | targetSlot=" + targetSlot.ToString(CultureInfo.InvariantCulture);
        }

        public static string DescribeHeroPowerAction(int targetEntityId, string targetDescription)
        {
            if (targetEntityId > 0)
                return "HeroPower target=" + targetEntityId.ToString(CultureInfo.InvariantCulture) + " -> " + (targetDescription ?? "none");
            return "HeroPower()";
        }

        public static string DescribeUseLocationAction(int sourceEntityId, Card.Cards sourceCardId, int targetEntityId, string targetDescription)
        {
            return "UseLocation source=" + sourceEntityId.ToString(CultureInfo.InvariantCulture)
                + " card=" + DescribeCardId(sourceCardId)
                + " target=" + targetEntityId.ToString(CultureInfo.InvariantCulture)
                + " -> " + (targetDescription ?? "none");
        }

        public static string DescribeAttackUnresolved(string sourceKind, Card.Cards sourceCardId, int sourceSlot, string targetKind, Card.Cards targetCardId, int targetSlot, int resolvedSourceEntityId, int resolvedTargetEntityId)
        {
            return "attack unresolved | sourceKind=" + (sourceKind ?? string.Empty)
                + " sourceId=" + sourceCardId
                + " sourceSlot=" + sourceSlot.ToString(CultureInfo.InvariantCulture)
                + " targetKind=" + (targetKind ?? string.Empty)
                + " targetId=" + targetCardId
                + " targetSlot=" + targetSlot.ToString(CultureInfo.InvariantCulture)
                + " resolvedSource=" + resolvedSourceEntityId.ToString(CultureInfo.InvariantCulture)
                + " resolvedTarget=" + resolvedTargetEntityId.ToString(CultureInfo.InvariantCulture);
        }

        public static string DescribeDedupWait(string signature)
        {
            return "dedup=" + (signature ?? string.Empty);
        }

        public static string DescribeEndTurnSuppressedWait(bool unresolvedAttackIntent)
        {
            return "teacher=endturn_suppressed | unresolved_attack=" + (unresolvedAttackIntent ? "1" : "0");
        }

        public static string DescribeUnsupportedActionWait(string actionType)
        {
            return "unsupported action=" + (actionType ?? string.Empty);
        }

        public static string DescribeCardId(Card.Cards cardId)
        {
            try
            {
                CardTemplate template = CardTemplate.LoadFromId(cardId);
                if (template != null)
                {
                    if (!string.IsNullOrWhiteSpace(template.NameCN))
                        return template.NameCN + "(" + cardId + ")";
                    if (!string.IsNullOrWhiteSpace(template.Name))
                        return template.Name + "(" + cardId + ")";
                }
            }
            catch
            {
            }

            return cardId.ToString();
        }

        public static string DescribeTarget(string targetKind, Card target, Card.Cards fallbackId)
        {
            if (target != null && target.Template != null)
                return DescribeCardId(target.Template.Id);

            string normalizedKind = (targetKind ?? string.Empty).Trim();
            if (string.Equals(normalizedKind, "enemy_hero", StringComparison.OrdinalIgnoreCase))
                return "enemy_hero";

            if (string.Equals(normalizedKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedKind, "own_hero", StringComparison.OrdinalIgnoreCase))
            {
                return "friendly_hero";
            }

            if (fallbackId != default(Card.Cards))
                return DescribeCardId(fallbackId);

            return "none";
        }

        private static DecisionTeacherActionStep CloneStep(DecisionTeacherActionStep step, string planSource)
        {
            if (step == null)
                return null;

            DecisionTeacherActionStep clone = new DecisionTeacherActionStep();
            clone.Sequence = step.Sequence;
            clone.ChainId = step.ChainId ?? string.Empty;
            clone.PlanSource = string.IsNullOrWhiteSpace(planSource) ? (step.PlanSource ?? string.Empty) : planSource;
            clone.ActionType = step.ActionType ?? string.Empty;
            clone.IsRequired = step.IsRequired;
            clone.SourceKind = step.SourceKind ?? string.Empty;
            clone.SourceCardId = step.SourceCardId;
            clone.SourceSlot = step.SourceSlot;
            clone.DeliveryMode = step.DeliveryMode ?? string.Empty;
            clone.BoardSlot = step.BoardSlot;
            clone.ChoiceIndex = step.ChoiceIndex;
            clone.TargetKind = step.TargetKind ?? string.Empty;
            clone.TargetCardId = step.TargetCardId;
            clone.TargetSlot = step.TargetSlot;
            clone.Score = step.Score;
            clone.Confidence = step.Confidence;
            return clone;
        }

        private static Card ResolveNamedCard(IEnumerable<Card> cards, string rawText, Func<Card, bool> predicate)
        {
            if (cards == null || string.IsNullOrWhiteSpace(rawText))
                return null;

            string normalizedText = NormalizeLooseText(rawText);
            if (string.IsNullOrWhiteSpace(normalizedText))
                return null;

            List<Card> matches = new List<Card>();
            foreach (Card card in cards)
            {
                if (card == null || card.Template == null)
                    continue;
                if (predicate != null && !predicate(card))
                    continue;

                string cardName = GetCardMatchName(card.Template);
                if (string.IsNullOrWhiteSpace(cardName))
                    continue;

                if (normalizedText.Contains(cardName))
                    matches.Add(card);
            }

            if (matches.Count == 1)
                return matches[0];

            return null;
        }

        private static bool TryResolveNamedTarget(Board board, string rawText, out string targetKind, out Card target)
        {
            targetKind = string.Empty;
            target = null;
            if (board == null || string.IsNullOrWhiteSpace(rawText))
                return false;

            string normalizedText = NormalizeLooseText(rawText);
            if (string.IsNullOrWhiteSpace(normalizedText))
                return false;

            Card matchedEnemy = ResolveNamedCard(board.MinionEnemy, normalizedText, card => card != null && card.Template != null);
            if (matchedEnemy != null)
            {
                targetKind = "enemy_minion";
                target = matchedEnemy;
                return true;
            }

            Card matchedFriendly = ResolveNamedCard(board.MinionFriend, normalizedText, card => card != null && card.Template != null);
            if (matchedFriendly != null)
            {
                targetKind = "friendly_minion";
                target = matchedFriendly;
                return true;
            }

            if (normalizedText.Contains(NormalizeLooseText("敌方英雄"))
                || normalizedText.Contains(NormalizeLooseText("对方英雄"))
                || normalizedText.Contains(NormalizeLooseText("敌方脸"))
                || normalizedText.Contains(NormalizeLooseText("enemyhero")))
            {
                targetKind = "enemy_hero";
                target = board.HeroEnemy;
                return target != null;
            }

            if (normalizedText.Contains(NormalizeLooseText("我方英雄"))
                || normalizedText.Contains(NormalizeLooseText("己方英雄"))
                || normalizedText.Contains(NormalizeLooseText("friendlyhero")))
            {
                targetKind = "friendly_hero";
                target = board.HeroFriend;
                return target != null;
            }

            return false;
        }

        private static int ResolveHandSlot(Board board, Card card)
        {
            if (board == null || board.Hand == null || card == null)
                return 0;

            for (int i = 0; i < board.Hand.Count; i++)
            {
                if (board.Hand[i] != null && board.Hand[i].Id == card.Id)
                    return i + 1;
            }

            return 0;
        }

        private static int ResolveTargetSlot(Board board, string targetKind, Card target)
        {
            if (board == null || target == null)
                return 0;

            IList<Card> cards = null;
            if (string.Equals(targetKind, "enemy_minion", StringComparison.OrdinalIgnoreCase))
                cards = board.MinionEnemy;
            else if (string.Equals(targetKind, "friendly_minion", StringComparison.OrdinalIgnoreCase))
                cards = board.MinionFriend;

            if (cards == null)
                return 0;

            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null && cards[i].Id == target.Id)
                    return i + 1;
            }

            return 0;
        }

        private static string NormalizeLooseText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            return new string(raw
                .Trim()
                .ToLowerInvariant()
                .Where(ch => !char.IsWhiteSpace(ch) && !char.IsPunctuation(ch) && !char.IsSymbol(ch))
                .ToArray());
        }

        private static string GetCardMatchName(CardTemplate template)
        {
            if (template == null)
                return string.Empty;

            string preferred = !string.IsNullOrWhiteSpace(template.NameCN) ? template.NameCN : template.Name;
            return NormalizeLooseText(preferred);
        }

        // 单文件 Profile 需要自带手牌/攻击源解析，避免依赖 SupportSources 才能编译。
        public static Card ResolveHandCard(Board board, Card.Cards cardId, int slot, int availableMana)
        {
            if (board == null || board.Hand == null || cardId == default(Card.Cards))
                return null;

            if (slot > 0 && slot <= board.Hand.Count)
            {
                Card bySlot = board.Hand[slot - 1];
                if (DecisionCardGuards.IsPlayableHandCard(board, bySlot, availableMana) && DecisionCardGuards.CardMatchesTemplate(bySlot, cardId))
                    return bySlot;
            }

            List<Card> matches = board.Hand
                .Where(card => DecisionCardGuards.IsPlayableHandCard(board, card, availableMana) && DecisionCardGuards.CardMatchesTemplate(card, cardId))
                .ToList();

            if (matches.Count == 1)
                return matches[0];

            return null;
        }

        // 单文件 Profile 需要自带攻击源解析，避免依赖 SupportSources 才能编译。
        public static Card ResolveAttackSource(Board board, string sourceKind, Card.Cards sourceCardId, int sourceSlot)
        {
            if (board == null || string.IsNullOrWhiteSpace(sourceKind))
                return null;

            if (string.Equals(sourceKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sourceKind, "own_hero", StringComparison.OrdinalIgnoreCase))
            {
                if (board.HeroFriend != null && board.HeroFriend.CanAttack && !board.HeroFriend.IsFrozen)
                    return board.HeroFriend;
                return null;
            }

            if (!string.Equals(sourceKind, "friendly_minion", StringComparison.OrdinalIgnoreCase) || board.MinionFriend == null)
                return null;

            if (sourceSlot > 0 && sourceSlot <= board.MinionFriend.Count)
            {
                Card bySlot = board.MinionFriend[sourceSlot - 1];
                if (DecisionCardGuards.IsAttackSourceReady(bySlot) && DecisionCardGuards.CardMatchesTemplate(bySlot, sourceCardId))
                    return bySlot;
            }

            List<Card> matches = board.MinionFriend
                .Where(card => DecisionCardGuards.IsAttackSourceReady(card) && DecisionCardGuards.CardMatchesTemplate(card, sourceCardId))
                .ToList();

            if (matches.Count == 1)
                return matches[0];

            if (matches.Count > 1 && sourceSlot > 0)
            {
                int bestDistance = int.MaxValue;
                Card best = null;
                bool tie = false;
                foreach (Card match in matches)
                {
                    int liveSlot = board.MinionFriend.IndexOf(match) + 1;
                    int distance = Math.Abs(liveSlot - sourceSlot);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = match;
                        tie = false;
                    }
                    else if (distance == bestDistance)
                    {
                        tie = true;
                    }
                }

                if (!tie)
                    return best;
            }

            return null;
        }

        // 单文件 Profile 需要自带位置与目标解析，避免依赖 SupportSources 才能编译。
        public static Card ResolveFriendlyLocation(Board board, Card.Cards sourceCardId, int sourceSlot)
        {
            if (board == null || board.MinionFriend == null || board.MinionFriend.Count == 0)
                return null;

            if (sourceSlot > 0 && sourceSlot <= board.MinionFriend.Count)
            {
                Card bySlot = board.MinionFriend[sourceSlot - 1];
                if (DecisionCardGuards.IsLocationCard(bySlot) && DecisionCardGuards.CardMatchesTemplate(bySlot, sourceCardId))
                    return bySlot;
            }

            List<Card> matches = board.MinionFriend
                .Where(card => DecisionCardGuards.IsLocationCard(card) && DecisionCardGuards.CardMatchesTemplate(card, sourceCardId))
                .ToList();

            if (matches.Count == 1)
                return matches[0];

            return null;
        }

        // 单文件 Profile 需要自带语义目标解析，避免依赖 SupportSources 才能编译。
        public static Card ResolveSemanticTarget(Board board, string targetKind, Card.Cards targetCardId, int targetSlot)
        {
            if (board == null || string.IsNullOrWhiteSpace(targetKind))
                return null;

            if (string.Equals(targetKind, "enemy_hero", StringComparison.OrdinalIgnoreCase))
                return board.HeroEnemy;

            if (string.Equals(targetKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetKind, "own_hero", StringComparison.OrdinalIgnoreCase))
                return board.HeroFriend;

            IList<Card> cards = null;
            if (string.Equals(targetKind, "enemy_minion", StringComparison.OrdinalIgnoreCase))
                cards = board.MinionEnemy;
            else if (string.Equals(targetKind, "friendly_minion", StringComparison.OrdinalIgnoreCase))
                cards = board.MinionFriend;

            if (cards == null)
                return null;

            List<Card> matches = cards
                .Where(card => card != null
                    && card.Template != null
                    && (targetCardId == default(Card.Cards) || card.Template.Id == targetCardId))
                .ToList();

            if (targetSlot > 0 && targetSlot <= cards.Count)
            {
                Card bySlot = cards[targetSlot - 1];
                if (bySlot != null
                    && bySlot.Template != null
                    && (targetCardId == default(Card.Cards) || bySlot.Template.Id == targetCardId))
                {
                    return bySlot;
                }
            }

            if (targetCardId == default(Card.Cards))
                return null;

            if (matches.Count == 1)
                return matches[0];

            if (matches.Count > 1 && targetSlot > 0)
            {
                int bestDistance = int.MaxValue;
                Card best = null;
                bool tie = false;
                foreach (Card match in matches)
                {
                    int liveSlot = cards.IndexOf(match) + 1;
                    int distance = Math.Abs(liveSlot - targetSlot);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = match;
                        tie = false;
                    }
                    else if (distance == bestDistance)
                    {
                        tie = true;
                    }
                }

                if (!tie)
                    return best;
            }

            return null;
        }

        private static DecisionTeacherHintState ParseTeacherHintState(string raw)
        {
            DecisionTeacherHintState state = new DecisionTeacherHintState();
            if (string.IsNullOrWhiteSpace(raw))
                return state;

            int pendingActionSequence = 0;
            string pendingActionChainId = string.Empty;
            string pendingActionType = string.Empty;
            bool pendingActionRequired = false;
            string pendingSourceKind = string.Empty;
            Card.Cards pendingSourceCardId = default(Card.Cards);
            bool hasPendingSourceCardId = false;
            int pendingSourceSlot = 0;
            string pendingActionDelivery = string.Empty;
            int pendingActionBoardSlot = 0;
            int pendingActionChoiceIndex = 0;
            string pendingTargetKind = string.Empty;
            Card.Cards pendingTargetCardId = default(Card.Cards);
            bool hasPendingTargetCardId = false;
            int pendingTargetSlot = 0;
            double pendingActionScore = 0;
            double pendingActionConfidence = 0;

            string[] lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine ?? string.Empty;

                if (line.StartsWith("action_step_index=", StringComparison.OrdinalIgnoreCase))
                {
                    FinalizePendingActionStep(
                        state,
                        ref pendingActionSequence,
                        ref pendingActionChainId,
                        ref pendingActionType,
                        ref pendingActionRequired,
                        ref pendingSourceKind,
                        ref pendingSourceCardId,
                        ref hasPendingSourceCardId,
                        ref pendingSourceSlot,
                        ref pendingActionDelivery,
                        ref pendingActionBoardSlot,
                        ref pendingActionChoiceIndex,
                        ref pendingTargetKind,
                        ref pendingTargetCardId,
                        ref hasPendingTargetCardId,
                        ref pendingTargetSlot,
                        ref pendingActionScore,
                        ref pendingActionConfidence);

                    int actionIndex;
                    if (!int.TryParse(line.Substring("action_step_index=".Length).Trim(), out actionIndex))
                        actionIndex = 0;
                    pendingActionSequence = actionIndex;
                    pendingActionChainId = string.Empty;
                    pendingActionType = string.Empty;
                    pendingActionRequired = false;
                    pendingSourceKind = string.Empty;
                    pendingSourceCardId = default(Card.Cards);
                    hasPendingSourceCardId = false;
                    pendingSourceSlot = 0;
                    pendingActionDelivery = string.Empty;
                    pendingActionBoardSlot = 0;
                    pendingActionChoiceIndex = 0;
                    pendingTargetKind = string.Empty;
                    pendingTargetCardId = default(Card.Cards);
                    hasPendingTargetCardId = false;
                    pendingTargetSlot = 0;
                    pendingActionScore = 0;
                    pendingActionConfidence = 0;
                    continue;
                }

                int idx = line.IndexOf('=');
                if (idx <= 0)
                    continue;

                string key = line.Substring(0, idx).Trim();
                string value = line.Substring(idx + 1).Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (string.Equals(key, "status", StringComparison.OrdinalIgnoreCase))
                {
                    state.Status = value;
                }
                else if (string.Equals(key, "stage", StringComparison.OrdinalIgnoreCase))
                {
                    state.Stage = value;
                }
                else if (string.Equals(key, "sb_profile", StringComparison.OrdinalIgnoreCase))
                {
                    state.SBProfile = value;
                }
                else if (string.Equals(key, "ts_utc", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime parsedUtc;
                    if (DateTime.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out parsedUtc))
                    {
                        state.TimestampUtc = parsedUtc;
                    }
                }
                else if (string.Equals(key, "line", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        state.Lines.Add(value);
                }
                else if (string.Equals(key, "discover_pick_id", StringComparison.OrdinalIgnoreCase))
                {
                    Card.Cards cardId;
                    if (TryParseCardId(value, out cardId))
                    {
                        state.DiscoverPickId = cardId;
                        state.HasDiscoverPick = true;
                    }
                }
                else if (string.Equals(key, "preferred_choice_index", StringComparison.OrdinalIgnoreCase))
                {
                    int preferredChoiceIndex;
                    if (!int.TryParse(value, out preferredChoiceIndex))
                        preferredChoiceIndex = 0;
                    if (preferredChoiceIndex > 0)
                        state.PreferredChoiceIndex = preferredChoiceIndex;
                }
                else if (string.Equals(key, "action_type", StringComparison.OrdinalIgnoreCase))
                {
                    pendingActionType = value;
                }
                else if (string.Equals(key, "action_chain_id", StringComparison.OrdinalIgnoreCase))
                {
                    pendingActionChainId = value;
                }
                else if (string.Equals(key, "action_required", StringComparison.OrdinalIgnoreCase))
                {
                    pendingActionRequired = string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
                }
                else if (string.Equals(key, "action_source_kind", StringComparison.OrdinalIgnoreCase))
                {
                    pendingSourceKind = value;
                }
                else if (string.Equals(key, "action_source_id", StringComparison.OrdinalIgnoreCase))
                {
                    Card.Cards parsedActionSourceId;
                    if (TryParseCardId(value, out parsedActionSourceId))
                    {
                        pendingSourceCardId = parsedActionSourceId;
                        hasPendingSourceCardId = true;
                    }
                }
                else if (string.Equals(key, "action_source_slot", StringComparison.OrdinalIgnoreCase))
                {
                    int actionSourceSlot;
                    if (!int.TryParse(value, out actionSourceSlot))
                        actionSourceSlot = 0;
                    pendingSourceSlot = actionSourceSlot;
                }
                else if (string.Equals(key, "action_delivery", StringComparison.OrdinalIgnoreCase))
                {
                    pendingActionDelivery = value;
                }
                else if (string.Equals(key, "action_board_slot", StringComparison.OrdinalIgnoreCase))
                {
                    int actionBoardSlot;
                    if (!int.TryParse(value, out actionBoardSlot))
                        actionBoardSlot = 0;
                    pendingActionBoardSlot = actionBoardSlot;
                }
                else if (string.Equals(key, "action_choice_index", StringComparison.OrdinalIgnoreCase))
                {
                    int choiceIndex;
                    if (!int.TryParse(value, out choiceIndex))
                        choiceIndex = 0;
                    pendingActionChoiceIndex = choiceIndex;
                    if (state.PreferredChoiceIndex <= 0 && choiceIndex > 0)
                        state.PreferredChoiceIndex = choiceIndex;
                }
                else if (string.Equals(key, "action_target_kind", StringComparison.OrdinalIgnoreCase))
                {
                    pendingTargetKind = value;
                }
                else if (string.Equals(key, "action_target_id", StringComparison.OrdinalIgnoreCase))
                {
                    Card.Cards parsedActionTargetId;
                    if (TryParseCardId(value, out parsedActionTargetId))
                    {
                        pendingTargetCardId = parsedActionTargetId;
                        hasPendingTargetCardId = true;
                    }
                }
                else if (string.Equals(key, "action_target_slot", StringComparison.OrdinalIgnoreCase))
                {
                    int actionTargetSlot;
                    if (!int.TryParse(value, out actionTargetSlot))
                        actionTargetSlot = 0;
                    pendingTargetSlot = actionTargetSlot;
                }
                else if (string.Equals(key, "action_score", StringComparison.OrdinalIgnoreCase))
                {
                    double actionScore;
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out actionScore))
                        pendingActionScore = actionScore;
                }
                else if (string.Equals(key, "action_confidence", StringComparison.OrdinalIgnoreCase))
                {
                    double actionConfidence;
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out actionConfidence))
                        pendingActionConfidence = actionConfidence;
                }
            }

            FinalizePendingActionStep(
                state,
                ref pendingActionSequence,
                ref pendingActionChainId,
                ref pendingActionType,
                ref pendingActionRequired,
                ref pendingSourceKind,
                ref pendingSourceCardId,
                ref hasPendingSourceCardId,
                ref pendingSourceSlot,
                ref pendingActionDelivery,
                ref pendingActionBoardSlot,
                ref pendingActionChoiceIndex,
                ref pendingTargetKind,
                ref pendingTargetCardId,
                ref hasPendingTargetCardId,
                ref pendingTargetSlot,
                ref pendingActionScore,
                ref pendingActionConfidence);
            return state;
        }

        private static void FinalizePendingActionStep(
            DecisionTeacherHintState state,
            ref int pendingActionSequence,
            ref string pendingActionChainId,
            ref string pendingActionType,
            ref bool pendingActionRequired,
            ref string pendingSourceKind,
            ref Card.Cards pendingSourceCardId,
            ref bool hasPendingSourceCardId,
            ref int pendingSourceSlot,
            ref string pendingActionDelivery,
            ref int pendingActionBoardSlot,
            ref int pendingActionChoiceIndex,
            ref string pendingTargetKind,
            ref Card.Cards pendingTargetCardId,
            ref bool hasPendingTargetCardId,
            ref int pendingTargetSlot,
            ref double pendingActionScore,
            ref double pendingActionConfidence)
        {
            if (state == null || string.IsNullOrWhiteSpace(pendingActionType))
                return;

            bool hasEnoughInfo = hasPendingSourceCardId
                || pendingActionChoiceIndex > 0
                || string.Equals(pendingActionType, "EndTurn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pendingActionType, "Choose", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pendingActionType, "DiscoverPick", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pendingActionType, "Choice", StringComparison.OrdinalIgnoreCase);
            if (!hasEnoughInfo)
                return;

            DecisionTeacherActionStep step = new DecisionTeacherActionStep();
            step.Sequence = pendingActionSequence;
            step.ChainId = pendingActionChainId ?? string.Empty;
            step.PlanSource = "teacher_step";
            step.ActionType = pendingActionType ?? string.Empty;
            step.IsRequired = pendingActionRequired;
            step.SourceKind = pendingSourceKind ?? string.Empty;
            step.SourceCardId = pendingSourceCardId;
            step.SourceSlot = pendingSourceSlot;
            step.DeliveryMode = pendingActionDelivery ?? string.Empty;
            step.BoardSlot = pendingActionBoardSlot;
            step.ChoiceIndex = pendingActionChoiceIndex;
            step.TargetKind = pendingTargetKind ?? string.Empty;
            step.TargetCardId = hasPendingTargetCardId ? pendingTargetCardId : default(Card.Cards);
            step.TargetSlot = pendingTargetSlot;
            step.Score = pendingActionScore;
            step.Confidence = pendingActionConfidence > 0 ? pendingActionConfidence : pendingActionScore;
            state.ActionSteps.Add(step);

            pendingActionSequence = 0;
            pendingActionChainId = string.Empty;
            pendingActionType = string.Empty;
            pendingActionRequired = false;
            pendingSourceKind = string.Empty;
            pendingSourceCardId = default(Card.Cards);
            hasPendingSourceCardId = false;
            pendingSourceSlot = 0;
            pendingActionDelivery = string.Empty;
            pendingActionBoardSlot = 0;
            pendingActionChoiceIndex = 0;
            pendingTargetKind = string.Empty;
            pendingTargetCardId = default(Card.Cards);
            hasPendingTargetCardId = false;
            pendingTargetSlot = 0;
            pendingActionScore = 0;
            pendingActionConfidence = 0;
        }

        private static void AddChoiceCardId(List<Card.Cards> ids, Card.Cards cardId)
        {
            if (ids == null || cardId == default(Card.Cards) || ids.Contains(cardId))
                return;

            ids.Add(cardId);
        }

        private static bool TryConvertChoiceCollection(object raw, List<DecisionChoiceRuntimeOption> output)
        {
            if (raw == null || output == null)
                return false;

            System.Collections.IEnumerable enumerable = raw as System.Collections.IEnumerable;
            if (enumerable == null)
                return false;

            bool addedAny = false;
            int choiceIndex = 0;
            foreach (object item in enumerable)
            {
                choiceIndex++;
                DecisionChoiceRuntimeOption option;
                if (!TryResolveChoiceOption(item, choiceIndex, out option))
                    continue;

                output.Add(option);
                addedAny = true;
            }

            return addedAny;
        }

        private static bool TryResolveChoiceOption(object item, int choiceIndex, out DecisionChoiceRuntimeOption option)
        {
            option = null;
            if (item == null)
                return false;

            option = new DecisionChoiceRuntimeOption();
            option.ChoiceIndex = choiceIndex;

            Card card = item as Card;
            if (card != null)
            {
                option.EntityId = card.Id;
                if (card.Template != null)
                    option.CardId = card.Template.Id;
                return option.EntityId > 0 || option.CardId != default(Card.Cards);
            }

            if (item is Card.Cards)
            {
                option.CardId = (Card.Cards)item;
                return true;
            }

            PopulateChoiceOptionFromObject(item, option);

            object cardObj = TryGetPropertyObject(item, "Card");
            if (cardObj != null)
                PopulateChoiceOptionFromObject(cardObj, option);

            return option.EntityId > 0 || option.CardId != default(Card.Cards);
        }

        private static void PopulateChoiceOptionFromObject(object item, DecisionChoiceRuntimeOption option)
        {
            if (item == null || option == null)
                return;

            object templateObj = TryGetPropertyObject(item, "Template");
            if (templateObj != null)
            {
                object templateIdObj = TryGetPropertyObject(templateObj, "Id");
                if (templateIdObj is Card.Cards)
                    option.CardId = (Card.Cards)templateIdObj;
            }

            if (option.EntityId <= 0)
            {
                object entityIdObj = TryGetPropertyObject(item, "EntityId");
                option.EntityId = TryConvertToInt(entityIdObj, option.EntityId);
            }

            if (option.EntityId <= 0)
            {
                object entityIdObj = TryGetPropertyObject(item, "EntityID");
                option.EntityId = TryConvertToInt(entityIdObj, option.EntityId);
            }

            object itemIdObj = TryGetPropertyObject(item, "Id");
            if (itemIdObj is Card.Cards)
            {
                if (option.CardId == default(Card.Cards))
                    option.CardId = (Card.Cards)itemIdObj;
            }
            else if (option.EntityId <= 0)
            {
                option.EntityId = TryConvertToInt(itemIdObj, option.EntityId);
            }

            if (option.CardId == default(Card.Cards))
            {
                object cardIdObj = TryGetPropertyObject(item, "CardId");
                if (cardIdObj is Card.Cards)
                    option.CardId = (Card.Cards)cardIdObj;
            }
        }

        private static object TryGetPropertyObject(object obj, string propertyName)
        {
            try
            {
                if (obj == null || string.IsNullOrWhiteSpace(propertyName))
                    return null;

                var prop = obj.GetType().GetProperty(propertyName);
                if (prop == null)
                    return null;
                return prop.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private static int TryConvertToInt(object value, int fallback)
        {
            int parsed;
            return TryConvertToInt(value, out parsed) ? parsed : fallback;
        }

        private static bool TryConvertToInt(object raw, out int value)
        {
            value = 0;
            if (raw == null)
                return false;

            try
            {
                if (raw is int)
                {
                    value = (int)raw;
                    return true;
                }

                if (raw is long)
                {
                    value = (int)(long)raw;
                    return true;
                }

                if (raw is short)
                {
                    value = (short)raw;
                    return true;
                }

                return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private static bool TryParseCardId(string raw, out Card.Cards value)
        {
            try
            {
                value = (Card.Cards)Enum.Parse(typeof(Card.Cards), raw, true);
                return true;
            }
            catch
            {
                value = default(Card.Cards);
                return false;
            }
        }
    }

    // 单文件 Profile 运行时不会自动并入 SupportSources，这里保留最小守卫工具以兼容编译。
    private static class DecisionCardGuards
    {
        public static bool CanUseHeroPowerNow(Board board, int availableMana)
        {
            if (board == null || board.Ability == null || board.Ability.Template == null)
                return false;

            try
            {
                object rawUsed = board.HeroPowerUsedThisTurn;
                if (rawUsed is bool)
                {
                    if ((bool)rawUsed)
                        return false;
                }
                else if (rawUsed != null && Convert.ToInt32(rawUsed, CultureInfo.InvariantCulture) > 0)
                {
                    return false;
                }
            }
            catch
            {
                // ignore
            }

            return board.Ability.CurrentCost <= Math.Max(0, availableMana);
        }

        public static int ResolveBoardIndex(Board board, int teacherBoardSlot, Card sourceCard)
        {
            if (board == null || sourceCard == null || sourceCard.Type != Card.CType.MINION)
                return 0;

            int maxIndex = board.MinionFriend != null ? board.MinionFriend.Count : 0;
            if (teacherBoardSlot <= 0)
                return maxIndex;

            int boardIndex = teacherBoardSlot - 1;
            if (boardIndex < 0)
                return 0;
            if (boardIndex > maxIndex)
                return maxIndex;
            return boardIndex;
        }

        public static bool IsPlayableHandCard(Board board, Card card)
        {
            return IsPlayableHandCard(board, card, board != null ? board.ManaAvailable : 0);
        }

        public static bool IsPlayableHandCard(Board board, Card card, int availableMana)
        {
            if (board == null || card == null || card.Template == null)
                return false;

            if (card.CurrentCost > Math.Max(0, availableMana))
                return false;

            if (card.Type == Card.CType.MINION)
            {
                int friendCount = board.MinionFriend != null ? board.MinionFriend.Count : 0;
                if (friendCount >= FriendlyBoardLimit)
                    return false;
            }

            return true;
        }

        public static bool IsAttackSourceReady(Card card)
        {
            return card != null
                && card.Template != null
                && card.CanAttack
                && card.CurrentAtk > 0;
        }

        public static bool IsLocationCard(Card card)
        {
            return card != null && card.Template != null && card.Type == Card.CType.LOCATION;
        }

        public static bool CardMatchesTemplate(Card card, Card.Cards cardId)
        {
            return card != null
                && card.Template != null
                && cardId != default(Card.Cards)
                && card.Template.Id == cardId;
        }
    }

    private static void Log(string line)
    {
        try
        {
            Bot.Log(LogPrefix + " " + line);
        }
        catch
        {
        }
    }
}
