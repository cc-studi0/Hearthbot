﻿﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBotProfiles
{
    [Serializable]
    public class UniversalPlayProfile : Profile
    {
        private System.Text.StringBuilder _logBuilder = new System.Text.StringBuilder();
        private const string ProfileVersion = "2026-04-01.43";
        private const Card.Cards TheCoin = Card.Cards.GAME_005;
        private const Card.Cards Innervate = Card.Cards.CORE_EX1_169;
        private const int DelayAttackValue = 9999;
        private const int PreferAttackOrderValue = -9999;
        private const int PreferAttackBodyValue = -2600;
        private const int DelayAttackBodyValue = 4200;
        private const int ForceEnemyAttackTargetValue = 9800;
        private const int BlockExactAttackValue = -9999;
        private const int BlockHeroAttackValue = 9999;
        private const int PreferHeroAttackValue = -2600;
        private const int FreshTeacherRefreshHoldSeconds = 8;
        private const int PendingTeacherRefreshHoldSeconds = 20;
        private const int PendingTeacherRefreshHardHoldSeconds = 5;
        private const int PendingTeacherRefreshStrongHoldSeconds = 6;
        // When the board signature already changed and no safe anchored action survived,
        // keeping the stale play snapshot alive for 20s is too long.
        private const int SignatureMismatchTeacherRefreshHoldSeconds = 8;
        private const int SignatureMismatchRelaxSourceValue = -9800;
        private const int TeacherRefreshHoldHandPenalty = 500;
        private const int TeacherRefreshHoldHeroPowerPenalty = 9999;
        private const int TeacherRefreshHoldLocationPenalty = 300;
        private const int TeacherRefreshHoldAttackPenalty = 350;
        private const int TeacherRefreshHoldAttackBodyPenalty = 350;
        // HOLD惩罚说明：值足够大以阻止所有非盒子出牌（包括0费法术）。
        // resimulate后如无更多可执行盒子步骤，AI选择EndTurn是正确行为。
        // 若有后续盒子步骤，TryPromoteCurrentExecutableTeacherActionStep
        // 会跳过hold并继续执行，因此不会漏掉多步操作。
        private const int PendingTeacherRefreshHardHandPenalty = 9999;
        private const int PendingTeacherRefreshHardHeroPowerPenalty = 9999;
        private const int PendingTeacherRefreshHardLocationPenalty = 500;
        private const int PendingTeacherRefreshHardAttackPenalty = 500;
        private const int PendingTeacherRefreshHardAttackBodyPenalty = 500;
        private const int PendingTeacherRefreshStrongHandPenalty = 9800;
        private const int PendingTeacherRefreshStrongHeroPowerPenalty = 9999;
        private const int PendingTeacherRefreshStrongLocationPenalty = 500;
        private const int PendingTeacherRefreshStrongAttackPenalty = 500;
        private const int PendingTeacherRefreshStrongAttackBodyPenalty = 500;
        private const int TeacherRefreshHoldAggroValue = -2200;
        private const int CompletedTeacherActionSyncReloadAttempts = 5;
        private const int CompletedTeacherActionSyncReloadDelayMilliseconds = 160;
        private const bool StrictTeacherExactExecutionOnly = true;
        private const int RecentTeacherActionEndTurnDowngradeHoldSeconds = 6;
        private const int FriendlyBoardLimit = 7;
        private static readonly object AcceptedTeacherSnapshotSync = new object();
        private static readonly object TeacherGuardReasonCounterSync = new object();
        private static readonly Dictionary<string, int> TeacherGuardReasonCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> HeroPowersRequiringFriendlyBoardSlot = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "HERO_02bp",
            "HERO_02bp2",
            "HERO_04bp",
            "HERO_04bp2",
            "EDR_847p"
        };
        private static string _overrideBoardSig = string.Empty;
        private static DateTime _lastAcceptedTeacherSnapshotUtc = DateTime.MinValue;
        private static string _lastAcceptedTeacherBoardSignature = string.Empty;
        private static string _lastAcceptedTeacherActionSummary = string.Empty;
        private static Card.Cards _lastAcceptedTeacherPlaySourceId = default(Card.Cards);
        private static int _lastAcceptedTeacherPlaySourceSlot = 0;
        private static int _lastAcceptedTeacherPlayHandMatchCount = -1;
        private static string _lastLogFingerprint = string.Empty;
        private static string _lastConsumedRecHash = string.Empty;

        public ProfileParameters GetParameters(Board board)
        {
            _logBuilder.Clear();

            ProfileParameters p = new ProfileParameters(BaseProfile.Rush)
            {
                DiscoverSimulationValueThresholdPercent = -10
            };

            AddLog("============== 通用学习出牌 v" + ProfileVersion + " ==============");

            if (board == null)
            {
                AddLog("[BOXOCR][APPLY][MISS] 当前棋盘不可用");
                FlushLog();
                return p;
            }

            ConfigureForcedResimulation(board, p);

            // 标准模式优先保住单文件盒子链路；外部执行器只作为可选增强。
            bool boxOcrBiasApplied = false;
            bool strongGuidanceApplied = false;
            LocalBoxOcrState state = null;
            try
            {
                boxOcrBiasApplied = TryApplyBoxOcrWeakBias(
                    p,
                    board,
                    AddLog,
                    SafeCurrentProfileName(),
                    out strongGuidanceApplied,
                    out state);
            }
            catch
            {
                boxOcrBiasApplied = false;
                strongGuidanceApplied = false;
            }

            bool integratedExecutorEscalated = false;
            if (boxOcrBiasApplied)
            {
                Card choiceCard;
                int choiceIndex;
                if (TryResolveIntegratedExecutorChoiceEscalation(board, state, out choiceCard, out choiceIndex))
                {
                    AddLog("[BOXOCR][CHOICE][ESCALATE] 检测到选项型出牌，追加统一执行器接管 | 卡牌="
                        + BuildCardDisplayName(choiceCard)
                        + " | 选项="
                        + choiceIndex.ToString(CultureInfo.InvariantCulture));
                    integratedExecutorEscalated = TryApplyPlayExecutorCompat(board, p, AddLog);
                    if (!integratedExecutorEscalated)
                        AddLog("[BOXOCR][CHOICE][ESCALATE] 统一执行器未接管，保留本地 ChoicesModifiers 兜底");
                }
            }

            if (!boxOcrBiasApplied && !integratedExecutorEscalated)
            {
                TryApplyPlayExecutorCompat(board, p, AddLog);
            }

            // ── Compact visual summary ──
            try
            {
                TryPromoteCurrentExecutableTeacherActionStep(board, state, null);

                // Decision source
                string decisionTag;
                string decisionIcon;
                string teacherHitStatus;
                string sourceTag;
                if (boxOcrBiasApplied && strongGuidanceApplied) { decisionTag = "盒子强引导"; decisionIcon = "★★★"; }
                else if (boxOcrBiasApplied) { decisionTag = "盒子弱引导"; decisionIcon = "★☆☆"; }
                else { decisionTag = "默认AI"; decisionIcon = "---"; }

                teacherHitStatus = boxOcrBiasApplied ? "命中" : "未命中";
                sourceTag = boxOcrBiasApplied ? "盒子老师" : "默认AI";

                // OCR recognition
                string ocrLine;
                if (state != null && !string.IsNullOrEmpty(state.PreferredActionType))
                {
                    string src = state.PreferredSourceId != default(Card.Cards) && state.PreferredSourceId != Card.Cards.CRED_01
                        ? state.PreferredSourceId.ToString() : "?";
                    string tgt = state.PreferredTargetId != default(Card.Cards) && state.PreferredTargetId != Card.Cards.CRED_01
                        ? " -> " + state.PreferredTargetId : "";
                    string stepSuffix = state.ActionSteps.Count > 1 && state.PreferredStepIndex > 0
                        ? " [" + state.PreferredStepIndex.ToString(CultureInfo.InvariantCulture) + "/" + state.ActionSteps.Count.ToString(CultureInfo.InvariantCulture) + "]"
                        : "";
                    ocrLine = state.PreferredActionType.ToUpper() + " " + src + tgt + stepSuffix;
                }
                else if (state != null && IsCurrentTeacherEndTurnIntent(state))
                    ocrLine = "END TURN (结束回合)";
                else
                    ocrLine = "(无推荐)";

                // OCR speed
                string ocrSpeed = "";
                if (state != null && !string.IsNullOrWhiteSpace(state.PluginVersion))
                {
                    double ageSeconds = GetLocalBoxOcrStateAgeSeconds(state);
                    ocrSpeed = ageSeconds < 2d ? "(<2s)" : "(" + ((int)Math.Round(ageSeconds)).ToString(CultureInfo.InvariantCulture) + "s前)";
                }

                // Board sig match
                string sigStatus = "";
                if (state != null && !string.IsNullOrWhiteSpace(state.BoardSignature))
                {
                    string currentSig = BuildCurrentPlayBoardSignatureKey(board, SafeCurrentProfileName());
                    bool sigMatch = state.MatchesBoardSignature(currentSig);
                    sigStatus = sigMatch ? "签名=匹配" : "签名=不匹配";
                }

                string heroPowerBiasSummary = BuildHeroPowerBiasSummary(board, p);
                string friendlyAttackBiasSummary = BuildFriendlyAttackOrderBiasSummary(board, p);

                AddLog("┌─────────────────────────────────────────────");
                AddLog("│ 盒子: " + teacherHitStatus + " | 来源=" + sourceTag);
                AddLog("│ 决策: " + decisionIcon + " " + decisionTag);
                AddLog("│ 识别: " + ocrLine + " " + ocrSpeed);
                if (!string.IsNullOrWhiteSpace(sigStatus))
                    AddLog("│ " + sigStatus);
                AddLog("│ 对局: " + board.EnemyClass + " | 法力=" + board.ManaAvailable + "/" + board.MaxMana + " | 手牌=" + (board.Hand != null ? board.Hand.Count : 0));
                AddLog("│ " + heroPowerBiasSummary);
                AddLog("│ " + friendlyAttackBiasSummary);

                // Per-card bias: show which cards are boosted/blocked
                if (p != null && board.Hand != null)
                {
                    AddLog("│ 手牌偏置:");
                    foreach (var card in board.Hand)
                    {
                        if (card == null || card.Template == null) continue;
                        string cname = card.Template.NameCN;
                        if (string.IsNullOrWhiteSpace(cname)) cname = card.Template.Name;
                        if (string.IsNullOrWhiteSpace(cname)) cname = card.Template.Id.ToString();

                        int csV = 100, cmV = 100, poV = 0;
                        try
                        {
                            var csById = p.CastSpellsModifiers?.RulesIntIds[(int)card.Id];
                            var csByTpl = p.CastSpellsModifiers?.RulesCardIds[card.Template.Id];
                            if (csById != null) csV = csById.CardModifier.Value;
                            else if (csByTpl != null) csV = csByTpl.CardModifier.Value;

                            var cmById = p.CastMinionsModifiers?.RulesIntIds[(int)card.Id];
                            var cmByTpl = p.CastMinionsModifiers?.RulesCardIds[card.Template.Id];
                            if (cmById != null) cmV = cmById.CardModifier.Value;
                            else if (cmByTpl != null) cmV = cmByTpl.CardModifier.Value;

                            var poById = p.PlayOrderModifiers?.RulesIntIds[(int)card.Id];
                            var poByTpl = p.PlayOrderModifiers?.RulesCardIds[card.Template.Id];
                            if (poById != null) poV = poById.CardModifier.Value;
                            else if (poByTpl != null) poV = poByTpl.CardModifier.Value;
                        }
                        catch { }

                        int castV = Math.Min(csV, cmV);
                        string icon;
                        if (castV <= -9000 && poV >= 9000) icon = ">>> ";
                        else if (castV <= -2000) icon = " >> ";
                        else if (castV >= 9000) icon = " XX ";
                        else if (castV >= 400) icon = "  X ";
                        else icon = "  . ";
                        string costStr = card.CurrentCost.ToString(CultureInfo.InvariantCulture);
                        AddLog("│  " + icon + costStr + "费 " + cname + "  (CS=" + csV + " CM=" + cmV + " PO=" + poV + ")");
                    }
                }

                AddLog("└─────────────────────────────────────────────");
                if (state != null && !string.IsNullOrWhiteSpace(state.RawText))
                    AddLog("[BOXOCR][RAW] " + state.RawText);
                if (state != null
                    && (state.BoxTurnNum > 0
                        || state.BoxChoiceId >= 0
                        || state.BoxOptionId >= 0
                        || state.BoxStatusCode > 0
                        || state.BoxDataCount > 0))
                {
                    AddLog("[BOXOCR][RAW_META] turn="
                        + state.BoxTurnNum.ToString(CultureInfo.InvariantCulture)
                        + " | choiceId="
                        + state.BoxChoiceId.ToString(CultureInfo.InvariantCulture)
                        + " | optionId="
                        + state.BoxOptionId.ToString(CultureInfo.InvariantCulture)
                        + " | status="
                        + state.BoxStatusCode.ToString(CultureInfo.InvariantCulture)
                        + " | dataCount="
                        + state.BoxDataCount.ToString(CultureInfo.InvariantCulture));
                }
                if (state != null && !string.IsNullOrWhiteSpace(state.RawBoxJson))
                    AddLog("[BOXOCR][RAW_JSON] " + state.RawBoxJson);

                // ── Dedup: suppress repeated identical output ──
                try
                {
                    var fp = new System.Text.StringBuilder(128);
                    fp.Append(teacherHitStatus).Append('|');
                    fp.Append(decisionTag).Append('|');
                    fp.Append(board.ManaAvailable).Append('/').Append(board.MaxMana).Append('|');
                    fp.Append(board.Hand != null ? board.Hand.Count : 0).Append('|');
                    if (board.Hand != null)
                        foreach (var c in board.Hand)
                            if (c != null && c.Template != null)
                                fp.Append((int)c.Template.Id).Append(',');
                    fp.Append('|').Append(ocrLine);
                    fp.Append('|').Append(heroPowerBiasSummary);
                    fp.Append('|').Append(friendlyAttackBiasSummary);
                    fp.Append('|').Append(state != null ? state.RecHash ?? string.Empty : string.Empty);
                    string currentFp = fp.ToString();
                    if (currentFp == _lastLogFingerprint)
                    {
                        _logBuilder.Clear(); // same as last output, suppress
                    }
                    else
                    {
                        _lastLogFingerprint = currentFp;
                    }
                }
                catch { /* fingerprint failed, let log through */ }
            }
            catch { }

            FlushLog();
            return p;
        }

        private void AddLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            if (_logBuilder.Length > 0)
                _logBuilder.Append("\r\n");
            _logBuilder.Append(line);
        }

        private void FlushLog()
        {
            try
            {
                if (_logBuilder.Length > 0)
                    Bot.Log(_logBuilder.ToString());
            }
            catch
            {
                // ignore
            }
        }

        private void LogImmediate(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            try
            {
                Bot.Log(line);
            }
            catch
            {
                // ignore
            }
        }

        private static string BuildHeroPowerBiasSummary(Board board, ProfileParameters p)
        {
            if (board == null)
                return "技能限制: 棋盘不可用";

            if (board.Ability == null || board.Ability.Template == null)
                return "技能限制: 无英雄技能";

            if (p == null)
                return "技能限制: 参数不可用";

            int castValue = GetRuleValue(p.CastHeroPowerModifier, board.Ability.Template.Id, board.Ability.Id);
            int orderValue = GetRuleValue(p.PlayOrderModifiers, board.Ability.Template.Id, board.Ability.Id);
            bool canUseNow = CanUseHeroPowerNow(board, board.ManaAvailable);

            string status;
            if (castValue != int.MinValue)
            {
                if (castValue >= 9000)
                    status = "已压制";
                else if (castValue >= 180)
                    status = "轻压制";
                else if (castValue <= -9000)
                    status = "强优先";
                else if (castValue <= -2000)
                    status = "优先";
                else
                    status = "已调整";
            }
            else if (orderValue != int.MinValue)
            {
                status = "次序调整";
            }
            else
            {
                status = "未调整";
            }

            return "技能限制: "
                + status
                + " | 可用="
                + (canUseNow ? "是" : "否")
                + " | "
                + BuildCardDisplayName(board.Ability)
                + " (CHP="
                + FormatRuleValue(castValue)
                + " PO="
                + FormatRuleValue(orderValue)
                + ")";
        }

        private static string BuildFriendlyAttackOrderBiasSummary(Board board, ProfileParameters p)
        {
            if (board == null)
                return "场攻次序: 棋盘不可用";

            if (p == null)
                return "场攻次序: 参数不可用";

            if (board.MinionFriend == null || board.MinionFriend.Count == 0)
                return "场攻次序: 场上无友方随从";

            int attackReadyCount = 0;
            int friendlyCount = 0;
            List<string> affected = new List<string>();

            for (int i = 0; i < board.MinionFriend.Count; i++)
            {
                Card friend = board.MinionFriend[i];
                if (friend == null || friend.Template == null || IsLocationCard(friend))
                    continue;

                friendlyCount++;
                bool canAttack = IsFriendlyAttackSourceReady(friend);
                if (canAttack)
                    attackReadyCount++;

                int attackOrderValue = GetRuleValue(p.AttackOrderModifiers, friend.Template.Id, friend.Id);
                int bodyValue = GetRuleValue(p.OnBoardFriendlyMinionsValuesModifiers, friend.Template.Id, friend.Id);
                if (attackOrderValue == int.MinValue && bodyValue == int.MinValue)
                    continue;

                affected.Add("slot="
                    + (i + 1).ToString(CultureInfo.InvariantCulture)
                    + " "
                    + BuildCardDisplayName(friend)
                    + "["
                    + DescribeAttackBiasState(attackOrderValue, bodyValue, canAttack)
                    + "]"
                    + "(AO="
                    + FormatRuleValue(attackOrderValue)
                    + " BV="
                    + FormatRuleValue(bodyValue)
                    + " 可攻="
                    + (canAttack ? "是" : "否")
                    + ")");
            }

            if (friendlyCount == 0)
                return "场攻次序: 场上无友方随从";

            if (affected.Count == 0)
            {
                return attackReadyCount > 0
                    ? "场攻次序: 未调整 | 可攻随从=" + attackReadyCount.ToString(CultureInfo.InvariantCulture)
                    : "场攻次序: 无可攻随从";
            }

            return "场攻次序: 已调整 "
                + affected.Count.ToString(CultureInfo.InvariantCulture)
                + "个 | 可攻随从="
                + attackReadyCount.ToString(CultureInfo.InvariantCulture)
                + " | "
                + string.Join(", ", affected);
        }

        private static string DescribeAttackBiasState(int attackOrderValue, int bodyValue, bool canAttack)
        {
            int mergedValue = MergeRuleValue(attackOrderValue, bodyValue);
            if (mergedValue == int.MinValue)
                return "未调整";

            if (mergedValue >= 9000)
                return canAttack ? "封锁" : "预封锁";

            if (mergedValue >= 400)
                return canAttack ? "滞后" : "预滞后";

            if (mergedValue <= -9000)
                return "强优先";

            if (mergedValue <= -2000)
                return "优先";

            return "微调";
        }

        private static string FormatRuleValue(int value)
        {
            return value == int.MinValue
                ? "-"
                : value.ToString(CultureInfo.InvariantCulture);
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

        private static string FormatVersionedProfileName(string raw)
        {
            string name = string.IsNullOrWhiteSpace(raw) ? "通用盒子学习出牌.cs" : raw.Trim();
            return name + "(v" + ProfileVersion + ")";
        }

        private static string SafeCurrentDeckName()
        {
            try
            {
                var deck = Bot.CurrentDeck();
                if (deck == null || string.IsNullOrWhiteSpace(deck.Name))
                    return string.Empty;
                return deck.Name.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryApplyPlayExecutorCompat(Board board, ProfileParameters p, Action<string> addLog)
        {
            try
            {
                string loadedAssemblyPath;
                if (!EnsureDecisionSupportAssemblyLoaded(out loadedAssemblyPath))
                {
                    if (addLog != null)
                        addLog("[PLAY][INTEGRATED][SKIP] 未找到外部统一执行器程序集，已跳过接管尝试");
                    return false;
                }

                if (addLog != null && !string.IsNullOrWhiteSpace(loadedAssemblyPath))
                    addLog("[PLAY][INTEGRATED][INFO] 外部统一执行器程序集已就绪=" + loadedAssemblyPath);

                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = assemblies.Length - 1; i >= 0; i--)
                {
                    var assembly = assemblies[i];
                    var executorType = assembly.GetType("SmartBotProfiles.DecisionPlayExecutor", false);
                    if (executorType == null)
                        continue;

                    var method = executorType.GetMethod(
                        "ApplyStandalone",
                        new[] { typeof(Board), typeof(ProfileParameters), typeof(bool), typeof(Action<string>) });
                    if (method == null)
                    {
                        if (addLog != null)
                            addLog("[PLAY][INTEGRATED][SKIP] 已找到 DecisionPlayExecutor，但缺少 ApplyStandalone 兼容入口 | assembly=" + assembly.FullName);
                        continue;
                    }

                    try
                    {
                        object result = method.Invoke(null, new object[] { board, p, true, addLog });
                        if (result is bool)
                        {
                            bool applied = (bool)result;
                            if (!applied && addLog != null)
                                addLog("[PLAY][INTEGRATED][MISS] 外部统一执行器已加载，但当前回合未命中任何可接管策略 | assembly=" + assembly.FullName);
                            return applied;
                        }

                        if (addLog != null)
                            addLog("[PLAY][INTEGRATED][SKIP] 外部统一执行器返回值不是 bool，已忽略 | assembly=" + assembly.FullName);
                    }
                    catch (Exception ex)
                    {
                        if (addLog != null)
                            addLog("[PLAY][INTEGRATED][ERROR] 外部统一执行器调用失败，继续尝试其他已加载版本 | assembly=" + assembly.FullName + " | error=" + ex.GetType().Name + ": " + ex.Message);
                    }
                }

                if (addLog != null)
                    addLog("[PLAY][INTEGRATED][SKIP] 已加载程序集里未找到可用的外部统一执行器类型");
            }
            catch (Exception ex)
            {
                if (addLog != null)
                    addLog("[PLAY][INTEGRATED][ERROR] 外部统一执行器兼容层初始化失败 | error=" + ex.GetType().Name + ": " + ex.Message);
            }

            return false;
        }

        private static bool EnsureDecisionSupportAssemblyLoaded(out string loadedAssemblyPath)
        {
            loadedAssemblyPath = string.Empty;
            foreach (string candidate in EnumerateDecisionSupportAssemblyCandidates())
            {
                try
                {
                    if (!File.Exists(candidate))
                        continue;

                    Assembly.LoadFrom(candidate);
                    loadedAssemblyPath = candidate;
                    return true;
                }
                catch
                {
                    // ignore
                }
            }

            return false;
        }

        private void ConfigureForcedResimulation(Board board, ProfileParameters p)
        {
            if (board == null || board.Hand == null || board.Hand.Count == 0 || p == null)
                return;

            p.ForceResimulation = true;

            if (p.ForcedResimulationCardList == null)
                p.ForcedResimulationCardList = new List<Card.Cards>();

            HashSet<Card.Cards> unique = new HashSet<Card.Cards>();
            foreach (Card card in board.Hand)
            {
                if (card == null || card.Template == null)
                    continue;

                Card.Cards id = card.Template.Id;

                if (!unique.Add(id))
                    continue;

                if (!p.ForcedResimulationCardList.Contains(id))
                    p.ForcedResimulationCardList.Add(id);
            }

            // (resim configured silently)
        }

        // 单文件 Profile 运行时不会自动并入 SupportSources，这里内置最小 play 状态模型以保证动态编译可用。
        private sealed class LocalBoxOcrActionStep
        {
            public int StepIndex = 0;
            public string ActionType = string.Empty;
            public string SourceKind = string.Empty;
            public Card.Cards SourceId = default(Card.Cards);
            public int SourceSlot = 0;
            public int ChoiceIndex = 0;
            public string Delivery = string.Empty;
            public int BoardSlot = 0;
            public bool ActionRequired = false;
            public string TargetKind = string.Empty;
            public Card.Cards TargetId = default(Card.Cards);
            public int TargetSlot = 0;
        }

        private sealed class LocalBoxOcrState
        {
            public DateTime TimestampUtc = DateTime.MinValue;
            public DateTime FileLastWriteUtc = DateTime.MinValue;
            public string Status = string.Empty;
            public string Stage = string.Empty;
            public string StatusReason = string.Empty;
            public bool RefreshPending = false;
            public string ObservedStage = string.Empty;
            public string RefreshTrigger = string.Empty;
            public string PluginVersion = string.Empty;
            public string SBProfile = string.Empty;
            public string BoardSignature = string.Empty;
            public string PreferredActionType = string.Empty;
            public string PreferredSourceKind = string.Empty;
            public Card.Cards PreferredSourceId = default(Card.Cards);
            public int PreferredSourceSlot = 0;
            public int PreferredStepIndex = 0;
            public int PreferredChoiceIndex = 0;
            public string PreferredDelivery = string.Empty;
            public int PreferredBoardSlot = 0;
            public string PreferredTargetKind = string.Empty;
            public Card.Cards PreferredTargetId = default(Card.Cards);
            public int PreferredTargetSlot = 0;
            public bool PreferredActionRequired = false;
            public bool EndTurnRecommended = false;
            public readonly List<LocalBoxOcrActionStep> ActionSteps = new List<LocalBoxOcrActionStep>();
            public readonly HashSet<Card.Cards> RecommendedCards = new HashSet<Card.Cards>();
            public readonly HashSet<Card.Cards> RecommendedHeroPowers = new HashSet<Card.Cards>();
            public readonly List<string> TextHints = new List<string>();
            public string RawText = string.Empty;
            public string RawBoxJson = string.Empty;
            public int BoxTurnNum = 0;
            public int BoxChoiceId = -1;
            public int BoxOptionId = -1;
            public int BoxStatusCode = 0;
            public int BoxDataCount = 0;
            public string RecHash = string.Empty;
            public string SourcePath = string.Empty;

            public bool IsFresh(int maxAgeSeconds)
            {
                if (TimestampUtc == DateTime.MinValue)
                    return false;

                return TimestampUtc >= DateTime.UtcNow.AddSeconds(-Math.Max(1, maxAgeSeconds));
            }

            public bool MatchesProfile(string expectedProfileName)
            {
                string expected = NormalizeStrategyName(expectedProfileName);
                if (string.IsNullOrWhiteSpace(expected))
                    return true;

                string actual = NormalizeStrategyName(SBProfile);
                if (string.IsNullOrWhiteSpace(actual))
                    return false;

                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            }

            public bool MatchesBoardSignature(string expectedSignature)
            {
                if (string.IsNullOrWhiteSpace(BoardSignature) || string.IsNullOrWhiteSpace(expectedSignature))
                    return false;

                string trimmedExpected = expectedSignature.Trim();
                if (string.Equals(BoardSignature.Trim(), trimmedExpected, StringComparison.Ordinal))
                    return true;

                string ovr = _overrideBoardSig;
                if (!string.IsNullOrWhiteSpace(ovr) && string.Equals(ovr.Trim(), trimmedExpected, StringComparison.Ordinal))
                    return true;

                return false;
            }

            private static string NormalizeStrategyName(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return string.Empty;

                string value = raw.Trim().Replace('/', '\\');
                return Path.GetFileName(value).Trim();
            }
        }

        private static bool ShouldBypassTeacherDueToNoticeBanner(LocalBoxOcrState state, out string reason)
        {
            reason = string.Empty;
            if (state == null)
                return false;

            StringBuilder raw = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(state.RawText))
                raw.AppendLine(state.RawText);
            if (!string.IsNullOrWhiteSpace(state.StatusReason))
                raw.AppendLine(state.StatusReason);
            if (!string.IsNullOrWhiteSpace(state.RawBoxJson))
                raw.AppendLine(state.RawBoxJson);
            if (state.TextHints != null && state.TextHints.Count > 0)
                raw.AppendLine(string.Join(" ", state.TextHints.ToArray()));

            string text = raw.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] directMarkers =
            {
                "AI推荐功能实装倒计时",
                "AI推算功能实装倒计时",
                "AI功能实装倒计时"
            };

            for (int i = 0; i < directMarkers.Length; i++)
            {
                if (text.IndexOf(directMarkers[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    reason = directMarkers[i];
                    return true;
                }
            }

            bool hasCountdown = text.IndexOf("倒计时", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasPromo = text.IndexOf("传送", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("杀号", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("beta", StringComparison.OrdinalIgnoreCase) >= 0;
            if (hasCountdown && hasPromo)
            {
                reason = "notice_banner";
                return true;
            }

            return false;
        }

        private static bool IsCurrentTeacherEndTurnIntent(LocalBoxOcrState state)
        {
            if (state == null)
                return false;

            if (!string.IsNullOrWhiteSpace(state.PreferredActionType))
                return ContainsEndTurnHint(state.PreferredActionType);

            return state.EndTurnRecommended;
        }

        private static bool WasAcceptedTeacherActionNonEndTurnRecently()
        {
            lock (AcceptedTeacherSnapshotSync)
            {
                if (_lastAcceptedTeacherSnapshotUtc == DateTime.MinValue)
                    return false;

                if (_lastAcceptedTeacherSnapshotUtc < DateTime.UtcNow.AddSeconds(-RecentTeacherActionEndTurnDowngradeHoldSeconds))
                    return false;

                return !string.IsNullOrWhiteSpace(_lastAcceptedTeacherActionSummary)
                    && _lastAcceptedTeacherActionSummary.IndexOf("结束回合", StringComparison.OrdinalIgnoreCase) < 0;
            }
        }

        private sealed class LocalChoiceRuntimeOption
        {
            public int EntityId = 0;
            public int ChoiceIndex = 0;
            public Card.Cards CardId = default(Card.Cards);
        }

        private static bool HasFreshUnconsumedTeacherRecHash(LocalBoxOcrState state)
        {
            return state != null
                && state.IsFresh(FreshTeacherRefreshHoldSeconds)
                && !string.IsNullOrWhiteSpace(state.RecHash)
                && !string.Equals(state.RecHash, _lastConsumedRecHash, StringComparison.Ordinal);
        }

        private static int BumpTeacherGuardReasonCounter(string category, string reason)
        {
            string bucket = (string.IsNullOrWhiteSpace(category) ? "guard" : category.Trim())
                + "|"
                + (string.IsNullOrWhiteSpace(reason) ? "(none)" : reason.Trim());
            lock (TeacherGuardReasonCounterSync)
            {
                int currentCount;
                TeacherGuardReasonCounters.TryGetValue(bucket, out currentCount);
                currentCount++;
                TeacherGuardReasonCounters[bucket] = currentCount;
                return currentCount;
            }
        }

        private static string BuildTeacherEndTurnReadinessSummary(Board board, out bool naturallyAcceptable)
        {
            int manaNow;
            bool hasPlayableHandNow;
            bool canHeroPowerNow;
            bool hasAttackReadyNow;
            naturallyAcceptable = CanAcceptBoxOcrEndTurn(
                board,
                out manaNow,
                out hasPlayableHandNow,
                out canHeroPowerNow,
                out hasAttackReadyNow);

            return "原始局面自然可结束=" + (naturallyAcceptable ? "是" : "否")
                + " | mana=" + manaNow.ToString(CultureInfo.InvariantCulture)
                + " | 手牌可动=" + (hasPlayableHandNow ? "是" : "否")
                + " | 技能可动=" + (canHeroPowerNow ? "是" : "否")
                + " | 攻击可动=" + (hasAttackReadyNow ? "是" : "否");
        }

        private static bool TryApplyBoxOcrWeakBias(ProfileParameters p, Board board, Action<string> addLog, string expectedProfileName, out bool strongGuidanceApplied, out LocalBoxOcrState loadedState)
        {
            strongGuidanceApplied = false;
            loadedState = null;
            if (p == null || board == null)
                return false;

            BoxAuditGuardState boxAuditGuard = LoadBoxAuditGuardState();
            LocalBoxOcrState state = LoadLocalBoxOcrState();
            loadedState = state;
            if (state == null)
            {
                if (addLog != null)
                    addLog("[BOXOCR][STATE][MISS] 未找到状态文件");
                return false;
            }

            string bypassReason;
            if (ShouldBypassTeacherDueToNoticeBanner(state, out bypassReason))
            {
                if (addLog != null)
                    addLog("[BOXOCR][BYPASS] 检测到盒子公告/占位文案(" + bypassReason + ")，改走本地智慧策略");
                return false;
            }

            if (TryApplyPendingTeacherRefreshHold(p, board, state, addLog))
            {
                strongGuidanceApplied = true;
                return true;
            }

            // 推荐已消费检查 —— 盒子一定是对的，已消费则挂起等待新推荐
            if (state != null
                && !string.IsNullOrEmpty(state.RecHash)
                && string.Equals(state.RecHash, _lastConsumedRecHash, StringComparison.Ordinal))
            {
                ApplyHoldForTeacherRefresh(board, p, true, state, false, false, true);
                strongGuidanceApplied = true;
                if (addLog != null)
                    addLog("[BOXOCR][HASH] 推荐已消费 rec_hash=" + state.RecHash + " → 挂起等待新推荐");
                return true;
            }

            if (!string.Equals(state.Stage, "play", StringComparison.OrdinalIgnoreCase))
            {
                if (addLog != null)
                    addLog("[BOXOCR][STATE][MISS] 阶段=" + DescribeBoxOcrStageForLog(state.Stage));
                return false;
            }

            if (string.Equals(state.Status, "refreshing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.Status, "transition", StringComparison.OrdinalIgnoreCase)
                || state.RefreshPending)
            {
                if (state.MatchesProfile(expectedProfileName)
                    && state.IsFresh(FreshTeacherRefreshHoldSeconds))
                {
                    bool useHardRefreshHold = GetLocalBoxOcrStateAgeSeconds(state) <= PendingTeacherRefreshHardHoldSeconds;
                    bool useStrongRefreshHold = !useHardRefreshHold
                        && GetLocalBoxOcrStateAgeSeconds(state) <= PendingTeacherRefreshStrongHoldSeconds;
                    ApplyHoldForTeacherRefresh(board, p, true, state, false, useStrongRefreshHold, useHardRefreshHold);
                    strongGuidanceApplied = true;
                    if (addLog != null)
                        addLog("[等待盒子刷新] " + DescribeBoxOcrStatusForLog(state.Status) + " " + ((int)GetLocalBoxOcrStateAgeSeconds(state)) + "s");
                    return true;
                }

                if (addLog != null)
                    addLog("[BOXOCR][STATE][MISS] 状态=" + DescribeBoxOcrStatusForLog(state.Status));
                return false;
            }

            if (!string.Equals(state.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                if (addLog != null)
                    addLog("[BOXOCR][STATE][MISS] 状态=" + DescribeBoxOcrStatusForLog(state.Status));
                return false;
            }

            if (!state.MatchesProfile(expectedProfileName))
            {
                if (addLog != null)
                    addLog(
                        "[BOXOCR][STATE][MISS] 策略不匹配 | 期望="
                        + NormalizeStrategyNameForLog(expectedProfileName)
                        + " | 实际="
                        + NormalizeStrategyNameForLog(state.SBProfile));
                return false;
            }

            string currentBoardSignature = BuildCurrentPlayBoardSignatureKey(board, expectedProfileName);
            // Clear stale board-sig override when a fresh OCR state with a real board sig arrives.
            if (!string.IsNullOrWhiteSpace(state.BoardSignature))
                _overrideBoardSig = string.Empty;
            LocalBoxOcrState refreshedCompletedState;
            if (TryReloadTeacherStateAfterCompletedAction(board, state, currentBoardSignature, expectedProfileName, addLog, out refreshedCompletedState))
            {
                state = refreshedCompletedState;
                loadedState = state;
                if (!string.IsNullOrWhiteSpace(state.BoardSignature))
                    _overrideBoardSig = string.Empty;

                if (string.Equals(state.Status, "refreshing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state.Status, "transition", StringComparison.OrdinalIgnoreCase)
                    || state.RefreshPending)
                {
                    bool useHardRefreshHold = GetLocalBoxOcrStateAgeSeconds(state) <= PendingTeacherRefreshHardHoldSeconds;
                    bool useStrongRefreshHold = !useHardRefreshHold
                        && GetLocalBoxOcrStateAgeSeconds(state) <= PendingTeacherRefreshStrongHoldSeconds;
                    ApplyHoldForTeacherRefresh(board, p, true, state, false, useStrongRefreshHold, useHardRefreshHold);
                    strongGuidanceApplied = true;
                    return true;
                }
            }
            bool promotedExecutableStep = TryPromoteCurrentExecutableTeacherActionStep(board, state, addLog);
            bool completedActionBailout = false;
            if (!promotedExecutableStep
                && TryApplyAcceptedTeacherSnapshotWaitHold(p, board, state, currentBoardSignature, addLog, out completedActionBailout))
            {
                strongGuidanceApplied = true;
                return true;
            }

            // 上一步盒子动作已执行完毕（英雄技能已用完、卡牌已打出），daemon尚未刷新。
            // 直接放行默认AI，避免后续签名不匹配/hold逻辑阻塞导致过早EndTurn。
            if (completedActionBailout)
            {
                // 动作链全部执行完毕 → 标记 hash 已消费
                if (state != null && !string.IsNullOrEmpty(state.RecHash))
                    _lastConsumedRecHash = state.RecHash;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(state.BoardSignature))
            {
                if (!state.MatchesBoardSignature(currentBoardSignature))
                {
                    if (CanReuseFreshTurnTransitionPlayState(board, state))
                    {
                        TryRefreshLocalBoxOcrBoardSignature(currentBoardSignature);
                        if (addLog != null)
                            addLog("[BOXOCR][STATE][RELAX] 棋盘签名变化，但命中新鲜OCR锚点");
                    }
                    else
                    {
                        string signatureRelaxSummary;
                        if (!StrictTeacherExactExecutionOnly
                            && TryApplySignatureMismatchSafeBias(p, board, state, boxAuditGuard, out signatureRelaxSummary))
                        {
                            TryRefreshLocalBoxOcrBoardSignature(currentBoardSignature);
                            RememberAcceptedTeacherSnapshot(board, state, currentBoardSignature);
                            strongGuidanceApplied = true;
                            if (addLog != null)
                                addLog("[BOXOCR][STATE][RELAX] 棋盘签名不匹配，但已安全继续低风险动作 -> " + signatureRelaxSummary);
                            return true;
                        }

                        string mismatchStrictSummary;
                        if (StrictTeacherExactExecutionOnly
                            && TryApplyStrictExecutableTeacherStepOnSignatureMismatch(
                                p,
                                board,
                                state,
                                currentBoardSignature,
                                addLog,
                                out mismatchStrictSummary))
                        {
                            strongGuidanceApplied = true;
                            return true;
                        }

                        if (StrictTeacherExactExecutionOnly && addLog != null)
                            addLog("[BOXOCR][STRICT] 棋盘签名不匹配，已禁用安全降级，等待盒子刷新精确步骤");

                        if (TryApplyFreshTeacherRefreshHoldOnSignatureMismatch(p, board, state, addLog, true, "signature_mismatch"))
                        {
                            strongGuidanceApplied = true;
                            return true;
                        }

                        if (addLog != null)
                            addLog("[BOXOCR][STATE][MISS] 棋盘签名不匹配");
                        return false;
                    }
                }

                int sigFreshThreshold = promotedExecutableStep ? 45 : 20;
                // 有未消费的 rec_hash 时信任盒子，跳过时间新鲜度检查
                bool hashBypassFreshness = !string.IsNullOrEmpty(state.RecHash)
                    && !string.Equals(state.RecHash, _lastConsumedRecHash, StringComparison.Ordinal);
                if (!hashBypassFreshness && !state.IsFresh(sigFreshThreshold))
                {
                    if (addLog != null)
                        addLog("[BOXOCR][STATE][MISS] 状态已过期 | 已过秒数=" + GetLocalBoxOcrStateAgeSeconds(state) + " | 阈值=" + sigFreshThreshold + BuildLocalBoxOcrSevereStaleHint(state));
                    return false;
                }
            }
            else
            {
                int noSigFreshThreshold = promotedExecutableStep ? 45 : 25;
                // 有未消费的 rec_hash 时信任盒子，跳过时间新鲜度检查
                bool hashBypassFreshnessNoSig = !string.IsNullOrEmpty(state.RecHash)
                    && !string.Equals(state.RecHash, _lastConsumedRecHash, StringComparison.Ordinal);
                if (!hashBypassFreshnessNoSig && !state.IsFresh(noSigFreshThreshold))
                {
                    if (addLog != null)
                        addLog("[BOXOCR][STATE][MISS] 状态已过期 | 已过秒数=" + GetLocalBoxOcrStateAgeSeconds(state) + " | 阈值=" + noSigFreshThreshold + BuildLocalBoxOcrSevereStaleHint(state));
                    return false;
                }
            }

            bool teacherRecommendationPresent = HasTeacherActionRecommendation(board, state);
            if (!teacherRecommendationPresent)
            {
                // 动作链无可执行步骤 → 标记 hash 已消费
                if (state != null && !string.IsNullOrEmpty(state.RecHash))
                    _lastConsumedRecHash = state.RecHash;

                // attack stale — details shown in summary

                if (TryApplyFreshTeacherRefreshHoldOnMiss(
                    p,
                    board,
                    state,
                    addLog,
                    "盒子暂时没有给出新动作，但上一拍OCR仍新鲜，阻止默认逻辑等待刷新",
                    false,
                    "no_new_action"))
                {
                    strongGuidanceApplied = true;
                    return true;
                }

                if (addLog != null)
                    addLog("[BOXOCR][STATE][MISS] 没有推荐动作");
                return false;
            }

            if (boxAuditGuard != null && boxAuditGuard.IsFresh() && boxAuditGuard.SuppressesStage("play"))
            {
                if (addLog != null)
                    addLog("[BOXAUDIT][GUARD][RELAX] 盒子已有推荐动作，忽略出牌拦截 | 分数=" + boxAuditGuard.ConsistencyScore + " | 摘要=" + boxAuditGuard.Summary);
            }

            bool attackIntentPresent = HasTeacherAttackIntent(state);
            bool forcedApplied = false;
            string forcedSummary;
            if (TryApplyBoxOcrPreferredStepBias(p, board, state, out forcedSummary))
            {
                forcedApplied = true;
                strongGuidanceApplied = true;
                if (addLog != null)
                    addLog(">> OCR FORCED: " + forcedSummary);
            }

            string strictSummary;
            if (CanStrictlyEnforceTeacherPlan(board, state, forcedApplied)
                && TryApplyTeacherOnlyPlanBias(p, board, state, forcedApplied, out strictSummary))
            {
                RememberAcceptedTeacherSnapshot(board, state, currentBoardSignature);
                strongGuidanceApplied = true;
                if (addLog != null)
                    addLog(">> OCR STRICT: " + strictSummary);
                return true;
            }

            if (forcedApplied)
            {
                RememberAcceptedTeacherSnapshot(board, state, currentBoardSignature);
                if (addLog != null)
                    addLog(">> OCR LOCKED: preferred action locked");
                return true;
            }

            // 结束回合仍以盒子为准，但若当前局面明显还有动作可做，先短暂等待刷新确认，
            // 防止陈旧 EndTurn 抢先覆盖刚执行完的老师动作链。
            if (IsCurrentTeacherEndTurnIntent(state))
            {
                bool naturallyAcceptableEndTurn;
                string endTurnReadinessSummary = BuildTeacherEndTurnReadinessSummary(board, out naturallyAcceptableEndTurn);
                if (!naturallyAcceptableEndTurn && WasAcceptedTeacherActionNonEndTurnRecently())
                {
                    if (TryApplyFreshTeacherRefreshHoldOnMiss(
                        p,
                        board,
                        state,
                        addLog,
                        "上一拍已接受非结束回合老师动作，暂不接受后续结束回合覆盖"
                            + " | "
                            + endTurnReadinessSummary,
                        true,
                        "end_turn_downgrade_guard",
                        useStrongHold: true))
                    {
                        strongGuidanceApplied = true;
                        return true;
                    }
                }

                if (!naturallyAcceptableEndTurn && state.IsFresh(FreshTeacherRefreshHoldSeconds))
                {
                    bool hasFreshUnconsumedRecHash = HasFreshUnconsumedTeacherRecHash(state);
                    string holdReasonCode = hasFreshUnconsumedRecHash
                        ? "end_turn_wait_refresh_hash"
                        : "end_turn_wait_refresh";
                    string holdReason = hasFreshUnconsumedRecHash
                        ? "老师建议结束回合，但当前仍有可行动作且 rec_hash 未消费，等待刷新确认"
                        : "老师建议结束回合，但当前仍有可行动作，等待刷新确认";
                    if (TryApplyFreshTeacherRefreshHoldOnMiss(
                        p,
                        board,
                        state,
                        addLog,
                        holdReason + " | " + endTurnReadinessSummary,
                        true,
                        holdReasonCode,
                        useStrongHold: true))
                    {
                        strongGuidanceApplied = true;
                        return true;
                    }
                }

                string endTurnSummary;
                if (TryApplyBoxOcrEndTurnBias(p, board, out endTurnSummary))
                {
                    RememberAcceptedTeacherSnapshot(board, state, currentBoardSignature);
                    strongGuidanceApplied = true;
                    if (addLog != null)
                        addLog(">> OCR END_TURN: " + endTurnSummary);
                    return true;
                }

                if (addLog != null)
                    addLog("[BOXOCR][STATE][MISS] 结束回合引导应用失败");
                return false;
            }

            Card anchoredCard;
            string relaxSummary;
            if (!StrictTeacherExactExecutionOnly
                && TryApplyTextAnchoredRecommendedCardRelax(p, board, state, boxAuditGuard, out anchoredCard, out relaxSummary))
            {
                RememberAcceptedTeacherSnapshot(board, state, currentBoardSignature);
                strongGuidanceApplied = true;
                if (addLog != null)
                    addLog("[BOXOCR][APPLY][RELAX] 精确映射不足，改用文本锚点强偏置 -> " + relaxSummary);
                return true;
            }

            if (StrictTeacherExactExecutionOnly && addLog != null)
                addLog("[BOXOCR][STRICT] 当前未命中精确映射，已禁用文本锚点降级");

            // ── 已打出检测：推荐卡牌已在坟场或奥秘区，说明推荐陈旧 ──
            // 不立即回退默认AI，让后续 mapping_not_ready hold 有机会等待 daemon 刷新
            bool preferredCardAlreadyPlayed = IsPreferredCardAlreadyPlayed(board, state);
            if (preferredCardAlreadyPlayed && addLog != null)
                addLog("[BOXOCR][SKIP] 推荐卡牌已打出(坟场/奥秘区)，等待daemon刷新: " + state.PreferredSourceId);

            // state detail omitted — shown in compact summary

            LocalBoxOcrState mappingReloadedState;
            if (TryReloadTeacherStateOnMappingNotReady(board, state, expectedProfileName, addLog, out mappingReloadedState))
            {
                state = mappingReloadedState;
                loadedState = state;
                if (!string.IsNullOrWhiteSpace(state.BoardSignature))
                    _overrideBoardSig = string.Empty;

                if (!string.IsNullOrWhiteSpace(state.BoardSignature)
                    && !state.MatchesBoardSignature(currentBoardSignature))
                {
                    if (addLog != null)
                        addLog("[BOXOCR][STATE][SYNC_WAIT] 补等后拿到新老师状态，但棋盘签名仍未对齐，继续等待精确刷新");
                }
                else if (string.Equals(state.Status, "refreshing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state.Status, "transition", StringComparison.OrdinalIgnoreCase)
                    || state.RefreshPending)
                {
                    bool useHardRefreshHold = GetLocalBoxOcrStateAgeSeconds(state) <= PendingTeacherRefreshHardHoldSeconds;
                    bool useStrongRefreshHold = !useHardRefreshHold
                        && GetLocalBoxOcrStateAgeSeconds(state) <= PendingTeacherRefreshStrongHoldSeconds;
                    ApplyHoldForTeacherRefresh(board, p, true, state, false, useStrongRefreshHold, useHardRefreshHold);
                    strongGuidanceApplied = true;
                    return true;
                }
                else
                {
                    TryPromoteCurrentExecutableTeacherActionStep(board, state, addLog);

                    bool retryForcedApplied = false;
                    string retryForcedSummary;
                    if (TryApplyBoxOcrPreferredStepBias(p, board, state, out retryForcedSummary))
                    {
                        retryForcedApplied = true;
                        strongGuidanceApplied = true;
                        if (addLog != null)
                            addLog(">> OCR REMAP FORCED: " + retryForcedSummary);
                    }

                    string retryStrictSummary;
                    if (CanStrictlyEnforceTeacherPlan(board, state, retryForcedApplied)
                        && TryApplyTeacherOnlyPlanBias(p, board, state, retryForcedApplied, out retryStrictSummary))
                    {
                        RememberAcceptedTeacherSnapshot(board, state, currentBoardSignature);
                        strongGuidanceApplied = true;
                        if (addLog != null)
                            addLog(">> OCR REMAP STRICT: " + retryStrictSummary);
                        return true;
                    }

                    if (retryForcedApplied)
                    {
                        RememberAcceptedTeacherSnapshot(board, state, currentBoardSignature);
                        if (addLog != null)
                            addLog(">> OCR REMAP LOCKED: preferred action locked");
                        return true;
                    }

                    if (IsCurrentTeacherEndTurnIntent(state))
                    {
                        string retryEndTurnSummary;
                        if (TryApplyBoxOcrEndTurnBias(p, board, out retryEndTurnSummary))
                        {
                            RememberAcceptedTeacherSnapshot(board, state, currentBoardSignature);
                            strongGuidanceApplied = true;
                            if (addLog != null)
                                addLog(">> OCR REMAP END_TURN: " + retryEndTurnSummary);
                            return true;
                        }
                    }
                }
            }

            // 盒子已经给了动作时，优先挡住默认逻辑抢跑，等下一拍刷新后再精确落地。
            // 使用加强力度阻止AI自行出牌/攻击，避免偏离盒子指示。
            if (TryApplyFreshTeacherRefreshHoldOnMiss(
                p,
                board,
                state,
                addLog,
                "盒子有动作但当前映射未落地，阻止默认逻辑等待刷新",
                true,
                "mapping_not_ready",
                useStrongHold: true))
            {
                strongGuidanceApplied = true;
                return true;
            }

            if (addLog != null)
                addLog("[BOXOCR][STATE][MISS] 盒子有动作，但当前无法精确映射，回退默认逻辑");

            DumpBoxOcrMissToFile(board, state, "盒子有动作，但当前无法精确映射，回退默认逻辑");

            return false;
        }

        private static void DumpBoxOcrMissToFile(Board board, LocalBoxOcrState state, string reason)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dumpPath = Path.Combine(baseDir, "Logs", "boxocr_miss_dump.log");

                var sb = new StringBuilder();
                sb.AppendLine("================================================================");
                sb.AppendLine("[MISS] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | 原因: " + reason);
                sb.AppendLine("----------------------------------------------------------------");

                // 盒子状态
                if (state != null)
                {
                    sb.AppendLine("ts_utc          = " + state.TimestampUtc.ToString("o"));
                    sb.AppendLine("status          = " + state.Status);
                    sb.AppendLine("stage           = " + state.Stage);
                    sb.AppendLine("status_reason   = " + state.StatusReason);
                    sb.AppendLine("source_path     = " + state.SourcePath);
                    sb.AppendLine("board_sig       = " + state.BoardSignature);
                    sb.AppendLine("action_type     = " + state.PreferredActionType);
                    sb.AppendLine("source_kind     = " + state.PreferredSourceKind);
                    sb.AppendLine("source_id       = " + state.PreferredSourceId);
                    sb.AppendLine("source_slot     = " + state.PreferredSourceSlot);
                    sb.AppendLine("target_kind     = " + state.PreferredTargetKind);
                    sb.AppendLine("target_id       = " + state.PreferredTargetId);
                    sb.AppendLine("target_slot     = " + state.PreferredTargetSlot);
                    sb.AppendLine("delivery        = " + state.PreferredDelivery);
                    sb.AppendLine("board_slot      = " + state.PreferredBoardSlot);
                    sb.AppendLine("choice_index    = " + state.PreferredChoiceIndex);
                    sb.AppendLine("step_index      = " + state.PreferredStepIndex);
                    sb.AppendLine("end_turn        = " + state.EndTurnRecommended);
                    sb.AppendLine("action_required = " + state.PreferredActionRequired);
                    sb.AppendLine("sb_profile      = " + state.SBProfile);
                    sb.AppendLine("plugin_version  = " + state.PluginVersion);
                    sb.AppendLine("box_turn_num    = " + state.BoxTurnNum.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("box_choice_id   = " + state.BoxChoiceId.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("box_option_id   = " + state.BoxOptionId.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("box_status_code = " + state.BoxStatusCode.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("box_data_count  = " + state.BoxDataCount.ToString(CultureInfo.InvariantCulture));

                    if (state.TextHints != null && state.TextHints.Count > 0)
                    {
                        sb.AppendLine("text_hints:");
                        foreach (string hint in state.TextHints)
                            sb.AppendLine("  - " + hint);
                    }

                    if (state.ActionSteps != null && state.ActionSteps.Count > 0)
                    {
                        sb.AppendLine("action_steps:");
                        foreach (var step in state.ActionSteps)
                        {
                            sb.AppendLine("  [step " + step.StepIndex + "] "
                                + step.ActionType
                                + " src=" + step.SourceKind + "/" + step.SourceId + "#" + step.SourceSlot
                                + " tgt=" + step.TargetKind + "/" + step.TargetId + "#" + step.TargetSlot
                                + " delivery=" + step.Delivery
                                + " board_slot=" + step.BoardSlot
                                + " choice=" + step.ChoiceIndex
                                + " required=" + step.ActionRequired);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(state.RawBoxJson))
                    {
                        sb.AppendLine("raw_box_json:");
                        sb.AppendLine(state.RawBoxJson);
                    }

                    if (state.RecommendedCards != null && state.RecommendedCards.Count > 0)
                    {
                        sb.Append("recommended_cards = ");
                        bool first = true;
                        foreach (var cid in state.RecommendedCards)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append(cid);
                            first = false;
                        }
                        sb.AppendLine();
                    }
                }

                // 棋盘上下文
                if (board != null)
                {
                    sb.AppendLine("----------------------------------------------------------------");
                    sb.AppendLine("对手: " + board.EnemyClass + " | 法力: " + board.ManaAvailable + "/" + board.MaxMana);

                    if (board.Hand != null && board.Hand.Count > 0)
                    {
                        sb.Append("手牌: ");
                        bool first = true;
                        foreach (var card in board.Hand)
                        {
                            if (card == null || card.Template == null) continue;
                            if (!first) sb.Append(", ");
                            string cname = card.Template.NameCN;
                            if (string.IsNullOrWhiteSpace(cname)) cname = card.Template.Name;
                            if (string.IsNullOrWhiteSpace(cname)) cname = card.Template.Id.ToString();
                            sb.Append(cname + "(" + card.Template.Id + ")");
                            first = false;
                        }
                        sb.AppendLine();
                    }

                    if (board.MinionFriend != null && board.MinionFriend.Count > 0)
                    {
                        sb.Append("友方随从: ");
                        bool first = true;
                        foreach (var m in board.MinionFriend)
                        {
                            if (m == null || m.Template == null) continue;
                            if (!first) sb.Append(", ");
                            string mname = m.Template.NameCN;
                            if (string.IsNullOrWhiteSpace(mname)) mname = m.Template.Name;
                            if (string.IsNullOrWhiteSpace(mname)) mname = m.Template.Id.ToString();
                            sb.Append(mname + "(" + m.Template.Id + " " + m.CurrentAtk + "/" + m.CurrentHealth
                                + (m.CanAttack ? " CAN_ATK" : "") + ")");
                            first = false;
                        }
                        sb.AppendLine();
                    }

                    if (board.MinionEnemy != null && board.MinionEnemy.Count > 0)
                    {
                        sb.Append("敌方随从: ");
                        bool first = true;
                        foreach (var m in board.MinionEnemy)
                        {
                            if (m == null || m.Template == null) continue;
                            if (!first) sb.Append(", ");
                            string mname = m.Template.NameCN;
                            if (string.IsNullOrWhiteSpace(mname)) mname = m.Template.Name;
                            if (string.IsNullOrWhiteSpace(mname)) mname = m.Template.Id.ToString();
                            sb.Append(mname + "(" + m.Template.Id + " " + m.CurrentAtk + "/" + m.CurrentHealth + ")");
                            first = false;
                        }
                        sb.AppendLine();
                    }
                }

                // 原始状态文件
                string rawPath = ResolveBoxOcrStatePath();
                if (!string.IsNullOrWhiteSpace(rawPath) && File.Exists(rawPath))
                {
                    sb.AppendLine("----------------------------------------------------------------");
                    sb.AppendLine("原始状态文件 (" + rawPath + "):");
                    string[] rawLines = SafeReadAllLines(rawPath);
                    foreach (string rl in rawLines)
                        sb.AppendLine("  " + rl);
                }

                sb.AppendLine("================================================================");
                sb.AppendLine();

                string dir = Path.GetDirectoryName(dumpPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(dumpPath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 记录MISS文件失败不影响主逻辑
            }
        }

        private static bool TryApplyBoxOcrEndTurnBias(ProfileParameters p, Board board, out string summary)
        {
            summary = "OCR建议结束回合";
            if (p == null || board == null)
                return false;

            bool appliedAny = false;
            int blockedHandActions = 0;
            int heldBoardAttackers = 0;

            try
            {
                if (board.Hand != null)
                {
                    foreach (Card card in board.Hand)
                    {
                        if (card == null || card.Template == null)
                            continue;

                        ApplyCardBias(p, card, 9999, 9999, -9999, -9999);
                        blockedHandActions++;
                        appliedAny = true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.Ability != null && board.Ability.Template != null)
                {
                    ApplyHeroPowerCastAndOrderBias(p, board, 9999, -9999);
                    appliedAny = true;
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.MinionFriend != null)
                {
                    foreach (Card friend in board.MinionFriend)
                    {
                        if (friend == null || friend.Template == null)
                            continue;

                        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                        p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                        heldBoardAttackers++;
                        appliedAny = true;

                        if (IsLocationCard(friend))
                        {
                            p.LocationsModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                            p.LocationsModifiers.AddOrUpdate(friend.Id, new Modifier(9999));
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.HeroFriend != null)
                {
                    p.WeaponsAttackModifiers.AddOrUpdate(board.HeroFriend.Id, new Modifier(9999));
                    appliedAny = true;
                }

                if (board.WeaponFriend != null && board.WeaponFriend.Template != null)
                {
                    p.WeaponsAttackModifiers.AddOrUpdate(board.WeaponFriend.Template.Id, new Modifier(9999));
                    p.WeaponsAttackModifiers.AddOrUpdate(board.WeaponFriend.Id, new Modifier(9999));
                    appliedAny = true;
                }
            }
            catch
            {
                // ignore
            }

            // Removed heavy aggro/defense modifiers for end-turn.
            // The engine will naturally end turn when no good actions remain.
            appliedAny = true;

            bool naturallyAcceptable;
            string endTurnReadinessSummary = BuildTeacherEndTurnReadinessSummary(board, out naturallyAcceptable);
            summary = "OCR建议结束回合 | 手牌已封锁=" + blockedHandActions
                + " | 场攻已封锁=" + heldBoardAttackers
                + " | "
                + endTurnReadinessSummary;
            return appliedAny;
        }

        private static bool CanAcceptBoxOcrEndTurn(
            Board board,
            out int manaNow,
            out bool hasPlayableHandNow,
            out bool canHeroPowerNow,
            out bool hasAttackReadyNow)
        {
            manaNow = GetAvailableManaIncludingBurstManaCards(board);
            hasPlayableHandNow = HasPlayableHandActionNow(board, manaNow);
            canHeroPowerNow = CanUseHeroPowerNow(board, manaNow);
            hasAttackReadyNow = HasAnyAttackReady(board);
            return !hasPlayableHandNow && !canHeroPowerNow && !hasAttackReadyNow;
        }

        private static int GetAvailableManaIncludingBurstManaCards(Board board)
        {
            if (board == null)
                return 0;

            int mana = Math.Max(0, board.ManaAvailable);
            int burstMana = 0;
            if (board.Hand != null)
            {
                foreach (Card card in board.Hand)
                {
                    if (card == null || card.Template == null)
                        continue;

                    if (card.Template.Id == TheCoin || card.Template.Id == Innervate)
                        burstMana++;
                }
            }

            return mana + burstMana;
        }

        private static bool HasPlayableHandActionNow(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;

            int freeBoardSlots = GetFreeBoardSlots(board);
            foreach (Card card in board.Hand)
            {
                if (card == null || card.Template == null)
                    continue;

                if (card.CurrentCost > manaNow)
                    continue;

                if (card.Type == Card.CType.MINION && freeBoardSlots <= 0)
                    continue;

                return true;
            }

            return false;
        }

        private static bool CanUseHeroPowerNow(Board board, int availableMana)
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

            if (board.Ability.CurrentCost > Math.Max(0, availableMana))
                return false;

            if (HeroPowerRequiresFriendlyBoardSlot(board.Ability.Template.Id)
                && GetFreeBoardSlots(board) <= 0)
            {
                return false;
            }

            return true;
        }

        private static bool HasAnyAttackReady(Board board)
        {
            if (board == null)
                return false;

            if (board.MinionFriend != null)
            {
                foreach (Card friend in board.MinionFriend)
                {
                    if (friend != null && friend.Template != null && friend.CanAttack && friend.CurrentAtk > 0)
                        return true;
                }
            }

            return CanFriendlyHeroAttack(board);
        }

        private static int GetFreeBoardSlots(Board board)
        {
            if (board == null || board.MinionFriend == null)
                return FriendlyBoardLimit;

            return Math.Max(0, FriendlyBoardLimit - board.MinionFriend.Count);
        }

        private static bool HeroPowerRequiresFriendlyBoardSlot(Card.Cards heroPowerId)
        {
            return heroPowerId != default(Card.Cards)
                && HeroPowersRequiringFriendlyBoardSlot.Contains(heroPowerId.ToString());
        }

        private static string NormalizeStrategyNameForLog(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "(empty)";

            try
            {
                string value = raw.Trim().Replace('/', '\\');
                string fileName = Path.GetFileName(value).Trim();
                return string.IsNullOrWhiteSpace(fileName) ? value : fileName;
            }
            catch
            {
                return raw.Trim();
            }
        }

        private static string DescribeBoxOcrStatusForLog(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "(空)";

            switch (raw.Trim().ToLowerInvariant())
            {
                case "ok":
                    return "正常";
                case "empty":
                    return "空";
                case "refreshing":
                    return "刷新中";
                case "transition":
                    return "过渡中";
                case "miss":
                    return "未命中";
                case "error":
                    return "异常";
                default:
                    return raw.Trim();
            }
        }

        private static string DescribeBoxOcrStageForLog(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "(空)";

            switch (raw.Trim().ToLowerInvariant())
            {
                case "play":
                    return "出牌";
                case "mulligan":
                    return "留牌";
                case "discover":
                    return "发现";
                case "end_turn":
                    return "结束回合";
                default:
                    return raw.Trim();
            }
        }

        private static string DescribeBoxOcrActionTypeForLog(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "(无)";

            switch (raw.Trim().ToLowerInvariant())
            {
                case "playcard":
                    return "出牌";
                case "heropower":
                case "useheropower":
                    return "英雄技能";
                case "attack":
                    return "攻击";
                case "endturn":
                case "end_turn":
                    return "结束回合";
                case "uselocation":
                    return "地标";
                default:
                    return raw.Trim();
            }
        }

        private static bool IsTeacherHeroPowerActionType(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string normalized = raw.Trim();
            return string.Equals(normalized, "HeroPower", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "UseHeroPower", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeBoxOcrSourceKindForLog(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "(无)";

            switch (raw.Trim().ToLowerInvariant())
            {
                case "hand_card":
                    return "手牌";
                case "hero_power":
                    return "英雄技能";
                case "friendly_minion":
                    return "友方随从";
                case "friendly_location":
                    return "友方地标";
                case "friendly_hero":
                case "own_hero":
                    return "我方英雄";
                default:
                    return raw.Trim();
            }
        }

        private static string DescribeBoxOcrDeliveryForLog(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "(无)";

            switch (raw.Trim().ToLowerInvariant())
            {
                case "mouse":
                    return "鼠标直连";
                case "bias":
                    return "偏置引导";
                default:
                    return raw.Trim();
            }
        }

        private static bool ContainsEndTurnHint(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string trimmed = raw.Trim();
            return raw.IndexOf("结束回合", StringComparison.OrdinalIgnoreCase) >= 0
                || raw.IndexOf("結束回合", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(trimmed, "end_turn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "endturn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "EndTurn", StringComparison.Ordinal);
        }

        private static void AddPlayTextHint(ICollection<string> hints, string raw)
        {
            if (hints == null || string.IsNullOrWhiteSpace(raw))
                return;

            string value = raw.Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (!hints.Contains(value))
                hints.Add(value);
        }

        private static string NormalizePlayTextHint(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            StringBuilder sb = new StringBuilder(raw.Length);
            foreach (char ch in raw)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            }

            return sb.ToString();
        }

        private static int ScorePlayAliasMatch(string normalizedHint, string normalizedAlias)
        {
            if (string.IsNullOrWhiteSpace(normalizedHint) || string.IsNullOrWhiteSpace(normalizedAlias))
                return 0;

            if (string.Equals(normalizedHint, normalizedAlias, StringComparison.Ordinal))
                return 1000 + normalizedAlias.Length;

            if (normalizedHint.Length >= 2
                && normalizedAlias.Length >= 2
                && (normalizedHint.Contains(normalizedAlias) || normalizedAlias.Contains(normalizedHint)))
            {
                return 100 + Math.Min(normalizedHint.Length, normalizedAlias.Length);
            }

            return 0;
        }

        private static int GetLocalBoxOcrStateAgeSeconds(LocalBoxOcrState state)
        {
            if (state == null || state.TimestampUtc == DateTime.MinValue)
                return -1;

            try
            {
                return Math.Max(0, (int)Math.Round((DateTime.UtcNow - state.TimestampUtc).TotalSeconds));
            }
            catch
            {
                return -1;
            }
        }

        private static string BuildLocalBoxOcrSevereStaleHint(LocalBoxOcrState state)
        {
            if (state == null)
                return string.Empty;

            int ageSeconds = GetLocalBoxOcrStateAgeSeconds(state);
            if (ageSeconds < 300)
                return string.Empty;

            List<string> parts = new List<string>();
            parts.Add("疑似daemon停更");

            if (state.TimestampUtc != DateTime.MinValue)
                parts.Add("ts_utc=" + state.TimestampUtc.ToString("o"));

            if (state.FileLastWriteUtc != DateTime.MinValue)
                parts.Add("文件落盘UTC=" + state.FileLastWriteUtc.ToString("o"));

            if (!string.IsNullOrWhiteSpace(state.SourcePath))
                parts.Add("文件=" + Path.GetFileName(state.SourcePath));

            return parts.Count > 0 ? " | " + string.Join(" | ", parts) : string.Empty;
        }

        private static bool IsTeacherUnknownCardId(Card.Cards cardId)
        {
            return cardId == default(Card.Cards)
                || string.Equals(cardId.ToString(), "CRED_01", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 检测推荐的来源卡牌是否已消费：已不在手牌，或已进入坟场/奥秘区。
        /// </summary>
        private static bool IsPreferredCardAlreadyPlayed(Board board, LocalBoxOcrState state)
        {
            if (board == null || state == null || IsTeacherUnknownCardId(state.PreferredSourceId))
                return false;

            // 攻击的来源是场上随从，不适用"已打出"检测
            if (string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                if (board.Hand != null)
                {
                    bool stillInHand = board.Hand.Any(card =>
                        card != null
                        && card.Template != null
                        && card.Template.Id == state.PreferredSourceId);
                    if (!stillInHand)
                        return true;
                }

                if (board.FriendGraveyard != null && board.FriendGraveyard.Contains(state.PreferredSourceId))
                    return true;

                if (board.Secret != null && board.Secret.Contains(state.PreferredSourceId))
                    return true;
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool HasLocalBoxOcrActionStepSignal(LocalBoxOcrActionStep step)
        {
            if (step == null)
                return false;

            return !string.IsNullOrWhiteSpace(step.ActionType)
                || !string.IsNullOrWhiteSpace(step.SourceKind)
                || !IsTeacherUnknownCardId(step.SourceId)
                || step.SourceSlot > 0
                || step.ChoiceIndex > 0
                || !string.IsNullOrWhiteSpace(step.Delivery)
                || step.BoardSlot > 0
                || step.ActionRequired
                || !string.IsNullOrWhiteSpace(step.TargetKind)
                || !IsTeacherUnknownCardId(step.TargetId)
                || step.TargetSlot > 0;
        }

        private static void TryFinalizeLocalBoxOcrActionStep(LocalBoxOcrState state, LocalBoxOcrActionStep step)
        {
            if (state == null || !HasLocalBoxOcrActionStepSignal(step))
                return;

            if (step.StepIndex <= 0)
                step.StepIndex = state.ActionSteps.Count + 1;

            state.ActionSteps.Add(step);
        }

        private static void ApplyLocalBoxOcrActionStep(LocalBoxOcrState state, LocalBoxOcrActionStep step)
        {
            if (state == null || step == null)
                return;

            state.PreferredActionType = step.ActionType ?? string.Empty;
            state.PreferredSourceKind = step.SourceKind ?? string.Empty;
            state.PreferredSourceId = step.SourceId;
            state.PreferredSourceSlot = step.SourceSlot;
            state.PreferredStepIndex = step.StepIndex;
            state.PreferredChoiceIndex = step.ChoiceIndex;
            state.PreferredDelivery = step.Delivery ?? string.Empty;
            state.PreferredBoardSlot = step.BoardSlot;
            state.PreferredTargetKind = step.TargetKind ?? string.Empty;
            state.PreferredTargetId = step.TargetId;
            state.PreferredTargetSlot = step.TargetSlot;
            state.PreferredActionRequired = step.ActionRequired;
        }

        private static bool IsCurrentPreferredActionStep(LocalBoxOcrState state, LocalBoxOcrActionStep step)
        {
            if (state == null || step == null)
                return false;

            return state.PreferredStepIndex == step.StepIndex
                && string.Equals(state.PreferredActionType ?? string.Empty, step.ActionType ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(state.PreferredSourceKind ?? string.Empty, step.SourceKind ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && state.PreferredSourceId == step.SourceId
                && state.PreferredSourceSlot == step.SourceSlot
                && state.PreferredChoiceIndex == step.ChoiceIndex
                && string.Equals(state.PreferredDelivery ?? string.Empty, step.Delivery ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && state.PreferredBoardSlot == step.BoardSlot
                && string.Equals(state.PreferredTargetKind ?? string.Empty, step.TargetKind ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && state.PreferredTargetId == step.TargetId
                && state.PreferredTargetSlot == step.TargetSlot
                && state.PreferredActionRequired == step.ActionRequired;
        }

        private static bool HasStrongTeacherActionIdentity(LocalBoxOcrActionStep step)
        {
            if (step == null)
                return false;

            return !IsTeacherUnknownCardId(step.SourceId)
                || !IsTeacherUnknownCardId(step.TargetId)
                || string.Equals(step.SourceKind, "hero_power", StringComparison.OrdinalIgnoreCase)
                || string.Equals(step.SourceKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(step.SourceKind, "own_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(step.SourceKind, "friendly_location", StringComparison.OrdinalIgnoreCase);
        }

        private static LocalBoxOcrState BuildActionStepEvaluationState(LocalBoxOcrState state, LocalBoxOcrActionStep step)
        {
            if (state == null || step == null)
                return null;

            LocalBoxOcrState stepState = new LocalBoxOcrState();
            stepState.TimestampUtc = state.TimestampUtc;
            stepState.Status = state.Status;
            stepState.Stage = state.Stage;
            stepState.SBProfile = state.SBProfile;
            stepState.BoardSignature = state.BoardSignature;
            stepState.EndTurnRecommended = ContainsEndTurnHint(step.ActionType);
            ApplyLocalBoxOcrActionStep(stepState, step);
            return stepState;
        }

        private static bool TryResolveExactTeacherPlayCard(Board board, LocalBoxOcrState state, out Card handCard, out Card targetCard)
        {
            handCard = null;
            targetCard = null;

            if (board == null || board.Hand == null || state == null)
                return false;

            if (!string.Equals(state.PreferredActionType, "PlayCard", StringComparison.OrdinalIgnoreCase))
                return false;

            if (HasPreferredTarget(state))
            {
                targetCard = FindPreferredTarget(board, state);
                if (targetCard == null || targetCard.Template == null)
                    return false;
            }

            if (state.PreferredSourceSlot > 0
                && state.PreferredSourceSlot <= board.Hand.Count)
            {
                Card bySlot = board.Hand[state.PreferredSourceSlot - 1];
                if (bySlot != null && bySlot.Template != null)
                {
                    if (IsTeacherUnknownCardId(state.PreferredSourceId)
                        || bySlot.Template.Id == state.PreferredSourceId)
                    {
                        handCard = bySlot;
                        return true;
                    }
                }
                // slot 不匹配时降级到 ID-only 匹配
            }

            // ── ID-only 匹配 ──
            if (!IsTeacherUnknownCardId(state.PreferredSourceId))
            {
                Card matched = null;
                int matchCount = 0;
                foreach (Card card in board.Hand)
                {
                    if (card == null || card.Template == null || card.Template.Id != state.PreferredSourceId)
                        continue;

                    matchCount++;
                    matched = card;
                }

                if (matchCount == 1)
                {
                    handCard = matched;
                    return true;
                }

                // 多张同名：用 slot 消歧
                if (matchCount > 1 && state.PreferredSourceSlot > 0 && state.PreferredSourceSlot <= board.Hand.Count)
                {
                    Card bySlot = board.Hand[state.PreferredSourceSlot - 1];
                    if (bySlot != null && bySlot.Template != null && bySlot.Template.Id == state.PreferredSourceId)
                    {
                        handCard = bySlot;
                        return true;
                    }
                }

                // 多张同名但 slot 无法精确命中时，不再猜测另一张同名牌。
                // 对盒子 source_slot 来说，这代表旧推荐已失真，应等待刷新而不是继续硬出第二张。
                if (matchCount > 1)
                    return false;

                return false;
            }

            // ── cardId 未知且 slot 无效：无法匹配 ──
            return false;
        }

        private static bool TryResolveExactTeacherAttack(
            Board board,
            LocalBoxOcrState state,
            out Card attackSource,
            out Card targetCard,
            out bool heroAttack)
        {
            attackSource = null;
            targetCard = null;
            heroAttack = false;

            if (board == null || state == null)
                return false;

            if (!string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!TryResolveExactTeacherAttackTarget(board, state, out targetCard))
                return false;

            if (!IsTeacherAttackTargetLegallyReachable(board, targetCard))
                return false;

            heroAttack = string.Equals(state.PreferredSourceKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "own_hero", StringComparison.OrdinalIgnoreCase);

            if (heroAttack)
            {
                if (!CanFriendlyHeroAttack(board) || board.HeroFriend == null)
                    return false;

                attackSource = board.HeroFriend;
            }
            else
            {
                if (!TryResolveExactTeacherAttackSource(board, state, targetCard, out attackSource))
                    return false;

                if (!IsFriendlyAttackSourceReady(attackSource))
                    return false;
            }

            if (targetCard == null)
                return false;

            return true;
        }

        private static bool TryResolveExactTeacherAttackSource(
            Board board,
            LocalBoxOcrState state,
            Card targetCard,
            out Card attackSource)
        {
            attackSource = null;
            if (board == null || board.MinionFriend == null || state == null)
                return false;

            Card slotFallback = null;
            if (state.PreferredSourceSlot > 0 && state.PreferredSourceSlot <= board.MinionFriend.Count)
            {
                Card bySlot = board.MinionFriend[state.PreferredSourceSlot - 1];
                if (IsFriendlyAttackSourceReady(bySlot))
                {
                    slotFallback = bySlot;
                    if (IsTeacherUnknownCardId(state.PreferredSourceId) || CardMatches(bySlot, state.PreferredSourceId))
                    {
                        attackSource = bySlot;
                        return true;
                    }
                }
            }

            if (IsTeacherUnknownCardId(state.PreferredSourceId))
                return false;

            List<Card> matches = board.MinionFriend
                .Where(card => IsFriendlyAttackSourceReady(card) && CardMatches(card, state.PreferredSourceId))
                .ToList();
            if (matches.Count == 1)
            {
                attackSource = matches[0];
                return true;
            }

            if (matches.Count > 1 && state.PreferredSourceSlot > 0)
            {
                int bestDistance = int.MaxValue;
                Card best = null;
                bool tie = false;
                foreach (Card match in matches)
                {
                    int liveSlot = board.MinionFriend.IndexOf(match) + 1;
                    int distance = Math.Abs(liveSlot - state.PreferredSourceSlot);
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

                if (!tie && best != null)
                {
                    attackSource = best;
                    return true;
                }
            }

            if (matches.Count > 1 && targetCard != null)
            {
                List<Card> legalMatches = matches
                    .Where(card => CanTeacherAttackSourceLegallyReachTarget(board, card, targetCard))
                    .ToList();
                if (legalMatches.Count == 1)
                {
                    attackSource = legalMatches[0];
                    return true;
                }
            }

            return slotFallback != null
                && matches.Count == 0
                && (IsTeacherUnknownCardId(state.PreferredSourceId) || CardMatches(slotFallback, state.PreferredSourceId))
                ? (attackSource = slotFallback) != null
                : false;
        }

        private static bool IsTeacherAttackTargetLegallyReachable(Board board, Card targetCard)
        {
            if (board == null || targetCard == null)
                return false;

            if (IsEnemyHeroTarget(board, targetCard))
                return !HasEnemyTaunt(board);

            if (IsEnemyMinionTarget(board, targetCard))
            {
                return !targetCard.IsStealth;
            }

            return false;
        }

        private static IEnumerable<string> EnumerateDecisionSupportAssemblyCandidates()
        {
            foreach (string root in EnumerateDecisionSupportSearchRoots())
            {
                yield return Path.Combine(root, "runtime", "compilecheck_decisionplayexecutor", "bin", "Debug", "net8.0_tmp3", "compilecheck_decisionplayexecutor.dll");
                yield return Path.Combine(root, "runtime", "compilecheck_decisionplayexecutor", "bin", "Debug", "net8.0_tmp2", "compilecheck_decisionplayexecutor.dll");
                yield return Path.Combine(root, "runtime", "compilecheck_decisionplayexecutor", "bin", "Debug", "net8.0_tmp", "compilecheck_decisionplayexecutor.dll");
                yield return Path.Combine(root, "runtime", "compilecheck_decisionplayexecutor", "bin", "Debug", "net8.0", "compilecheck_decisionplayexecutor.dll");
                yield return Path.Combine(root, "runtime", "compilecheck_decisionplayexecutor", "bin", "Release", "net8.0", "compilecheck_decisionplayexecutor.dll");
            }
        }

        private static IEnumerable<string> EnumerateDecisionSupportSearchRoots()
        {
            HashSet<string> yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] seeds = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory
            };

            foreach (string seed in seeds)
            {
                string current = NormalizeDecisionSupportSearchRoot(seed);
                for (int depth = 0; depth < 4 && !string.IsNullOrWhiteSpace(current); depth++)
                {
                    if (yielded.Add(current))
                        yield return current;

                    try
                    {
                        DirectoryInfo parent = Directory.GetParent(current);
                        current = parent != null ? parent.FullName : string.Empty;
                    }
                    catch
                    {
                        current = string.Empty;
                    }
                }
            }
        }

        private static string NormalizeDecisionSupportSearchRoot(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            try
            {
                return Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool CanTeacherAttackSourceLegallyReachTarget(Board board, Card attackSource, Card targetCard)
        {
            return IsFriendlyAttackSourceReady(attackSource)
                && IsTeacherAttackTargetLegallyReachable(board, targetCard);
        }

        private static bool TryResolveExactTeacherAttackTarget(Board board, LocalBoxOcrState state, out Card targetCard)
        {
            targetCard = null;
            if (board == null || state == null || !HasPreferredTarget(state))
                return false;

            string kind = state.PreferredTargetKind ?? string.Empty;
            if (string.Equals(kind, "enemy_hero", StringComparison.OrdinalIgnoreCase))
            {
                if (board.HeroEnemy == null || state.PreferredTargetSlot > 0)
                    return false;

                if (!IsTeacherUnknownCardId(state.PreferredTargetId)
                    && !CardMatches(board.HeroEnemy, state.PreferredTargetId))
                {
                    return false;
                }

                targetCard = board.HeroEnemy;
                return true;
            }

            if (string.Equals(kind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "own_hero", StringComparison.OrdinalIgnoreCase))
            {
                if (board.HeroFriend == null || state.PreferredTargetSlot > 0)
                    return false;

                if (!IsTeacherUnknownCardId(state.PreferredTargetId)
                    && !CardMatches(board.HeroFriend, state.PreferredTargetId))
                {
                    return false;
                }

                targetCard = board.HeroFriend;
                return true;
            }

            if (string.Equals(kind, "enemy_minion", StringComparison.OrdinalIgnoreCase))
                return TryResolveExactCardIdentity(board.MinionEnemy, state.PreferredTargetId, state.PreferredTargetSlot, out targetCard);

            if (string.Equals(kind, "friendly_minion", StringComparison.OrdinalIgnoreCase))
                return TryResolveExactCardIdentity(board.MinionFriend, state.PreferredTargetId, state.PreferredTargetSlot, out targetCard);

            if (string.Equals(kind, "enemy_character", StringComparison.OrdinalIgnoreCase))
            {
                if (state.PreferredTargetSlot > 0)
                    return TryResolveExactCardIdentity(board.MinionEnemy, state.PreferredTargetId, state.PreferredTargetSlot, out targetCard);

                if (!IsTeacherUnknownCardId(state.PreferredTargetId))
                {
                    if (CardMatches(board.HeroEnemy, state.PreferredTargetId))
                    {
                        targetCard = board.HeroEnemy;
                        return true;
                    }

                    return TryResolveExactCardIdentity(board.MinionEnemy, state.PreferredTargetId, 0, out targetCard);
                }

                return false;
            }

            if (string.Equals(kind, "friendly_character", StringComparison.OrdinalIgnoreCase))
            {
                if (state.PreferredTargetSlot > 0)
                    return TryResolveExactCardIdentity(board.MinionFriend, state.PreferredTargetId, state.PreferredTargetSlot, out targetCard);

                if (!IsTeacherUnknownCardId(state.PreferredTargetId))
                {
                    if (CardMatches(board.HeroFriend, state.PreferredTargetId))
                    {
                        targetCard = board.HeroFriend;
                        return true;
                    }

                    return TryResolveExactCardIdentity(board.MinionFriend, state.PreferredTargetId, 0, out targetCard);
                }

                return false;
            }

            if (state.PreferredTargetSlot > 0)
            {
                if (TryResolveExactCardIdentity(board.MinionEnemy, state.PreferredTargetId, state.PreferredTargetSlot, out targetCard))
                    return true;

                return TryResolveExactCardIdentity(board.MinionFriend, state.PreferredTargetId, state.PreferredTargetSlot, out targetCard);
            }

            if (!IsTeacherUnknownCardId(state.PreferredTargetId))
            {
                if (TryResolveExactCardIdentity(board.MinionEnemy, state.PreferredTargetId, 0, out targetCard))
                    return true;
                if (TryResolveExactCardIdentity(board.MinionFriend, state.PreferredTargetId, 0, out targetCard))
                    return true;
                if (CardMatches(board.HeroEnemy, state.PreferredTargetId))
                {
                    targetCard = board.HeroEnemy;
                    return true;
                }
                if (CardMatches(board.HeroFriend, state.PreferredTargetId))
                {
                    targetCard = board.HeroFriend;
                    return true;
                }
            }

            return false;
        }

        private static bool CanExecuteTeacherActionStep(Board board, LocalBoxOcrState state, LocalBoxOcrActionStep step)
        {
            if (board == null || state == null || step == null)
                return false;

            if (ContainsEndTurnHint(step.ActionType))
                return true;

            LocalBoxOcrState stepState = BuildActionStepEvaluationState(state, step);
            if (stepState == null)
                return false;

            if (IsTeacherHeroPowerActionType(stepState.PreferredActionType)
                || string.Equals(stepState.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase))
            {
                return CanExecuteTeacherHeroPowerStep(board, stepState);
            }

            if (IsTeacherLocationActionType(stepState)
                || string.Equals(stepState.PreferredSourceKind, "friendly_location", StringComparison.OrdinalIgnoreCase))
            {
                return CanExecuteTeacherLocationStep(board, stepState);
            }

            if (string.Equals(stepState.PreferredActionType, "PlayCard", StringComparison.OrdinalIgnoreCase))
            {
                Card exactHandCard;
                Card exactTargetCard;
                return TryResolveExactTeacherPlayCard(board, stepState, out exactHandCard, out exactTargetCard);
            }

            if (string.Equals(stepState.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
            {
                Card exactAttackSource;
                Card exactAttackTarget;
                bool heroAttack;
                return TryResolveExactTeacherAttack(board, stepState, out exactAttackSource, out exactAttackTarget, out heroAttack);
            }

            string summary;
            return TryApplyBoxOcrPreferredStepBias(new ProfileParameters(BaseProfile.Rush), board, stepState, out summary);
        }

        private static bool CanExecuteTeacherHeroPowerStep(Board board, LocalBoxOcrState stepState)
        {
            if (board == null || stepState == null || board.Ability == null || board.Ability.Template == null)
                return false;

            if (!IsTeacherUnknownCardId(stepState.PreferredSourceId)
                && board.Ability.Template.Id != stepState.PreferredSourceId)
            {
                return false;
            }

            if (!CanUseHeroPowerNow(board, board.ManaAvailable))
                return false;

            if (!HasPreferredTarget(stepState))
                return true;

            Card preferredTarget = FindPreferredTarget(board, stepState);
            return preferredTarget != null && preferredTarget.Template != null;
        }

        private static bool CanExecuteTeacherLocationStep(Board board, LocalBoxOcrState stepState)
        {
            if (board == null || stepState == null)
                return false;

            Card preferredLocation = FindPreferredFriendlyLocation(board, stepState.PreferredSourceId, stepState.PreferredSourceSlot);
            return preferredLocation != null
                && preferredLocation.Template != null
                && !IsLocationExhausted(preferredLocation);
        }

        private static LocalBoxOcrActionStep FindCurrentExecutableTeacherActionStep(Board board, LocalBoxOcrState state)
        {
            if (board == null || state == null || state.ActionSteps == null || state.ActionSteps.Count == 0)
                return null;

            LocalBoxOcrActionStep firstExecutable = null;
            foreach (LocalBoxOcrActionStep step in state.ActionSteps)
            {
                if (!CanExecuteTeacherActionStep(board, state, step))
                    continue;

                if (HasStrongTeacherActionIdentity(step))
                    return step;

                if (firstExecutable == null)
                    firstExecutable = step;
            }

            return firstExecutable;
        }

        private static bool TryPromoteCurrentExecutableTeacherActionStep(Board board, LocalBoxOcrState state, Action<string> addLog)
        {
            if (board == null || state == null || state.ActionSteps == null || state.ActionSteps.Count <= 1)
                return false;

            LocalBoxOcrActionStep step = FindCurrentExecutableTeacherActionStep(board, state);
            if (step == null)
                return false;

            bool changed = !IsCurrentPreferredActionStep(state, step);
            ApplyLocalBoxOcrActionStep(state, step);

            if (changed && addLog != null)
            {
                addLog("[BOXOCR][STEP][PROMOTE] 续走盒子动作链 | step="
                    + state.PreferredStepIndex.ToString(CultureInfo.InvariantCulture)
                    + "/"
                    + state.ActionSteps.Count.ToString(CultureInfo.InvariantCulture)
                    + " | 动作="
                    + DescribeBoxOcrActionTypeForLog(state.PreferredActionType));
            }

            return changed;
        }

        private static bool TryApplyBoxOcrPreferredStepBias(ProfileParameters p, Board board, LocalBoxOcrState state, out string summary)
        {
            summary = string.Empty;
            if (p == null || board == null || state == null)
                return false;

            if (string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
                return TryApplyBoxOcrPreferredAttackBias(p, board, state, out summary);

            bool expectsTarget = HasPreferredTarget(state);
            Card preferredTarget = expectsTarget ? FindPreferredTarget(board, state) : null;
            bool targetMissing = expectsTarget && (preferredTarget == null || preferredTarget.Template == null);
            if (targetMissing
                && string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
                return false;
            // 非攻击动作：目标失踪时降级为无目标出牌（让AI自选目标）

            // 只要当前盒子步骤已经成功定位成非攻击动作，就先把本拍的抢先攻击压死。
            // 后续若老师下一步真要求攻击，下一次重算会重新放行对应攻击步骤。
            Action suppressPrematureAttacks = () => BlockAllAttacksForTeacher(board, p);

            if (string.Equals(state.PreferredActionType, "UseLocation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "friendly_location", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Card preferredLocation = FindPreferredFriendlyLocation(board, state.PreferredSourceId, state.PreferredSourceSlot);
                    if (preferredLocation != null && preferredLocation.Template != null)
                    {
                        p.LocationsModifiers.AddOrUpdate(preferredLocation.Template.Id, new Modifier(-9800));
                        p.LocationsModifiers.AddOrUpdate(preferredLocation.Id, new Modifier(-9800));
                        p.PlayOrderModifiers.AddOrUpdate(preferredLocation.Template.Id, new Modifier(9999));
                        p.PlayOrderModifiers.AddOrUpdate(preferredLocation.Id, new Modifier(9999));
                        suppressPrematureAttacks();
                        summary = BuildCardDisplayName(preferredLocation) + " slot=" + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "?");
                        return true;
                    }

                    if (!IsTeacherUnknownCardId(state.PreferredSourceId))
                    {
                        p.LocationsModifiers.AddOrUpdate(state.PreferredSourceId, new Modifier(-9800));
                        p.PlayOrderModifiers.AddOrUpdate(state.PreferredSourceId, new Modifier(9999));
                        suppressPrematureAttacks();
                        summary = state.PreferredSourceId + " slot=" + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "?");
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }

            if (string.Equals(state.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (board.Ability != null
                        && board.Ability.Template != null
                        && CanUseHeroPowerNow(board, board.ManaAvailable)
                        && (IsTeacherUnknownCardId(state.PreferredSourceId)
                            || board.Ability.Template.Id == state.PreferredSourceId))
                    {
                        if (preferredTarget != null
                            && TryApplyExactHeroPowerModifier(p, board.Ability.Template.Id, preferredTarget.Id, preferredTarget.Template.Id, -9800))
                        {
                            ApplyHeroPowerCastAndOrderBias(p, board, -9800, 9999);
                            suppressPrematureAttacks();
                            summary = "英雄技能(" + board.Ability.Template.Id + ") -> " + BuildCardDisplayName(preferredTarget);
                            return true;
                        }

                        ApplyHeroPowerCastAndOrderBias(p, board, -9800, 9999);
                        suppressPrematureAttacks();
                        summary = "英雄技能(" + board.Ability.Template.Id + ")";
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }

            Card preferredCard = FindPreferredHandCard(board, state);
            if (preferredCard == null || preferredCard.Template == null)
            {
                // 手牌未找到：检查是否为场上地标的激活操作 (盒子 play_location → PlayCard，但卡已在场上)
                if (!IsTeacherUnknownCardId(state.PreferredSourceId))
                {
                    Card locationOnBoard = FindPreferredFriendlyLocation(board, state.PreferredSourceId, state.PreferredSourceSlot);
                    if (locationOnBoard != null && locationOnBoard.Template != null)
                    {
                        p.LocationsModifiers.AddOrUpdate(locationOnBoard.Template.Id, new Modifier(-9800));
                        p.LocationsModifiers.AddOrUpdate(locationOnBoard.Id, new Modifier(-9800));
                        p.PlayOrderModifiers.AddOrUpdate(locationOnBoard.Template.Id, new Modifier(9999));
                        p.PlayOrderModifiers.AddOrUpdate(locationOnBoard.Id, new Modifier(9999));
                        suppressPrematureAttacks();
                        summary = BuildCardDisplayName(locationOnBoard) + " [UseLocation自动降级] slot="
                            + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "?");
                        return true;
                    }
                }
                return false;
            }

            int manaAvailable = GetAvailableManaIncludingBurstManaCards(board);
            if (preferredCard.CurrentCost > manaAvailable)
                return false;

            ForcePreferredCardCombo(p, preferredCard);
            int preferredChoiceIndex = ResolvePreferredChoiceIndex(state, preferredCard, preferredTarget);
            if (preferredChoiceIndex > 0)
                ApplyChoiceBias(p, preferredCard.Template.Id, preferredChoiceIndex, -9800);

            if (preferredTarget != null
                && TryApplyExactCardCastModifier(
                    p,
                    preferredCard,
                    preferredTarget.Id,
                    preferredTarget.Template.Id,
                    -9800))
            {
                p.PlayOrderModifiers.AddOrUpdate(preferredCard.Template.Id, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(preferredCard.Id, new Modifier(9999));
                suppressPrematureAttacks();
                summary = BuildCardDisplayName(preferredCard)
                    + " -> "
                    + BuildCardDisplayName(preferredTarget)
                    + " 槽位="
                    + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "?")
                    + (preferredCard.Type == Card.CType.MINION && state.PreferredBoardSlot > 0
                        ? " | 落位=" + state.PreferredBoardSlot.ToString()
                        : string.Empty)
                    + (preferredChoiceIndex > 0 ? " | 选项=" + preferredChoiceIndex : string.Empty);
                return true;
            }

            ApplyCardBias(p, preferredCard, -9800, -9800, 9999, 9999);
            suppressPrematureAttacks();
            summary = BuildCardDisplayName(preferredCard)
                + (targetMissing ? " [目标失踪,AI自选]" : string.Empty)
                + " 槽位="
                + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "?")
                + (preferredCard.Type == Card.CType.MINION && state.PreferredBoardSlot > 0
                    ? " | 落位=" + state.PreferredBoardSlot.ToString()
                    : string.Empty)
                + (preferredChoiceIndex > 0 ? " | 选项=" + preferredChoiceIndex : string.Empty);
            return true;
        }

        private static bool TryApplyBoxOcrPreferredAttackBias(ProfileParameters p, Board board, LocalBoxOcrState state, out string summary)
        {
            summary = string.Empty;
            if (p == null || board == null || state == null)
                return false;

            Card attackSource;
            Card target;
            bool heroAttack;
            if (!TryResolveExactTeacherAttack(board, state, out attackSource, out target, out heroAttack))
                return false;

            SuppressNonAttackActionsForForcedAttack(board, p);
            if (!ConfigureForcedAttackSource(board, p, attackSource, heroAttack))
                return false;
            BlockCompetingTeacherAttacks(board, p, attackSource, target, heroAttack);

            if (IsEnemyMinionTarget(board, target)
                && target.Template != null)
            {
                bool exactApplied = heroAttack
                    ? TryApplyExactWeaponAttackModifier(
                        p,
                        attackSource.Id,
                        GetHeroAttackCardId(board),
                        target.Id,
                        target.Template.Id,
                        5000)
                    : TryApplyExactAttackModifier(
                        p,
                        attackSource.Id,
                        attackSource.Template.Id,
                        target.Id,
                        target.Template.Id,
                        5000);

                if (!exactApplied)
                    return false;

                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(target.Template.Id, new Modifier(ForceEnemyAttackTargetValue));
                summary = "攻击(" + BuildCardDisplayName(attackSource) + " -> " + BuildCardDisplayName(target) + ")"
                    + " | sourceEntity=" + attackSource.Id.ToString(CultureInfo.InvariantCulture)
                    + " | sourceSlot=" + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "?")
                    + " | targetEntity=" + target.Id.ToString(CultureInfo.InvariantCulture)
                    + " | targetSlot=" + (state.PreferredTargetSlot > 0 ? state.PreferredTargetSlot.ToString() : "?");
                return true;
            }

            if (IsEnemyHeroTarget(board, target))
            {
                if (HasEnemyTaunt(board))
                    return false;

                if (board.MinionEnemy != null)
                {
                    foreach (Card enemy in board.MinionEnemy)
                    {
                        if (enemy == null || enemy.Template == null)
                            continue;

                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-9999));
                    }
                }

                bool exactApplied = heroAttack
                    ? TryApplyExactWeaponAttackModifier(
                        p,
                        attackSource.Id,
                        GetHeroAttackCardId(board),
                        target.Id,
                        target.Template != null ? target.Template.Id : default(Card.Cards),
                        5000)
                    : TryApplyExactAttackModifier(
                        p,
                        attackSource.Id,
                        attackSource.Template.Id,
                        target.Id,
                        target.Template != null ? target.Template.Id : default(Card.Cards),
                        5000);

                if (!exactApplied)
                {
                    if (heroAttack)
                    {
                        p.GlobalWeaponsAttackModifier = new Modifier(PreferHeroAttackValue);
                        p.WeaponsAttackModifiers.AddOrUpdate(attackSource.Id, new Modifier(PreferHeroAttackValue));
                        if (board.WeaponFriend != null && board.WeaponFriend.Template != null)
                            p.WeaponsAttackModifiers.AddOrUpdate(board.WeaponFriend.Template.Id, new Modifier(PreferHeroAttackValue));
                    }
                    else if (p.GlobalAggroModifier != null)
                    {
                        p.GlobalAggroModifier.Value = Math.Max(p.GlobalAggroModifier.Value, 300);
                    }
                    else
                    {
                        p.GlobalAggroModifier = new Modifier(300);
                    }
                }

                summary = "攻击(" + BuildCardDisplayName(attackSource) + " -> " + BuildCardDisplayName(target) + ")"
                    + " | sourceEntity=" + attackSource.Id.ToString(CultureInfo.InvariantCulture)
                    + " | sourceSlot=" + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "?")
                    + " | targetEntity=" + target.Id.ToString(CultureInfo.InvariantCulture)
                    + " | targetSlot=" + (state.PreferredTargetSlot > 0 ? state.PreferredTargetSlot.ToString() : "?");
                return true;
            }

            return false;
        }

        private static void SuppressNonAttackActionsForForcedAttack(Board board, ProfileParameters p)
        {
            if (board == null || p == null)
                return;

            try
            {
                if (board.Hand != null)
                {
                    foreach (Card card in board.Hand)
                    {
                        if (card == null || card.Template == null)
                            continue;

                        ApplyCardBias(p, card, 9999, 9999, -9999, -9999);
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.Ability != null && board.Ability.Template != null)
                {
                    ApplyHeroPowerCastAndOrderBias(p, board, 9999, -9999);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static bool ConfigureForcedAttackSource(Board board, ProfileParameters p, Card preferredSource, bool heroAttack)
        {
            if (board == null || p == null || preferredSource == null || preferredSource.Template == null)
                return false;

            try
            {
                if (board.MinionFriend != null)
                {
                    foreach (Card friend in board.MinionFriend)
                    {
                        if (friend == null || friend.Template == null)
                            continue;

                        if (IsLocationCard(friend))
                        {
                            p.LocationsModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                            p.LocationsModifiers.AddOrUpdate(friend.Id, new Modifier(9999));
                            continue;
                        }

                        if (!friend.CanAttack)
                            continue;

                        if (!heroAttack && friend.Id == preferredSource.Id)
                        {
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(PreferAttackBodyValue));
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Id, new Modifier(PreferAttackBodyValue));
                            p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(PreferAttackOrderValue));
                            p.AttackOrderModifiers.AddOrUpdate(friend.Id, new Modifier(PreferAttackOrderValue));
                        }
                        else
                        {
                            if (friend.Template.Id != preferredSource.Template.Id)
                            {
                                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(DelayAttackBodyValue));
                                p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(DelayAttackValue));
                            }

                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Id, new Modifier(DelayAttackBodyValue));
                            p.AttackOrderModifiers.AddOrUpdate(friend.Id, new Modifier(DelayAttackValue));
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            if (heroAttack)
            {
                if (!CanFriendlyHeroAttack(board))
                    return false;

                try
                {
                    p.GlobalWeaponsAttackModifier = new Modifier(PreferHeroAttackValue);
                    p.WeaponsAttackModifiers.AddOrUpdate(preferredSource.Id, new Modifier(PreferHeroAttackValue));
                    if (board.WeaponFriend != null && board.WeaponFriend.Template != null)
                        p.WeaponsAttackModifiers.AddOrUpdate(board.WeaponFriend.Template.Id, new Modifier(PreferHeroAttackValue));
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                BlockHeroAttack(board, p);
            }

            return true;
        }

        private static void BlockHeroAttack(Board board, ProfileParameters p)
        {
            if (board == null || p == null || !CanFriendlyHeroAttack(board))
                return;

            try
            {
                p.GlobalWeaponsAttackModifier = new Modifier(BlockHeroAttackValue);

                if (board.HeroFriend != null)
                    p.WeaponsAttackModifiers.AddOrUpdate(board.HeroFriend.Id, new Modifier(BlockHeroAttackValue));

                Card.Cards heroAttackCardId = GetHeroAttackCardId(board);
                if (heroAttackCardId != default(Card.Cards))
                    p.WeaponsAttackModifiers.AddOrUpdate(heroAttackCardId, new Modifier(BlockHeroAttackValue));

                if (board.WeaponFriend != null)
                {
                    if (board.WeaponFriend.Template != null)
                        p.WeaponsAttackModifiers.AddOrUpdate(board.WeaponFriend.Template.Id, new Modifier(BlockHeroAttackValue));

                    p.WeaponsAttackModifiers.AddOrUpdate(board.WeaponFriend.Id, new Modifier(BlockHeroAttackValue));
                }
            }
            catch
            {
                // ignore
            }
        }

        private static bool CanFriendlyHeroAttack(Board board)
        {
            if (board == null || board.HeroFriend == null)
                return false;

            try
            {
                if (board.HeroFriend.IsFrozen)
                    return false;

                if (board.HeroFriend.CanAttack)
                    return true;
            }
            catch
            {
                // ignore
            }

            try
            {
                bool hasWeaponAttack = board.WeaponFriend != null
                    && board.WeaponFriend.Template != null
                    && board.WeaponFriend.CurrentDurability > 0
                    && board.WeaponFriend.CurrentAtk > 0;
                if (hasWeaponAttack && board.HeroFriend.CountAttack == 0)
                    return true;
            }
            catch
            {
                // ignore
            }

            try
            {
                return board.HeroFriend.CurrentAtk > 0 && board.HeroFriend.CountAttack == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasEnemyTaunt(Board board)
        {
            if (board == null || board.MinionEnemy == null)
                return false;

            foreach (Card enemy in board.MinionEnemy)
            {
                if (enemy != null && enemy.IsTaunt)
                    return true;
            }

            return false;
        }

        private static bool IsEnemyHeroTarget(Board board, Card target)
        {
            return board != null
                && board.HeroEnemy != null
                && target != null
                && target.Id == board.HeroEnemy.Id;
        }

        private static bool IsEnemyMinionTarget(Board board, Card target)
        {
            if (board == null || target == null || board.MinionEnemy == null)
                return false;

            foreach (Card enemy in board.MinionEnemy)
            {
                if (enemy != null && enemy.Id == target.Id)
                    return true;
            }

            return false;
        }

        private static Card.Cards GetHeroAttackCardId(Board board)
        {
            if (board != null && board.WeaponFriend != null && board.WeaponFriend.Template != null)
                return board.WeaponFriend.Template.Id;

            if (board != null && board.HeroFriend != null && board.HeroFriend.Template != null)
                return board.HeroFriend.Template.Id;

            return default(Card.Cards);
        }

        private static Card FindPreferredHandCard(Board board, LocalBoxOcrState state)
        {
            if (board == null || board.Hand == null || state == null)
                return null;

            // ── 优先用 cardId 匹配 ──
            if (!IsTeacherUnknownCardId(state.PreferredSourceId))
            {
                Card matched = null;
                int matchCount = 0;
                foreach (Card card in board.Hand)
                {
                    if (card == null || card.Template == null || card.Template.Id != state.PreferredSourceId)
                        continue;
                    matchCount++;
                    matched = card;
                }

                if (matchCount == 1)
                    return matched;

                // 多张同名卡：用 slot 消歧
                if (matchCount > 1 && state.PreferredSourceSlot > 0 && state.PreferredSourceSlot <= board.Hand.Count)
                {
                    Card bySlot = board.Hand[state.PreferredSourceSlot - 1];
                    if (bySlot != null && bySlot.Template != null && bySlot.Template.Id == state.PreferredSourceId)
                        return bySlot;
                }

                // 多张同名但 slot 无法精确命中：返回空，交给 WAIT/HOLD 等刷新。
                if (matchCount > 1)
                    return null;
            }

            // ── cardId 未知时，按 slot 回退 ──
            if (state.PreferredSourceSlot > 0 && state.PreferredSourceSlot <= board.Hand.Count)
            {
                Card bySlot = board.Hand[state.PreferredSourceSlot - 1];
                if (bySlot != null
                    && bySlot.Template != null
                    && (IsTeacherUnknownCardId(state.PreferredSourceId)
                        || ScoreCardAgainstPlayTextHints(bySlot, state) > 0))
                {
                    return bySlot;
                }
            }

            Card recommendedCard = FindRecommendedHandCard(board.Hand, state);
            if (recommendedCard != null)
                return recommendedCard;

            return FindBestTextMatchedCard(board.Hand, state, null);
        }

        private static Card FindRecommendedHandCard(IEnumerable<Card> cards, LocalBoxOcrState state)
        {
            if (cards == null || state == null || state.RecommendedCards.Count == 0)
                return null;

            Card bestTextMatched = FindBestTextMatchedCard(
                cards,
                state,
                card => card != null && card.Template != null && state.RecommendedCards.Contains(card.Template.Id));
            if (bestTextMatched != null)
                return bestTextMatched;

            Card firstRecommended = null;
            Card.Cards firstRecommendedId = default(Card.Cards);
            bool multipleRecommendedIds = false;
            foreach (Card card in cards)
            {
                if (card == null || card.Template == null || !state.RecommendedCards.Contains(card.Template.Id))
                    continue;

                if (firstRecommended == null)
                {
                    firstRecommended = card;
                    firstRecommendedId = card.Template.Id;
                    continue;
                }

                if (card.Template.Id != firstRecommendedId)
                {
                    multipleRecommendedIds = true;
                    break;
                }
            }

            return multipleRecommendedIds ? null : firstRecommended;
        }

        private static bool HasTeacherActionRecommendation(Board board, LocalBoxOcrState state)
        {
            if (state == null)
                return false;

            if (HasTeacherAttackIntent(state))
                return true;

            if (IsCurrentTeacherEndTurnIntent(state))
                return true;

            if (string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
                return HasExecutableTeacherAttackRecommendation(board, state);

            if (!string.IsNullOrWhiteSpace(state.PreferredActionType))
                return true;

            if (!string.IsNullOrWhiteSpace(state.PreferredSourceKind))
                return true;

            if (!IsTeacherUnknownCardId(state.PreferredSourceId))
                return true;

            if (state.PreferredSourceSlot > 0
                || state.PreferredChoiceIndex > 0
                || state.PreferredTargetSlot > 0
                || !string.IsNullOrWhiteSpace(state.PreferredTargetKind)
                || !IsTeacherUnknownCardId(state.PreferredTargetId))
                return true;

            if (state.RecommendedHeroPowers.Count > 0)
                return true;

            if (board != null && FindPreferredHandCard(board, state) != null)
                return true;

            return false;
        }

        private static bool HasTeacherAttackIntent(LocalBoxOcrState state)
        {
            if (state == null || !string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
                return false;

            bool hasSource = !string.IsNullOrWhiteSpace(state.PreferredSourceKind)
                || !IsTeacherUnknownCardId(state.PreferredSourceId)
                || state.PreferredSourceSlot > 0;
            bool hasTarget = !string.IsNullOrWhiteSpace(state.PreferredTargetKind)
                || !IsTeacherUnknownCardId(state.PreferredTargetId)
                || state.PreferredTargetSlot > 0;
            return hasSource && hasTarget;
        }

        private static bool HasExecutableTeacherAttackRecommendation(Board board, LocalBoxOcrState state)
        {
            Card attackSource;
            Card targetCard;
            bool heroAttack;
            return TryResolveExactTeacherAttack(board, state, out attackSource, out targetCard, out heroAttack);
        }

        private static bool TryApplyFreshTeacherRefreshHoldOnSignatureMismatch(
            ProfileParameters p,
            Board board,
            LocalBoxOcrState state,
            Action<string> addLog,
            bool holdAttacks,
            string holdReasonCode)
        {
            if (p == null || board == null || state == null)
                return false;

            int freshnessWindow = HasTeacherActionRecommendation(board, state)
                ? SignatureMismatchTeacherRefreshHoldSeconds
                : FreshTeacherRefreshHoldSeconds;
            if (!board.IsOwnTurn || !state.IsFresh(freshnessWindow))
                return false;

            if (!HasFreshTeacherSignal(state))
                return false;

            // Signature mismatch means the previous preferred source can already be stale.
            // Use hard penalty (9999) to truly block all non-teacher actions — default 500 is
            // too weak for 0-cost / high-value cards like Innervate that the AI engine ignores.
            int holdCount = BumpTeacherGuardReasonCounter(
                "hold_signature_mismatch",
                string.IsNullOrWhiteSpace(holdReasonCode) ? "signature_mismatch" : holdReasonCode);
            string locationDiagnostic = BuildTeacherLocationHoldDiagnostic(board, state);
            ApplyHoldForTeacherRefresh(board, p, holdAttacks, state, false, false, true);
            if (addLog != null)
            {
                addLog(
                    "[BOXOCR][STATE][HOLD] 棋盘签名不匹配，硬挡等待盒子刷新"
                    + " | 原因=" + (string.IsNullOrWhiteSpace(holdReasonCode) ? "signature_mismatch" : holdReasonCode)
                    + " | 拦攻击=" + (holdAttacks ? "是" : "否")
                    + " | 保留旧源动作=否"
                    + " | 挡级=hard(9999)"
                    + " | 有效窗=" + freshnessWindow
                    + locationDiagnostic
                    + " | 命中次数=" + holdCount.ToString(CultureInfo.InvariantCulture)
                    + " | 已过秒数=" + GetLocalBoxOcrStateAgeSeconds(state));
            }

            return true;
        }

        private static bool TryApplyStrictExecutableTeacherStepOnSignatureMismatch(
            ProfileParameters p,
            Board board,
            LocalBoxOcrState state,
            string currentBoardSignature,
            Action<string> addLog,
            out string summary)
        {
            summary = string.Empty;
            if (p == null || board == null || state == null)
                return false;

            LocalBoxOcrActionStep executableStep = null;
            LocalBoxOcrActionStep currentPreferredStep = FindCurrentPreferredActionStep(state);
            if (currentPreferredStep != null && CanExecuteTeacherActionStep(board, state, currentPreferredStep))
                executableStep = currentPreferredStep;

            if (executableStep == null)
                executableStep = FindCurrentExecutableTeacherActionStep(board, state);

            if (executableStep == null)
                return false;

            LocalBoxOcrState executableState = BuildActionStepEvaluationState(state, executableStep);
            if (executableState == null)
                return false;

            string forcedSummary;
            bool forcedApplied = TryApplyBoxOcrPreferredStepBias(p, board, executableState, out forcedSummary);
            if (!forcedApplied)
                return false;

            string strictSummary;
            if (!TryApplyTeacherOnlyPlanBias(p, board, executableState, true, out strictSummary))
                return false;

            ApplyLocalBoxOcrActionStep(state, executableStep);
            TryRefreshLocalBoxOcrBoardSignature(currentBoardSignature);
            RememberAcceptedTeacherSnapshot(board, state, currentBoardSignature);
            summary = strictSummary;

            if (addLog != null)
            {
                addLog("[BOXOCR][STRICT] 棋盘签名不匹配，但当前盒子步骤可精确执行，继续强制落地"
                    + " | step="
                    + (state.PreferredStepIndex > 0 ? state.PreferredStepIndex.ToString(CultureInfo.InvariantCulture) : "?")
                    + "/"
                    + state.ActionSteps.Count.ToString(CultureInfo.InvariantCulture)
                    + " | 动作="
                    + DescribeBoxOcrActionTypeForLog(state.PreferredActionType));
                addLog(">> OCR STRICT: " + strictSummary);
            }

            return true;
        }

        private static LocalBoxOcrActionStep FindCurrentPreferredActionStep(LocalBoxOcrState state)
        {
            if (state == null || state.ActionSteps == null || state.ActionSteps.Count == 0)
                return null;

            foreach (LocalBoxOcrActionStep step in state.ActionSteps)
            {
                if (IsCurrentPreferredActionStep(state, step))
                    return step;
            }

            return null;
        }

        private static bool TryApplyPendingTeacherRefreshHold(
            ProfileParameters p,
            Board board,
            LocalBoxOcrState state,
            Action<string> addLog)
        {
            if (p == null || board == null || state == null)
                return false;

            if (!board.IsOwnTurn || !state.IsFresh(PendingTeacherRefreshHoldSeconds))
                return false;

            bool pendingStatus = ShouldTreatEmptyTeacherStateAsPendingRefresh(state)
                || state.RefreshPending
                || string.Equals(state.Status, "refreshing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.Status, "transition", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(state.Status, "ok", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(state.Stage, "play", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(state.Stage, "mulligan", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(state.Stage, "discover", StringComparison.OrdinalIgnoreCase)));
            if (!pendingStatus)
                return false;

            double pendingAgeSeconds = GetLocalBoxOcrStateAgeSeconds(state);
            bool hasTeacherRecommendation = HasTeacherActionRecommendation(board, state);
            bool keepAbsolutePendingHold = state.RefreshPending
                || string.Equals(state.Status, "refreshing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.Status, "transition", StringComparison.OrdinalIgnoreCase);
            bool forceAbsoluteHold = !hasTeacherRecommendation || keepAbsolutePendingHold;
            bool useHardPendingHold = forceAbsoluteHold || pendingAgeSeconds <= PendingTeacherRefreshHardHoldSeconds;
            bool useStrongPendingHold = !useHardPendingHold
                && pendingAgeSeconds <= PendingTeacherRefreshStrongHoldSeconds;
            string teacherActionSummary = BuildAcceptedTeacherActionSummary(state);
            string holdReasonCode = string.IsNullOrWhiteSpace(state.StatusReason) ? "refresh_pending" : state.StatusReason;
            int holdCount = BumpTeacherGuardReasonCounter("hold_pending", holdReasonCode);
            string locationDiagnostic = BuildTeacherLocationHoldDiagnostic(board, state);
            ApplyHoldForTeacherRefresh(board, p, true, state, true, useStrongPendingHold, useHardPendingHold, forceAbsoluteHold);
            if (addLog != null)
            {
                string observedStage = !string.IsNullOrWhiteSpace(state.ObservedStage)
                    ? DescribeBoxOcrStageForLog(state.ObservedStage)
                    : DescribeBoxOcrStageForLog(state.Stage);
                addLog(
                    "[BOXOCR][STATE][HOLD] 盒子正在刷新，暂不放默认逻辑抢跑"
                    + " | 状态=" + DescribeBoxOcrStatusForLog(state.Status)
                    + " | 原因=" + holdReasonCode
                    + " | 观察阶段=" + observedStage
                    + " | 触发=" + (string.IsNullOrWhiteSpace(state.RefreshTrigger) ? "(无)" : state.RefreshTrigger)
                    + " | 力度=" + (forceAbsoluteHold ? "绝对硬挡" : (useHardPendingHold ? "硬挡" : (useStrongPendingHold ? "加强" : "标准")))
                    + " | 刷新绝对挡=" + (keepAbsolutePendingHold ? "是" : "否")
                    + " | 推荐动作=" + (hasTeacherRecommendation ? "有" : "无")
                    + " | 动作=" + (string.IsNullOrWhiteSpace(teacherActionSummary) ? "(无)" : teacherActionSummary)
                    + locationDiagnostic
                    + " | 命中次数=" + holdCount.ToString(CultureInfo.InvariantCulture)
                    + " | 已过秒数=" + GetLocalBoxOcrStateAgeSeconds(state));
            }

            return true;
        }

        private static bool TryApplyAcceptedTeacherSnapshotWaitHold(
            ProfileParameters p,
            Board board,
            LocalBoxOcrState state,
            string currentBoardSignature,
            Action<string> addLog,
            out bool completedActionBailout)
        {
            completedActionBailout = false;
            if (p == null || board == null || state == null)
                return false;

            if (!board.IsOwnTurn || !state.IsFresh(FreshTeacherRefreshHoldSeconds))
                return false;

            if (!HasFreshTeacherSignal(state))
                return false;

            if (state.RefreshPending
                || string.Equals(state.Status, "refreshing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.Status, "transition", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            DateTime acceptedSnapshotUtc;
            string acceptedBoardSignature;
            string acceptedActionSummary;
            lock (AcceptedTeacherSnapshotSync)
            {
                acceptedSnapshotUtc = _lastAcceptedTeacherSnapshotUtc;
                acceptedBoardSignature = _lastAcceptedTeacherBoardSignature;
                acceptedActionSummary = _lastAcceptedTeacherActionSummary;
            }

            if (acceptedSnapshotUtc == DateTime.MinValue)
                return false;

            bool exactTimestampMatch = (acceptedSnapshotUtc == state.TimestampUtc);
            // 新OCR到达（时间戳不同），但动作摘要与上次已接受的完全相同，说明盒子文本尚未更新，
            // 视为陈旧重复快照，仍然触发WAIT_NEXT hold。
            bool staleRepeat = false;
            if (!exactTimestampMatch)
            {
                string currentActionSummaryEarly = BuildAcceptedTeacherActionSummary(state);
                if (!string.IsNullOrWhiteSpace(currentActionSummaryEarly)
                    && !string.IsNullOrWhiteSpace(acceptedActionSummary)
                    && string.Equals(currentActionSummaryEarly, acceptedActionSummary, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(acceptedBoardSignature)
                    && !string.IsNullOrWhiteSpace(currentBoardSignature)
                    && !string.Equals(acceptedBoardSignature.Trim(), currentBoardSignature.Trim(), StringComparison.Ordinal))
                {
                    staleRepeat = true;
                }
            }

            if (!exactTimestampMatch && !staleRepeat)
                return false;

            string currentActionSummary = BuildAcceptedTeacherActionSummary(state);
            if (!string.IsNullOrWhiteSpace(currentActionSummary)
                && !string.IsNullOrWhiteSpace(acceptedActionSummary)
                && !string.Equals(currentActionSummary, acceptedActionSummary, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(acceptedBoardSignature) || string.IsNullOrWhiteSpace(currentBoardSignature))
                return false;

            if (string.Equals(acceptedBoardSignature.Trim(), currentBoardSignature.Trim(), StringComparison.Ordinal))
                return false;

            bool repeatedStaleEndTurn = staleRepeat
                && !string.IsNullOrWhiteSpace(currentActionSummary)
                && !string.IsNullOrWhiteSpace(acceptedActionSummary)
                && ContainsEndTurnHint(currentActionSummary)
                && ContainsEndTurnHint(acceptedActionSummary);
            if (repeatedStaleEndTurn)
            {
                bool naturallyAcceptableEndTurn;
                string endTurnReadinessSummary = BuildTeacherEndTurnReadinessSummary(board, out naturallyAcceptableEndTurn);
                if (!naturallyAcceptableEndTurn)
                {
                    lock (AcceptedTeacherSnapshotSync)
                    {
                        _lastAcceptedTeacherSnapshotUtc = DateTime.MinValue;
                        _lastAcceptedTeacherBoardSignature = string.Empty;
                        _lastAcceptedTeacherActionSummary = string.Empty;
                        _lastAcceptedTeacherPlaySourceId = default(Card.Cards);
                        _lastAcceptedTeacherPlaySourceSlot = 0;
                        _lastAcceptedTeacherPlayHandMatchCount = -1;
                    }

                    completedActionBailout = true;
                    if (addLog != null)
                    {
                        int dropCount = BumpTeacherGuardReasonCounter("wait_next_drop", "stale_repeat_end_turn");
                        addLog("[BOXOCR][STATE][WAIT_NEXT_DROP] 检测到陈旧重复的结束回合快照，当前局面仍有动作可做，丢弃旧快照并放行默认逻辑等待新推荐"
                            + " | 上一步=" + acceptedActionSummary
                            + " | 当前=" + currentActionSummary
                            + " | 命中次数=" + dropCount.ToString(CultureInfo.InvariantCulture)
                            + " | " + endTurnReadinessSummary);
                    }
                    return false;
                }
            }

            // 已接受的动作已经执行完毕，但盒子下一拍还没刷新出来时，
            // 继续硬挡等待新推荐，避免默认AI抢跑偏离盒子链路。
            string completedReason;
            if (IsAcceptedActionAlreadyCompleted(board, state, out completedReason))
            {
                if (state.IsFresh(PendingTeacherRefreshHoldSeconds))
                {
                    bool useHardCompletedHold = GetLocalBoxOcrStateAgeSeconds(state) <= PendingTeacherRefreshHardHoldSeconds;
                    bool useStrongCompletedHold = !useHardCompletedHold;
                    ApplyHoldForTeacherRefresh(
                        board,
                        p,
                        true,
                        state,
                        false,
                        useStrongCompletedHold,
                        useHardCompletedHold,
                        true);
                    if (addLog != null)
                    {
                        int waitCount = BumpTeacherGuardReasonCounter("wait_next_completed_hold", completedReason);
                        addLog("[BOXOCR][STATE][WAIT_NEXT_DONE] 上一步盒子动作已完成，但仍未收到下一拍推荐，继续硬挡等待刷新"
                            + " | 上一步=" + (string.IsNullOrWhiteSpace(acceptedActionSummary) ? "(无)" : acceptedActionSummary)
                            + " | 原因=" + (string.IsNullOrWhiteSpace(completedReason) ? "(无)" : completedReason)
                            + " | 力度=" + (useHardCompletedHold ? "硬挡" : "加强")
                            + " | 命中次数=" + waitCount.ToString(CultureInfo.InvariantCulture)
                            + " | 已过秒数=" + GetLocalBoxOcrStateAgeSeconds(state));
                    }
                    return true;
                }

                lock (AcceptedTeacherSnapshotSync)
                {
                    _lastAcceptedTeacherSnapshotUtc = DateTime.MinValue;
                    _lastAcceptedTeacherBoardSignature = string.Empty;
                    _lastAcceptedTeacherActionSummary = string.Empty;
                    _lastAcceptedTeacherPlaySourceId = default(Card.Cards);
                    _lastAcceptedTeacherPlaySourceSlot = 0;
                    _lastAcceptedTeacherPlayHandMatchCount = -1;
                }
                completedActionBailout = true;
                if (addLog != null)
                {
                    int skipCount = BumpTeacherGuardReasonCounter("wait_next_skip", completedReason);
                    addLog("[BOXOCR][STATE][WAIT_NEXT_SKIP] 上一步动作已执行完毕，但等待盒子刷新已超时，放行默认逻辑"
                        + " | 上一步=" + (string.IsNullOrWhiteSpace(acceptedActionSummary) ? "(无)" : acceptedActionSummary)
                        + " | 原因=" + (string.IsNullOrWhiteSpace(completedReason) ? "(无)" : completedReason)
                        + " | 命中次数=" + skipCount.ToString(CultureInfo.InvariantCulture));
                }
                return false;
            }

            double pendingAgeSeconds = GetLocalBoxOcrStateAgeSeconds(state);
            bool useHardPendingHold = pendingAgeSeconds <= PendingTeacherRefreshHardHoldSeconds;
            bool useStrongPendingHold = !useHardPendingHold
                && pendingAgeSeconds <= PendingTeacherRefreshStrongHoldSeconds;
            // 陈旧重复快照至少使用加强力度，防止AI在盒子未更新时抢跑
            if (staleRepeat && !useHardPendingHold)
                useStrongPendingHold = true;
            ApplyHoldForTeacherRefresh(board, p, true, state, false, useStrongPendingHold, useHardPendingHold);
            if (addLog != null)
            {
                int waitCount = BumpTeacherGuardReasonCounter(
                    "wait_next_hold",
                    staleRepeat ? "stale_repeat" : "accepted_snapshot_wait");
                string locationDiagnostic = BuildTeacherLocationHoldDiagnostic(board, state);
                addLog(
                    "[BOXOCR][STATE][WAIT_NEXT] 上一步盒子动作已接受，等待下一拍刷新"
                    + (staleRepeat ? " (陈旧重复快照)" : "")
                    + " | 上一步=" + (string.IsNullOrWhiteSpace(acceptedActionSummary) ? "(无)" : acceptedActionSummary)
                    + " | 当前=" + (string.IsNullOrWhiteSpace(currentActionSummary) ? "(无)" : currentActionSummary)
                    + " | 力度=" + (useHardPendingHold ? "硬挡" : (useStrongPendingHold ? "加强" : "标准"))
                    + locationDiagnostic
                    + " | 命中次数=" + waitCount.ToString(CultureInfo.InvariantCulture)
                    + " | 已过秒数=" + GetLocalBoxOcrStateAgeSeconds(state));
            }

            return true;
        }

        /// <summary>
        /// 检测上一步已接受的盒子动作是否在当前棋盘上已经执行完毕。
        /// 用于 WAIT_NEXT 判断：若动作已完成，不应阻挡后续出牌。
        /// </summary>
        private static bool IsAcceptedActionAlreadyCompleted(Board board, LocalBoxOcrState state)
        {
            string ignoredReason;
            return IsAcceptedActionAlreadyCompleted(board, state, out ignoredReason);
        }

        private static bool IsAcceptedActionAlreadyCompleted(Board board, LocalBoxOcrState state, out string completionReason)
        {
            completionReason = string.Empty;
            if (board == null || state == null)
                return false;

            // 英雄技能已使用
            if (IsTeacherHeroPowerActionType(state.PreferredActionType)
                || string.Equals(state.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    object rawUsed = board.HeroPowerUsedThisTurn;
                    if (rawUsed is bool && (bool)rawUsed)
                    {
                        completionReason = "hero_power_used";
                        return true;
                    }
                    if (rawUsed != null && !(rawUsed is bool) && Convert.ToInt32(rawUsed, CultureInfo.InvariantCulture) > 0)
                    {
                        completionReason = "hero_power_used";
                        return true;
                    }
                }
                catch { }
            }

            // 出牌类型：推荐的卡牌已不在手牌中（已打出到坟场/奥秘区）
            if (string.Equals(state.PreferredActionType, "PlayCard", StringComparison.OrdinalIgnoreCase)
                && !IsTeacherUnknownCardId(state.PreferredSourceId))
            {
                int acceptedHandMatchCount = -1;
                Card.Cards acceptedPlaySourceId = default(Card.Cards);
                int acceptedPlaySourceSlot = 0;
                lock (AcceptedTeacherSnapshotSync)
                {
                    acceptedHandMatchCount = _lastAcceptedTeacherPlayHandMatchCount;
                    acceptedPlaySourceId = _lastAcceptedTeacherPlaySourceId;
                    acceptedPlaySourceSlot = _lastAcceptedTeacherPlaySourceSlot;
                }

                if (acceptedHandMatchCount > 0
                    && acceptedPlaySourceId == state.PreferredSourceId
                    && acceptedPlaySourceSlot == state.PreferredSourceSlot)
                {
                    int currentHandMatchCount = CountHandCardsById(board.Hand, state.PreferredSourceId);
                    if (currentHandMatchCount < acceptedHandMatchCount)
                    {
                        completionReason = "play_card_consumed_count_drop";
                        return true;
                    }
                }

                if (IsPreferredCardAlreadyPlayed(board, state))
                {
                    completionReason = "play_card_consumed";
                    return true;
                }
            }

            if (string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase)
                && IsTeacherAttackActionAlreadyConsumed(board, state, out completionReason))
            {
                return true;
            }

            if (IsTeacherLocationActionType(state)
                && IsTeacherLocationActionAlreadyConsumed(board, state, out completionReason))
            {
                return true;
            }

            return false;
        }

        private static bool TryReloadTeacherStateAfterCompletedAction(
            Board board,
            LocalBoxOcrState state,
            string currentBoardSignature,
            string expectedProfileName,
            Action<string> addLog,
            out LocalBoxOcrState refreshedState)
        {
            refreshedState = null;
            if (board == null || state == null || !board.IsOwnTurn)
                return false;

            if (!state.IsFresh(FreshTeacherRefreshHoldSeconds) || !HasFreshTeacherSignal(state))
                return false;

            if (state.RefreshPending
                || string.Equals(state.Status, "refreshing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.Status, "transition", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            DateTime acceptedSnapshotUtc;
            string acceptedBoardSignature;
            string acceptedActionSummary;
            lock (AcceptedTeacherSnapshotSync)
            {
                acceptedSnapshotUtc = _lastAcceptedTeacherSnapshotUtc;
                acceptedBoardSignature = _lastAcceptedTeacherBoardSignature;
                acceptedActionSummary = _lastAcceptedTeacherActionSummary;
            }

            if (acceptedSnapshotUtc == DateTime.MinValue
                || string.IsNullOrWhiteSpace(acceptedBoardSignature)
                || string.IsNullOrWhiteSpace(currentBoardSignature))
            {
                return false;
            }

            if (string.Equals(acceptedBoardSignature.Trim(), currentBoardSignature.Trim(), StringComparison.Ordinal))
                return false;

            string currentActionSummary = BuildAcceptedTeacherActionSummary(state);
            bool exactTimestampMatch = acceptedSnapshotUtc == state.TimestampUtc;
            bool staleRepeat = !exactTimestampMatch
                && !string.IsNullOrWhiteSpace(currentActionSummary)
                && !string.IsNullOrWhiteSpace(acceptedActionSummary)
                && string.Equals(currentActionSummary, acceptedActionSummary, StringComparison.Ordinal);
            if (!exactTimestampMatch && !staleRepeat)
                return false;

            string completionReason;
            if (!IsAcceptedActionAlreadyCompleted(board, state, out completionReason))
                return false;

            LocalBoxOcrState latestRefreshSignal = null;
            for (int attempt = 1; attempt <= CompletedTeacherActionSyncReloadAttempts; attempt++)
            {
                try
                {
                    System.Threading.Thread.Sleep(CompletedTeacherActionSyncReloadDelayMilliseconds);
                }
                catch
                {
                    // ignore
                }

                LocalBoxOcrState candidate = LoadLocalBoxOcrState();
                if (candidate == null)
                    continue;

                if (!candidate.MatchesProfile(expectedProfileName))
                    continue;

                if (!HasMeaningfulTeacherStateAdvance(state, candidate))
                    continue;

                bool actionableAdvance = string.Equals(candidate.Status, "ok", StringComparison.OrdinalIgnoreCase)
                    && HasFreshTeacherSignal(candidate)
                    && !string.Equals(
                        BuildAcceptedTeacherActionSummary(candidate),
                        currentActionSummary,
                        StringComparison.Ordinal);
                bool refreshAdvance = candidate.RefreshPending
                    || string.Equals(candidate.Status, "refreshing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.Status, "transition", StringComparison.OrdinalIgnoreCase);
                if (refreshAdvance)
                    latestRefreshSignal = candidate;

                if (!actionableAdvance)
                    continue;

                refreshedState = candidate;
                if (addLog != null)
                {
                    addLog("[BOXOCR][STATE][SYNC_WAIT] 上一步已执行完，补等盒子续步成功"
                        + " | 原因=" + (string.IsNullOrWhiteSpace(completionReason) ? "(无)" : completionReason)
                        + " | 尝试=" + attempt.ToString(CultureInfo.InvariantCulture)
                        + " | 旧动作=" + (string.IsNullOrWhiteSpace(currentActionSummary) ? "(无)" : currentActionSummary)
                        + " | 新动作=" + (string.IsNullOrWhiteSpace(BuildAcceptedTeacherActionSummary(candidate)) ? "(无)" : BuildAcceptedTeacherActionSummary(candidate))
                        + " | 状态=" + DescribeBoxOcrStatusForLog(candidate.Status));
                }
                return true;
            }

            if (latestRefreshSignal != null)
            {
                refreshedState = latestRefreshSignal;
                if (addLog != null)
                {
                    addLog("[BOXOCR][STATE][SYNC_WAIT] 上一步已执行完，补等盒子续步只拿到刷新信号"
                        + " | 原因=" + (string.IsNullOrWhiteSpace(completionReason) ? "(无)" : completionReason)
                        + " | 旧动作=" + (string.IsNullOrWhiteSpace(currentActionSummary) ? "(无)" : currentActionSummary)
                        + " | 状态=" + DescribeBoxOcrStatusForLog(latestRefreshSignal.Status));
                }
                return true;
            }

            return false;
        }

        private static bool TryReloadTeacherStateOnMappingNotReady(
            Board board,
            LocalBoxOcrState state,
            string expectedProfileName,
            Action<string> addLog,
            out LocalBoxOcrState refreshedState)
        {
            refreshedState = null;
            if (board == null || state == null || !board.IsOwnTurn)
                return false;

            if (!state.IsFresh(FreshTeacherRefreshHoldSeconds) || !HasFreshTeacherSignal(state))
                return false;

            if (state.RefreshPending
                || string.Equals(state.Status, "refreshing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.Status, "transition", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string currentActionSummary = BuildAcceptedTeacherActionSummary(state);
            LocalBoxOcrState latestRefreshSignal = null;
            for (int attempt = 1; attempt <= CompletedTeacherActionSyncReloadAttempts; attempt++)
            {
                try
                {
                    System.Threading.Thread.Sleep(CompletedTeacherActionSyncReloadDelayMilliseconds);
                }
                catch
                {
                    // ignore
                }

                LocalBoxOcrState candidate = LoadLocalBoxOcrState();
                if (candidate == null)
                    continue;

                if (!candidate.MatchesProfile(expectedProfileName))
                    continue;

                if (!HasMeaningfulTeacherStateAdvance(state, candidate))
                    continue;

                bool refreshAdvance = candidate.RefreshPending
                    || string.Equals(candidate.Status, "refreshing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.Status, "transition", StringComparison.OrdinalIgnoreCase);
                if (refreshAdvance)
                    latestRefreshSignal = candidate;

                bool actionableAdvance = string.Equals(candidate.Status, "ok", StringComparison.OrdinalIgnoreCase)
                    && HasFreshTeacherSignal(candidate)
                    && !string.Equals(
                        BuildAcceptedTeacherActionSummary(candidate),
                        currentActionSummary,
                        StringComparison.Ordinal);
                if (!actionableAdvance)
                    continue;

                refreshedState = candidate;
                if (addLog != null)
                {
                    addLog("[BOXOCR][STATE][SYNC_WAIT] 当前动作未映射成功，补等盒子续步成功"
                        + " | 尝试=" + attempt.ToString(CultureInfo.InvariantCulture)
                        + " | 旧动作=" + (string.IsNullOrWhiteSpace(currentActionSummary) ? "(无)" : currentActionSummary)
                        + " | 新动作=" + (string.IsNullOrWhiteSpace(BuildAcceptedTeacherActionSummary(candidate)) ? "(无)" : BuildAcceptedTeacherActionSummary(candidate))
                        + " | 状态=" + DescribeBoxOcrStatusForLog(candidate.Status));
                }
                return true;
            }

            if (latestRefreshSignal != null)
            {
                refreshedState = latestRefreshSignal;
                if (addLog != null)
                {
                    addLog("[BOXOCR][STATE][SYNC_WAIT] 当前动作未映射成功，补等盒子续步只拿到刷新信号"
                        + " | 旧动作=" + (string.IsNullOrWhiteSpace(currentActionSummary) ? "(无)" : currentActionSummary)
                        + " | 状态=" + DescribeBoxOcrStatusForLog(latestRefreshSignal.Status));
                }
                return true;
            }

            return false;
        }

        private static bool HasMeaningfulTeacherStateAdvance(LocalBoxOcrState currentState, LocalBoxOcrState candidateState)
        {
            if (currentState == null || candidateState == null)
                return false;

            if (!string.Equals(currentState.Status ?? string.Empty, candidateState.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return true;

            if (currentState.RefreshPending != candidateState.RefreshPending)
                return true;

            if (currentState.BoxOptionId != candidateState.BoxOptionId)
                return true;

            if (!string.IsNullOrWhiteSpace(currentState.RecHash)
                || !string.IsNullOrWhiteSpace(candidateState.RecHash))
            {
                if (!string.Equals(currentState.RecHash ?? string.Empty, candidateState.RecHash ?? string.Empty, StringComparison.Ordinal))
                    return true;
            }

            string currentSummary = BuildAcceptedTeacherActionSummary(currentState);
            string candidateSummary = BuildAcceptedTeacherActionSummary(candidateState);
            if (!string.Equals(currentSummary ?? string.Empty, candidateSummary ?? string.Empty, StringComparison.Ordinal))
                return true;

            return false;
        }

        private static bool IsTeacherAttackActionAlreadyConsumed(Board board, LocalBoxOcrState state, out string completionReason)
        {
            completionReason = string.Empty;
            if (board == null || state == null || !string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
                return false;

            bool heroAttack = string.Equals(state.PreferredSourceKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "own_hero", StringComparison.OrdinalIgnoreCase);
            if (heroAttack)
            {
                if (!CanFriendlyHeroAttack(board))
                {
                    completionReason = "hero_attack_consumed";
                    return true;
                }

                return false;
            }

            return IsTeacherFriendlyAttackSourceAlreadyConsumed(
                board,
                state.PreferredSourceId,
                state.PreferredSourceSlot,
                out completionReason);
        }

        private static bool IsTeacherFriendlyAttackSourceAlreadyConsumed(
            Board board,
            Card.Cards sourceCardId,
            int sourceSlot,
            out string completionReason)
        {
            completionReason = string.Empty;
            IList<Card> boardCards = board != null ? board.MinionFriend : null;
            if (boardCards == null || boardCards.Count == 0)
            {
                if (CanTreatTeacherSourceAsCompleted(sourceCardId, sourceSlot))
                {
                    completionReason = "attack_source_missing";
                    return true;
                }

                return false;
            }

            Card slotMatch = null;
            bool slotMatchesExactSource = false;
            if (sourceSlot > 0 && sourceSlot <= boardCards.Count)
            {
                slotMatch = boardCards[sourceSlot - 1];
                slotMatchesExactSource = slotMatch != null
                    && slotMatch.Template != null
                    && !IsLocationCard(slotMatch)
                    && CardMatches(slotMatch, sourceCardId);
                if (slotMatchesExactSource)
                {
                    if (!IsFriendlyAttackSourceReady(slotMatch))
                    {
                        completionReason = "attack_source_cannot_attack";
                        return true;
                    }

                    return false;
                }
            }

            List<Card> sameCardSources = !IsTeacherUnknownCardId(sourceCardId)
                ? boardCards
                    .Where(card => card != null
                        && card.Template != null
                        && !IsLocationCard(card)
                        && card.Template.Id == sourceCardId)
                    .ToList()
                : new List<Card>();

            if (sourceSlot <= 0)
            {
                if (sameCardSources.Count == 0 && CanTreatTeacherSourceAsCompleted(sourceCardId, sourceSlot))
                {
                    completionReason = "attack_source_missing";
                    return true;
                }

                if (sameCardSources.Count == 1 && !IsFriendlyAttackSourceReady(sameCardSources[0]))
                {
                    completionReason = "attack_source_cannot_attack";
                    return true;
                }

                return false;
            }

            bool sourceStillExists = slotMatchesExactSource || sameCardSources.Count > 0;
            if (!sourceStillExists && CanTreatTeacherSourceAsCompleted(sourceCardId, sourceSlot))
            {
                completionReason = "attack_source_missing";
                return true;
            }

            return false;
        }

        private static bool IsTeacherLocationActionType(LocalBoxOcrState state)
        {
            return state != null
                && (string.Equals(state.PreferredActionType, "UseLocation", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state.PreferredSourceKind, "friendly_location", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsTeacherLocationActionAlreadyConsumed(Board board, LocalBoxOcrState state, out string completionReason)
        {
            completionReason = string.Empty;
            if (board == null || state == null || !IsTeacherLocationActionType(state))
                return false;

            IList<Card> boardCards = board.MinionFriend;
            if (boardCards == null || boardCards.Count == 0)
            {
                if (CanTreatTeacherSourceAsCompleted(state.PreferredSourceId, state.PreferredSourceSlot))
                {
                    completionReason = "location_missing";
                    return true;
                }

                return false;
            }

            Card slotMatch = null;
            bool slotMatchesExactSource = false;
            if (state.PreferredSourceSlot > 0 && state.PreferredSourceSlot <= boardCards.Count)
            {
                slotMatch = boardCards[state.PreferredSourceSlot - 1];
                slotMatchesExactSource = slotMatch != null
                    && IsLocationCard(slotMatch)
                    && CardMatches(slotMatch, state.PreferredSourceId);
                if (slotMatchesExactSource)
                {
                    if (IsLocationExhausted(slotMatch))
                    {
                        completionReason = "location_exhausted";
                        return true;
                    }

                    return false;
                }
            }

            List<Card> sameLocations = !IsTeacherUnknownCardId(state.PreferredSourceId)
                ? boardCards
                    .Where(card => card != null
                        && card.Template != null
                        && IsLocationCard(card)
                        && card.Template.Id == state.PreferredSourceId)
                    .ToList()
                : new List<Card>();

            if (state.PreferredSourceSlot <= 0)
            {
                if (sameLocations.Count == 0 && CanTreatTeacherSourceAsCompleted(state.PreferredSourceId, state.PreferredSourceSlot))
                {
                    completionReason = "location_missing";
                    return true;
                }

                if (sameLocations.Count == 1 && IsLocationExhausted(sameLocations[0]))
                {
                    completionReason = "location_exhausted";
                    return true;
                }

                return false;
            }

            bool sourceStillExists = slotMatchesExactSource || sameLocations.Count > 0;
            if (!sourceStillExists && CanTreatTeacherSourceAsCompleted(state.PreferredSourceId, state.PreferredSourceSlot))
            {
                completionReason = "location_missing";
                return true;
            }

            return false;
        }

        private static bool CanTreatTeacherSourceAsCompleted(Card.Cards sourceCardId, int sourceSlot)
        {
            return sourceSlot > 0 || !IsTeacherUnknownCardId(sourceCardId);
        }

        private static bool IsFriendlyAttackSourceReady(Card card)
        {
            if (card == null || card.Template == null || IsLocationCard(card))
                return false;

            try
            {
                return card.CanAttack && !card.IsFrozen && card.CurrentAtk > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLocationExhausted(Card card)
        {
            return GetTag(card, Card.GAME_TAG.EXHAUSTED) == 1;
        }

        private static string BuildTeacherLocationHoldDiagnostic(Board board, LocalBoxOcrState state)
        {
            if (board == null || state == null || !IsTeacherLocationActionType(state))
                return string.Empty;

            Card locationSource = FindPreferredFriendlyLocation(board, state.PreferredSourceId, state.PreferredSourceSlot);
            if (locationSource == null || locationSource.Template == null)
                return " | 地标源=(未定位)";

            int exhaustedTag = GetTag(locationSource, Card.GAME_TAG.EXHAUSTED);
            return exhaustedTag < 0
                ? " | 地标耗尽标签=未知"
                : " | 地标耗尽标签=" + exhaustedTag.ToString(CultureInfo.InvariantCulture);
        }

        private static void RememberAcceptedTeacherSnapshot(Board board, LocalBoxOcrState state, string currentBoardSignature)
        {
            if (state == null || state.TimestampUtc == DateTime.MinValue)
                return;

            string acceptedBoardSignature = string.IsNullOrWhiteSpace(currentBoardSignature)
                ? (state.BoardSignature ?? string.Empty).Trim()
                : currentBoardSignature.Trim();
            string acceptedActionSummary = BuildAcceptedTeacherActionSummary(state);
            Card.Cards acceptedPlaySourceId = default(Card.Cards);
            int acceptedPlaySourceSlot = 0;
            int acceptedPlayHandMatchCount = -1;
            if (board != null
                && string.Equals(state.PreferredActionType, "PlayCard", StringComparison.OrdinalIgnoreCase)
                && !IsTeacherUnknownCardId(state.PreferredSourceId))
            {
                acceptedPlaySourceId = state.PreferredSourceId;
                acceptedPlaySourceSlot = state.PreferredSourceSlot;
                acceptedPlayHandMatchCount = CountHandCardsById(board.Hand, state.PreferredSourceId);
            }

            lock (AcceptedTeacherSnapshotSync)
            {
                _lastAcceptedTeacherSnapshotUtc = state.TimestampUtc;
                _lastAcceptedTeacherBoardSignature = acceptedBoardSignature;
                _lastAcceptedTeacherActionSummary = acceptedActionSummary;
                _lastAcceptedTeacherPlaySourceId = acceptedPlaySourceId;
                _lastAcceptedTeacherPlaySourceSlot = acceptedPlaySourceSlot;
                _lastAcceptedTeacherPlayHandMatchCount = acceptedPlayHandMatchCount;
            }
        }

        private static int CountHandCardsById(IList<Card> hand, Card.Cards sourceCardId)
        {
            if (hand == null || hand.Count == 0 || IsTeacherUnknownCardId(sourceCardId))
                return 0;

            int count = 0;
            foreach (Card card in hand)
            {
                if (card == null || card.Template == null)
                    continue;

                if (card.Template.Id == sourceCardId)
                    count++;
            }

            return count;
        }

        private static string BuildAcceptedTeacherActionSummary(LocalBoxOcrState state)
        {
            if (state == null)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            string actionLabel = DescribeBoxOcrActionTypeForLog(state.PreferredActionType);
            if (string.IsNullOrWhiteSpace(actionLabel))
                actionLabel = IsCurrentTeacherEndTurnIntent(state) ? "结束回合" : "老师动作";
            sb.Append(actionLabel);

            if (state.PreferredStepIndex > 0 && state.ActionSteps.Count > 0)
            {
                sb.Append(" step=")
                    .Append(state.PreferredStepIndex.ToString(CultureInfo.InvariantCulture))
                    .Append("/")
                    .Append(state.ActionSteps.Count.ToString(CultureInfo.InvariantCulture));
            }

            if (state.PreferredSourceSlot > 0)
                sb.Append(" srcSlot=").Append(state.PreferredSourceSlot.ToString(CultureInfo.InvariantCulture));
            if (state.PreferredBoardSlot > 0)
                sb.Append(" boardSlot=").Append(state.PreferredBoardSlot.ToString(CultureInfo.InvariantCulture));
            if (state.PreferredChoiceIndex > 0)
                sb.Append(" choice=").Append(state.PreferredChoiceIndex.ToString(CultureInfo.InvariantCulture));
            if (!IsTeacherUnknownCardId(state.PreferredSourceId))
                sb.Append(" src=").Append(state.PreferredSourceId);
            if (!string.IsNullOrWhiteSpace(state.PreferredTargetKind))
                sb.Append(" tgtKind=").Append(state.PreferredTargetKind.Trim());
            if (state.PreferredTargetSlot > 0)
                sb.Append(" tgtSlot=").Append(state.PreferredTargetSlot.ToString(CultureInfo.InvariantCulture));
            if (!IsTeacherUnknownCardId(state.PreferredTargetId))
                sb.Append(" tgt=").Append(state.PreferredTargetId);

            return sb.ToString();
        }

        private static bool ShouldTreatEmptyTeacherStateAsPendingRefresh(LocalBoxOcrState state)
        {
            if (state == null || !string.Equals(state.Status, "empty", StringComparison.OrdinalIgnoreCase))
                return false;

            switch ((state.StatusReason ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "board_null":
                case "not_own_turn":
                case "mulligan_stage":
                case "discover_stage":
                case "overlay_only":
                case "no_useful_lines":
                case "no_play_signal":
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryApplyFreshTeacherRefreshHoldOnMiss(
            ProfileParameters p,
            Board board,
            LocalBoxOcrState state,
            Action<string> addLog,
            string reason,
            bool holdAttacks,
            string holdReasonCode,
            bool useStrongHold = false)
        {
            if (p == null || board == null || state == null)
                return false;

            if (!board.IsOwnTurn || !state.IsFresh(FreshTeacherRefreshHoldSeconds))
                return false;

            if (!HasFreshTeacherSignal(state))
                return false;

            string teacherActionSummary = BuildAcceptedTeacherActionSummary(state);
            int holdCount = BumpTeacherGuardReasonCounter(
                "hold_fresh",
                string.IsNullOrWhiteSpace(holdReasonCode) ? "refresh_wait" : holdReasonCode);
            string locationDiagnostic = BuildTeacherLocationHoldDiagnostic(board, state);
            ApplyHoldForTeacherRefresh(board, p, holdAttacks, state, true, useStrongHold);
            string holdStrength = useStrongHold ? "加强" : "标准";
            if (addLog != null)
            {
                addLog(
                    "[BOXOCR][STATE][HOLD] "
                    + (string.IsNullOrWhiteSpace(reason) ? "上一拍OCR仍新鲜，阻止默认逻辑等待刷新" : reason)
                    + " | 原因=" + (string.IsNullOrWhiteSpace(holdReasonCode) ? "refresh_wait" : holdReasonCode)
                    + " | 拦攻击=" + (holdAttacks ? "是" : "否")
                    + " | 力度=" + holdStrength
                    + " | 动作=" + (string.IsNullOrWhiteSpace(teacherActionSummary) ? "(无)" : teacherActionSummary)
                    + locationDiagnostic
                    + " | 命中次数=" + holdCount.ToString(CultureInfo.InvariantCulture)
                    + " | 已过秒数=" + GetLocalBoxOcrStateAgeSeconds(state));
            }

            return true;
        }

        private static bool HasFreshTeacherSignal(LocalBoxOcrState state)
        {
            if (state == null)
                return false;

            if (IsCurrentTeacherEndTurnIntent(state))
                return true;

            if (!string.IsNullOrWhiteSpace(state.PreferredActionType)
                || !string.IsNullOrWhiteSpace(state.PreferredSourceKind)
                || !IsTeacherUnknownCardId(state.PreferredSourceId)
                || state.PreferredSourceSlot > 0
                || state.PreferredChoiceIndex > 0
                || !string.IsNullOrWhiteSpace(state.PreferredTargetKind)
                || !IsTeacherUnknownCardId(state.PreferredTargetId)
                || state.PreferredTargetSlot > 0)
            {
                return true;
            }

            if (state.RecommendedCards.Count > 0 || state.RecommendedHeroPowers.Count > 0)
                return true;

            return state.TextHints.Count > 0;
        }

        private static bool TryApplyTeacherOnlyPlanBias(
            ProfileParameters p,
            Board board,
            LocalBoxOcrState state,
            bool forcedApplied,
            out string summary)
        {
            summary = string.Empty;
            if (p == null || board == null || state == null)
                return false;

            bool attackPlan = string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase);
            bool heroPowerPlan =
                IsTeacherHeroPowerActionType(state.PreferredActionType)
                || string.Equals(state.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase)
                || state.RecommendedHeroPowers.Count > 0;
            bool locationPlan =
                string.Equals(state.PreferredActionType, "UseLocation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "friendly_location", StringComparison.OrdinalIgnoreCase);
            bool currentStepIsHandPlay = IsTeacherCurrentStepHandPlayAction(state);

            if (attackPlan)
            {
                if (forcedApplied)
                {
                    summary = "仅保留盒子攻击动作";
                    return true;
                }

                ApplyHoldForTeacherRefresh(board, p, true, state);
                summary = "盒子要求攻击，但当前无法精确落地，已阻止默认逻辑等待刷新";
                return true;
            }

            int allowedHand = 0;
            int blockedHand = 0;
            bool heroPowerApplied = false;
            bool heroPowerBlocked = false;
            bool locationApplied = false;
            int blockedLocations = 0;

            Card preferredTarget = HasPreferredTarget(state) ? FindPreferredTarget(board, state) : null;
            Card preferredCard = FindPreferredHandCard(board, state);
            if (preferredCard == null)
                preferredCard = FindBestTextMatchedCard(board != null ? board.Hand : null, state, card => IsTeacherPreferredHandCard(state, card));
            Card strictComboCard = preferredCard;
            if (strictComboCard == null && board.Hand != null)
            {
                Card onlyPreferredCard = null;
                int strictPreferredCount = 0;
                for (int i = 0; i < board.Hand.Count; i++)
                {
                    Card candidate = board.Hand[i];
                    if (candidate == null || candidate.Template == null || !IsTeacherPreferredHandCard(state, candidate))
                        continue;

                    strictPreferredCount++;
                    if (strictPreferredCount > 1)
                    {
                        onlyPreferredCard = null;
                        break;
                    }

                    onlyPreferredCard = candidate;
                }

                if (strictPreferredCount == 1)
                    strictComboCard = onlyPreferredCard;
            }

            if (strictComboCard != null && strictComboCard.Template != null)
                ForcePreferredCardCombo(p, strictComboCard);
            Card preferredLocation = locationPlan
                ? FindPreferredFriendlyLocation(board, state.PreferredSourceId, state.PreferredSourceSlot)
                : null;

            if (board.Hand != null)
            {
                for (int handIndex = 0; handIndex < board.Hand.Count; handIndex++)
                {
                    Card card = board.Hand[handIndex];
                    if (card == null || card.Template == null)
                        continue;

                    bool preferred = currentStepIsHandPlay
                        && (IsTeacherCurrentStepHandCard(state, card, handIndex + 1)
                        || (preferredCard != null && preferredCard.Id == card.Id));
                    if (!preferred
                        && currentStepIsHandPlay
                        && preferredCard == null)
                    {
                        preferred = IsTeacherPreferredHandCard(state, card);
                    }
                    if (preferred)
                    {
                        int preferredChoiceIndex = ResolvePreferredChoiceIndex(state, card, preferredTarget);
                        if (preferredChoiceIndex > 0)
                            ApplyChoiceBias(p, card.Template.Id, preferredChoiceIndex, -9800);

                        ApplyCardBias(p, card, -9800, -9800, 9999, 9999);
                        allowedHand++;
                    }
                    else
                    {
                        BlockHandCardForTeacher(p, card);
                        blockedHand++;
                    }
                }
            }

            if (board.Ability != null && board.Ability.Template != null)
            {
                bool preferredHeroPower = heroPowerPlan
                    && CanUseHeroPowerNow(board, board.ManaAvailable)
                    && (state.RecommendedHeroPowers.Count == 0
                        || state.RecommendedHeroPowers.Contains(board.Ability.Template.Id)
                        || IsTeacherUnknownCardId(state.PreferredSourceId)
                        || state.PreferredSourceId == board.Ability.Template.Id);
                if (preferredHeroPower)
                {
                    ApplyHeroPowerCastAndOrderBias(p, board, -9800, 9999);
                    heroPowerApplied = true;
                }
                else
                {
                    // 盒子未推荐英雄技能时彻底封死，防止AI偷放
                    ApplyHeroPowerCastAndOrderBias(p, board, 9999, -9999);
                    heroPowerBlocked = true;
                }
            }

            if (board.MinionFriend != null)
            {
                foreach (Card friend in board.MinionFriend)
                {
                    if (!IsLocationCard(friend) || friend.Template == null)
                        continue;

                    bool isPreferredLocation = preferredLocation != null && friend.Id == preferredLocation.Id;
                    if (locationPlan && isPreferredLocation)
                    {
                        p.LocationsModifiers.AddOrUpdate(friend.Template.Id, new Modifier(-9800));
                        p.LocationsModifiers.AddOrUpdate(friend.Id, new Modifier(-9800));
                        p.PlayOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                        p.PlayOrderModifiers.AddOrUpdate(friend.Id, new Modifier(9999));
                        locationApplied = true;
                    }
                    else
                    {
                        p.LocationsModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                        p.LocationsModifiers.AddOrUpdate(friend.Id, new Modifier(9999));
                        blockedLocations++;
                    }
                }
            }

            bool strictAttackLock = forcedApplied
                || heroPowerApplied
                || locationApplied
                || (allowedHand > 0
                    && (state.PreferredSourceSlot > 0
                        || !IsTeacherUnknownCardId(state.PreferredSourceId)
                        || state.PreferredChoiceIndex > 0));

            if (strictAttackLock || allowedHand > 0 || heroPowerApplied || locationApplied)
                BlockAllAttacksForTeacher(board, p);
            else
                DelayAllAttacksForTeacher(board, p);

            if (forcedApplied)
            {
                summary = "仅保留盒子动作 | 已封锁非推荐动作 | 已禁止抢先攻击";
                return true;
            }

            if (allowedHand > 0 || heroPowerApplied || locationApplied)
            {
                summary = "仅保留盒子动作 | 手牌=" + allowedHand
                    + " | 技能=" + (heroPowerApplied ? "1" : "0")
                    + " | 地标=" + (locationApplied ? "1" : "0")
                    + " | 已封锁手牌=" + blockedHand
                    + " | 已封锁技能=" + (heroPowerBlocked ? "1" : "0")
                    + " | 已封锁地标=" + blockedLocations
                    + " | 已禁止抢先攻击";
                return true;
            }

            ApplyHoldForTeacherRefresh(board, p, true, state);
            summary = "盒子有推荐动作，但当前无法建立可执行映射，已阻止默认逻辑等待刷新";
            return true;
        }

        private static bool CanStrictlyEnforceTeacherPlan(Board board, LocalBoxOcrState state, bool forcedApplied)
        {
            if (ShouldStrictlyLockWholeTurnForTeacherPlan(state != null ? state.PreferredActionType : null, forcedApplied))
                return true;

            if (board == null || state == null)
                return false;

            if (forcedApplied)
                return false;

            bool attackPlan = string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase);
            if (attackPlan)
                return false;

            bool heroPowerPlan =
                IsTeacherHeroPowerActionType(state.PreferredActionType)
                || string.Equals(state.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase)
                || state.RecommendedHeroPowers.Count > 0;
            if (heroPowerPlan
                && board.Ability != null
                && board.Ability.Template != null
                && CanUseHeroPowerNow(board, board.ManaAvailable)
                && (state.RecommendedHeroPowers.Count == 0
                    || state.RecommendedHeroPowers.Contains(board.Ability.Template.Id)
                    || IsTeacherUnknownCardId(state.PreferredSourceId)
                    || state.PreferredSourceId == board.Ability.Template.Id))
            {
                return true;
            }

            bool locationPlan =
                string.Equals(state.PreferredActionType, "UseLocation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "friendly_location", StringComparison.OrdinalIgnoreCase);
            if (locationPlan && FindPreferredFriendlyLocation(board, state.PreferredSourceId, state.PreferredSourceSlot) != null)
                return true;

            Card preferredCard = FindPreferredHandCard(board, state);
            if (preferredCard == null)
                preferredCard = FindBestTextMatchedCard(board.Hand, state, card => IsTeacherPreferredHandCard(state, card));

            int manaAvailable = GetAvailableManaIncludingBurstManaCards(board);
            return preferredCard != null
                && preferredCard.Template != null
                && preferredCard.CurrentCost <= manaAvailable;
        }

        private static bool ShouldStrictlyLockWholeTurnForTeacherPlan(string preferredActionType, bool forcedApplied)
        {
            if (!forcedApplied)
                return false;

            return !string.Equals(preferredActionType, "Attack", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyHoldForTeacherRefresh(
            Board board,
            ProfileParameters p,
            bool holdAttacks,
            LocalBoxOcrState state,
            bool preserveTeacherSource = true,
            bool useStrongPendingHold = false,
            bool useHardPendingHold = false,
            bool forceOverride = false)
        {
            if (board == null || p == null)
                return;

            int handPenalty = useHardPendingHold
                ? PendingTeacherRefreshHardHandPenalty
                : (useStrongPendingHold ? PendingTeacherRefreshStrongHandPenalty : TeacherRefreshHoldHandPenalty);
            int heroPowerPenalty = useHardPendingHold
                ? PendingTeacherRefreshHardHeroPowerPenalty
                : (useStrongPendingHold ? PendingTeacherRefreshStrongHeroPowerPenalty : TeacherRefreshHoldHeroPowerPenalty);
            int locationPenalty = useHardPendingHold
                ? PendingTeacherRefreshHardLocationPenalty
                : (useStrongPendingHold ? PendingTeacherRefreshStrongLocationPenalty : TeacherRefreshHoldLocationPenalty);
            int attackPenalty = useHardPendingHold
                ? PendingTeacherRefreshHardAttackPenalty
                : (useStrongPendingHold ? PendingTeacherRefreshStrongAttackPenalty : TeacherRefreshHoldAttackPenalty);
            int attackBodyPenalty = useHardPendingHold
                ? PendingTeacherRefreshHardAttackBodyPenalty
                : (useStrongPendingHold ? PendingTeacherRefreshStrongAttackBodyPenalty : TeacherRefreshHoldAttackBodyPenalty);

            bool useAbsoluteNonAttackHold = forceOverride || useHardPendingHold || useStrongPendingHold;
            DelayNonAttackActionsForTeacher(
                board,
                p,
                state,
                preserveTeacherSource,
                handPenalty,
                heroPowerPenalty,
                locationPenalty,
                useAbsoluteNonAttackHold);
            if (holdAttacks)
            {
                bool useAbsoluteAttackHold = forceOverride || useHardPendingHold || useStrongPendingHold;
                // `mapping_not_ready` / WAIT_NEXT stale snapshots must suppress all fallback attacks.
                // Weak positive penalties (for example +500) can be ignored by stronger attack prefs.
                if (useAbsoluteAttackHold)
                    BlockAllAttacksForTeacher(board, p);
                else
                    DelayAllAttacksForTeacher(board, p, attackBodyPenalty, attackPenalty, forceOverride);
            }
        }

        private static void DelayNonAttackActionsForTeacher(
            Board board,
            ProfileParameters p,
            LocalBoxOcrState state,
            bool preserveTeacherSource,
            int handPenalty = TeacherRefreshHoldHandPenalty,
            int heroPowerPenalty = TeacherRefreshHoldHeroPowerPenalty,
            int locationPenalty = TeacherRefreshHoldLocationPenalty,
            bool forceOverride = false)
        {
            if (board == null || p == null)
                return;

            bool preserveTeacherNonAttackAction = preserveTeacherSource
                && state != null
                && !HasTeacherAttackIntent(state)
                && !IsCurrentTeacherEndTurnIntent(state);
            Card preferredTarget = preserveTeacherNonAttackAction && HasPreferredTarget(state)
                ? FindPreferredTarget(board, state)
                : null;

            int manaAvailable = GetAvailableManaIncludingBurstManaCards(board);
            Card preferredHandCard = preserveTeacherNonAttackAction ? FindPreferredHandCard(board, state) : null;
            if (preferredHandCard != null && preferredHandCard.CurrentCost > manaAvailable)
                preferredHandCard = null;

            bool preserveHeroPower = preserveTeacherNonAttackAction
                && board.Ability != null
                && board.Ability.Template != null
                && CanUseHeroPowerNow(board, board.ManaAvailable)
                && (IsTeacherHeroPowerActionType(state.PreferredActionType)
                    || string.Equals(state.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase)
                    || state.RecommendedHeroPowers.Contains(board.Ability.Template.Id)
                    || (state.RecommendedHeroPowers.Count == 0
                        && !IsTeacherUnknownCardId(state.PreferredSourceId)
                        && state.PreferredSourceId == board.Ability.Template.Id));

            Card preferredLocation = preserveTeacherNonAttackAction
                && (string.Equals(state.PreferredActionType, "UseLocation", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state.PreferredSourceKind, "friendly_location", StringComparison.OrdinalIgnoreCase))
                ? FindPreferredFriendlyLocation(board, state.PreferredSourceId, state.PreferredSourceSlot)
                : null;

            if (preserveTeacherNonAttackAction)
            {
                string holdPreferredSummary;
                TryApplyHoldPreferredTeacherSourceBias(
                    p,
                    board,
                    state,
                    preferredHandCard,
                    preferredTarget,
                    preserveHeroPower,
                    preferredLocation,
                    out holdPreferredSummary);
            }

            if (board.Hand != null)
            {
                foreach (Card card in board.Hand)
                {
                    if (card == null || card.Template == null)
                        continue;

                    if (ShouldPreserveTeacherHandCardDuringHold(state, preferredHandCard, card))
                        continue;

                    DelayCompetingHandCardForTeacher(p, card, handPenalty, forceOverride);
                }
            }

            if (!preserveHeroPower)
                SuppressHeroPowerForTeacher(p, board, heroPowerPenalty, forceOverride);

            DelayFriendlyLocationsForTeacher(board, p, preferredLocation, locationPenalty, forceOverride);
        }

        private static bool TryApplyHoldPreferredTeacherSourceBias(
            ProfileParameters p,
            Board board,
            LocalBoxOcrState state,
            Card preferredHandCard,
            Card preferredTarget,
            bool preserveHeroPower,
            Card preferredLocation,
            out string summary)
        {
            summary = string.Empty;
            if (p == null || board == null || state == null)
                return false;

            if (preferredHandCard != null && preferredHandCard.Template != null)
            {
                ForcePreferredCardCombo(p, preferredHandCard);
                int preferredChoiceIndex = ResolvePreferredChoiceIndex(state, preferredHandCard, preferredTarget);
                if (preferredChoiceIndex > 0)
                    ApplyChoiceBias(p, preferredHandCard.Template.Id, preferredChoiceIndex, -9800);

                if (preferredTarget != null
                    && preferredTarget.Template != null
                    && TryApplyExactCardCastModifier(
                        p,
                        preferredHandCard,
                        preferredTarget.Id,
                        preferredTarget.Template.Id,
                        -9800))
                {
                    p.PlayOrderModifiers.AddOrUpdate(preferredHandCard.Id, new Modifier(9999));
                    summary = BuildCardDisplayName(preferredHandCard)
                        + " -> "
                        + BuildCardDisplayName(preferredTarget)
                        + (preferredChoiceIndex > 0 ? " | 选项=" + preferredChoiceIndex : string.Empty);
                    return true;
                }

                ApplyCardInstanceBias(p, preferredHandCard, -9800, 9999);
                summary = BuildCardDisplayName(preferredHandCard)
                    + " | HOLD保留源动作"
                    + (preferredChoiceIndex > 0 ? " | 选项=" + preferredChoiceIndex : string.Empty);
                return true;
            }

            if (preserveHeroPower && board.Ability != null && board.Ability.Template != null)
            {
                if (preferredTarget != null
                    && preferredTarget.Template != null
                    && TryApplyExactHeroPowerModifier(
                        p,
                        board.Ability.Template.Id,
                        preferredTarget.Id,
                        preferredTarget.Template.Id,
                        -9800))
                {
                    ApplyHeroPowerCastAndOrderBias(p, board, -9800, 9999);
                    summary = "英雄技能(" + board.Ability.Template.Id + ") -> " + BuildCardDisplayName(preferredTarget);
                    return true;
                }

                ApplyHeroPowerCastAndOrderBias(p, board, -9800, 9999);
                summary = "英雄技能(" + board.Ability.Template.Id + ") | HOLD保留源动作";
                return true;
            }

            if (preferredLocation != null && preferredLocation.Template != null)
            {
                p.LocationsModifiers.AddOrUpdate(preferredLocation.Id, new Modifier(-9800));
                p.PlayOrderModifiers.AddOrUpdate(preferredLocation.Id, new Modifier(9999));
                summary = BuildCardDisplayName(preferredLocation) + " | HOLD保留源动作";
                return true;
            }

            return false;
        }

        private static void DelayFriendlyAttackOrderForTeacher(Board board, ProfileParameters p, int orderModifier)
        {
            if (board == null || p == null)
                return;

            try
            {
                if (board.MinionFriend != null)
                {
                    foreach (Card friend in board.MinionFriend)
                    {
                        if (friend == null || friend.Template == null || IsLocationCard(friend) || !friend.CanAttack)
                            continue;

                        p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(orderModifier));
                        p.AttackOrderModifiers.AddOrUpdate(friend.Id, new Modifier(orderModifier));
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (CanFriendlyHeroAttack(board))
                {
                    Card.Cards heroAttackCardId = GetHeroAttackCardId(board);
                    if (heroAttackCardId != default(Card.Cards))
                    {
                        p.WeaponsAttackModifiers.AddOrUpdate(heroAttackCardId, new Modifier(orderModifier));
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private static void DelayAllAttacksForTeacher(
            Board board,
            ProfileParameters p,
            int attackBodyPenalty = TeacherRefreshHoldAttackBodyPenalty,
            int attackPenalty = TeacherRefreshHoldAttackPenalty,
            bool forceOverride = false)
        {
            if (board == null || p == null)
                return;

            try
            {
                if (board.MinionFriend != null)
                {
                    foreach (Card friend in board.MinionFriend)
                    {
                        if (friend == null || friend.Template == null || IsLocationCard(friend) || !friend.CanAttack)
                            continue;

                        if (forceOverride)
                        {
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(attackBodyPenalty));
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Id, new Modifier(attackBodyPenalty));
                            p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(attackPenalty));
                            p.AttackOrderModifiers.AddOrUpdate(friend.Id, new Modifier(attackPenalty));
                        }
                        else
                        {
                            TryApplyWeakPositiveRule(
                                p.OnBoardFriendlyMinionsValuesModifiers,
                                friend.Template.Id,
                                friend.Id,
                                attackBodyPenalty);
                            TryApplyWeakPositiveRule(
                                p.AttackOrderModifiers,
                                friend.Template.Id,
                                friend.Id,
                                attackPenalty);
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (!CanFriendlyHeroAttack(board))
                    return;

                if (p.GlobalWeaponsAttackModifier == null
                    || (p.GlobalWeaponsAttackModifier.Value >= 0
                        && p.GlobalWeaponsAttackModifier.Value < attackPenalty))
                {
                    p.GlobalWeaponsAttackModifier = new Modifier(attackPenalty);
                }

                Card.Cards heroAttackCardId = GetHeroAttackCardId(board);
                if (heroAttackCardId != default(Card.Cards))
                {
                    int heroEntityId = board.HeroFriend != null ? board.HeroFriend.Id : 0;
                    if (forceOverride)
                    {
                        if (heroEntityId > 0)
                            p.WeaponsAttackModifiers.AddOrUpdate(heroEntityId, new Modifier(attackPenalty));
                        p.WeaponsAttackModifiers.AddOrUpdate(heroAttackCardId, new Modifier(attackPenalty));
                    }
                    else
                    {
                        TryApplyWeakPositiveRule(
                            p.WeaponsAttackModifiers,
                            heroAttackCardId,
                            heroEntityId,
                            attackPenalty);
                    }

                    if (board.WeaponFriend != null)
                    {
                        if (forceOverride)
                            p.WeaponsAttackModifiers.AddOrUpdate(board.WeaponFriend.Id, new Modifier(attackPenalty));
                        else
                        {
                            TryApplyWeakPositiveRule(
                                p.WeaponsAttackModifiers,
                                heroAttackCardId,
                                board.WeaponFriend.Id,
                                attackPenalty);
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            BlockAllExactAttacksForTeacher(board, p);
            ApplyTeacherRefreshAggroHold(board, p);
        }

        private static void BlockAllAttacksForTeacher(Board board, ProfileParameters p)
        {
            if (board == null || p == null)
                return;

            try
            {
                if (board.MinionFriend != null)
                {
                    foreach (Card friend in board.MinionFriend)
                    {
                        if (friend == null || friend.Template == null || IsLocationCard(friend))
                            continue;

                        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(DelayAttackValue));
                        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Id, new Modifier(DelayAttackValue));
                        p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(DelayAttackValue));
                        p.AttackOrderModifiers.AddOrUpdate(friend.Id, new Modifier(DelayAttackValue));
                    }
                }
            }
            catch
            {
                // ignore
            }

            BlockAllExactAttacksForTeacher(board, p);
            BlockHeroAttack(board, p);
            ApplyTeacherRefreshAggroHold(board, p);
        }

        private static void BlockAllExactAttacksForTeacher(Board board, ProfileParameters p)
        {
            if (board == null || p == null)
                return;

            IList<Card> enemyTargets = CollectEnemyAttackTargets(board);
            if (enemyTargets.Count <= 0)
                return;

            try
            {
                if (board.MinionFriend != null)
                {
                    foreach (Card friend in board.MinionFriend)
                    {
                        if (friend == null || friend.Template == null || IsLocationCard(friend) || !friend.CanAttack)
                            continue;

                        ApplyExactAttackBlocksForTeacherSource(p, friend, enemyTargets, null);
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (CanFriendlyHeroAttack(board))
                    ApplyExactHeroAttackBlocksForTeacherSource(board, p, enemyTargets, null);
            }
            catch
            {
                // ignore
            }
        }

        private static void BlockCompetingTeacherAttacks(
            Board board,
            ProfileParameters p,
            Card preferredSource,
            Card preferredTarget,
            bool heroAttack)
        {
            if (board == null || p == null || preferredTarget == null)
                return;

            IList<Card> enemyTargets = CollectEnemyAttackTargets(board);
            if (enemyTargets.Count <= 0)
                return;

            try
            {
                if (board.MinionFriend != null)
                {
                    foreach (Card friend in board.MinionFriend)
                    {
                        if (friend == null || friend.Template == null || IsLocationCard(friend) || !friend.CanAttack)
                            continue;

                        Card allowedTarget = (!heroAttack && preferredSource != null && friend.Id == preferredSource.Id)
                            ? preferredTarget
                            : null;
                        ApplyExactAttackBlocksForTeacherSource(p, friend, enemyTargets, allowedTarget);
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (!CanFriendlyHeroAttack(board))
                    return;

                Card heroAllowedTarget = heroAttack ? preferredTarget : null;
                ApplyExactHeroAttackBlocksForTeacherSource(board, p, enemyTargets, heroAllowedTarget);
            }
            catch
            {
                // ignore
            }
        }

        private static IList<Card> CollectEnemyAttackTargets(Board board)
        {
            List<Card> targets = new List<Card>();
            if (board == null)
                return targets;

            try
            {
                if (board.MinionEnemy != null)
                {
                    foreach (Card enemy in board.MinionEnemy)
                    {
                        if (enemy == null || enemy.Template == null)
                            continue;

                        targets.Add(enemy);
                    }
                }

                if (board.HeroEnemy != null)
                    targets.Add(board.HeroEnemy);
            }
            catch
            {
                // ignore
            }

            return targets;
        }

        private static void ApplyExactAttackBlocksForTeacherSource(
            ProfileParameters p,
            Card source,
            IList<Card> enemyTargets,
            Card allowedTarget)
        {
            if (p == null || source == null || source.Template == null || enemyTargets == null)
                return;

            foreach (Card target in enemyTargets)
            {
                if (target == null)
                    continue;

                if (allowedTarget != null && target.Id == allowedTarget.Id)
                    continue;

                Card.Cards targetCardId = target.Template != null ? target.Template.Id : default(Card.Cards);
                TryApplyExactAttackModifier(
                    p,
                    source.Id,
                    source.Template.Id,
                    target.Id,
                    targetCardId,
                    BlockExactAttackValue);
            }
        }

        private static void ApplyExactHeroAttackBlocksForTeacherSource(
            Board board,
            ProfileParameters p,
            IList<Card> enemyTargets,
            Card allowedTarget)
        {
            if (board == null || p == null || enemyTargets == null)
                return;

            int heroEntityId = board.HeroFriend != null ? board.HeroFriend.Id : 0;
            Card.Cards heroAttackCardId = GetHeroAttackCardId(board);
            if (heroEntityId <= 0 && heroAttackCardId == default(Card.Cards))
                return;

            foreach (Card target in enemyTargets)
            {
                if (target == null)
                    continue;

                if (allowedTarget != null && target.Id == allowedTarget.Id)
                    continue;

                Card.Cards targetCardId = target.Template != null ? target.Template.Id : default(Card.Cards);
                TryApplyExactWeaponAttackModifier(
                    p,
                    heroEntityId,
                    heroAttackCardId,
                    target.Id,
                    targetCardId,
                    BlockExactAttackValue);
            }
        }

        private static void ApplyTeacherRefreshAggroHold(Board board, ProfileParameters p)
        {
            if (board == null || p == null)
                return;

            try
            {
                if (board.MinionEnemy != null)
                {
                    foreach (Card enemy in board.MinionEnemy)
                    {
                        if (enemy == null || enemy.Template == null)
                            continue;

                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(TeacherRefreshHoldAggroValue));
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (p.GlobalAggroModifier == null || p.GlobalAggroModifier.Value > TeacherRefreshHoldAggroValue)
                    p.GlobalAggroModifier = new Modifier(TeacherRefreshHoldAggroValue);
            }
            catch
            {
                // ignore
            }
        }

        private static void SuppressHeroPowerForTeacher(
            ProfileParameters p,
            Board board,
            int heroPowerPenalty = TeacherRefreshHoldHeroPowerPenalty,
            bool forceOverride = false)
        {
            if (p == null || board == null || board.Ability == null || board.Ability.Template == null)
                return;

            if (forceOverride)
            {
                int forcedPenalty = Math.Max(9999, heroPowerPenalty);
                ApplyHeroPowerCastAndOrderBias(
                    p,
                    board,
                    forcedPenalty,
                    -Math.Max(120, forcedPenalty / 2));
                return;
            }

            if (!TryApplyBoxAuditHeroPowerPenalty(p, board.Ability.Template.Id, heroPowerPenalty)
                && CanApplyWeakPositiveBias(p.CastHeroPowerModifier, board.Ability.Template.Id, (int)board.Ability.Template.Id))
            {
                ApplyHeroPowerCastAndOrderBias(
                    p,
                    board,
                    heroPowerPenalty,
                    -Math.Max(120, heroPowerPenalty / 2));
            }
        }

        private static void BlockHandCardForTeacher(ProfileParameters p, Card card)
        {
            if (p == null || card == null || card.Template == null)
                return;

            // STRICT模式：彻底封锁非盒子推荐卡牌（包括0费法术），
            // 防止resimulate阶段因抑制不足而偷跑。
            ApplyCardBias(p, card, 9999, 9999, -9999, -9999);
        }

        private static void DelayCompetingHandCardForTeacher(
            ProfileParameters p,
            Card card,
            int handPenalty = TeacherRefreshHoldHandPenalty,
            bool forceOverride = false)
        {
            if (p == null || card == null || card.Template == null)
                return;

            if (forceOverride)
            {
                BlockHandCardForTeacher(p, card);
                return;
            }

            if (TryApplyGenericCardInstanceCastBias(p, card, handPenalty))
            {
                p.PlayOrderModifiers.AddOrUpdate(
                    card.Id,
                    new Modifier(-Math.Max(120, handPenalty / 2)));
            }
        }

        private static bool ShouldPreserveTeacherHandCardDuringHold(LocalBoxOcrState state, Card preferredHandCard, Card card)
        {
            if (card == null || card.Template == null)
                return false;

            if (preferredHandCard != null)
                return preferredHandCard.Id == card.Id;

            return false;
        }

        private static void DelayFriendlyLocationsForTeacher(
            Board board,
            ProfileParameters p,
            Card preferredLocation,
            int locationPenalty = TeacherRefreshHoldLocationPenalty,
            bool forceOverride = false)
        {
            if (board == null || p == null || board.MinionFriend == null)
                return;

            try
            {
                foreach (Card friend in board.MinionFriend)
                {
                    if (!IsLocationCard(friend) || friend.Template == null)
                        continue;

                    if (preferredLocation != null && preferredLocation.Id == friend.Id)
                        continue;

                    if (forceOverride)
                    {
                        p.LocationsModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                        p.LocationsModifiers.AddOrUpdate(friend.Id, new Modifier(9999));
                    }
                    else
                    {
                        TryApplyWeakPositiveRule(
                            p.LocationsModifiers,
                            friend.Template.Id,
                            friend.Id,
                            locationPenalty);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private static bool TryApplyTextAnchoredRecommendedCardRelax(
            ProfileParameters p,
            Board board,
            LocalBoxOcrState state,
            BoxAuditGuardState boxAuditGuard,
            out Card anchoredCard,
            out string summary)
        {
            anchoredCard = null;
            summary = string.Empty;

            if (p == null || board == null || state == null || board.Hand == null || board.Hand.Count == 0)
                return false;

            int matchScore;
            string matchSource;
            anchoredCard = FindTextAnchoredRecommendedHandCard(board, state, out matchScore, out matchSource);
            if (anchoredCard == null || anchoredCard.Template == null)
                return false;

            if (IsCardPenalized(boxAuditGuard, anchoredCard.Template.Id))
                return false;

            int manaAvailable = GetAvailableManaIncludingBurstManaCards(board);
            if (anchoredCard.CurrentCost > manaAvailable)
                return false;

            ForcePreferredCardCombo(p, anchoredCard);

            int preferredChoiceIndex = ResolvePreferredChoiceIndex(state, anchoredCard, null);
            if (preferredChoiceIndex > 0)
                ApplyChoiceBias(p, anchoredCard.Template.Id, preferredChoiceIndex, -2400);

            ApplyCardBias(p, anchoredCard, -2400, -2400, 5200, 5200);
            summary = BuildCardDisplayName(anchoredCard)
                + " | 锚点="
                + (string.IsNullOrWhiteSpace(matchSource) ? "文本" : matchSource)
                + " | 分数="
                + matchScore
                + (preferredChoiceIndex > 0 ? " | 选项=" + preferredChoiceIndex : string.Empty);
            return true;
        }

        private static bool TryApplySignatureMismatchSafeBias(
            ProfileParameters p,
            Board board,
            LocalBoxOcrState state,
            BoxAuditGuardState boxAuditGuard,
            out string summary)
        {
            summary = string.Empty;
            if (p == null || board == null || state == null)
                return false;

            if (!state.IsFresh(20))
                return false;

            if (string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
                return false;

            bool locationPlan =
                string.Equals(state.PreferredActionType, "UseLocation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "friendly_location", StringComparison.OrdinalIgnoreCase);
            if (locationPlan)
            {
                Card preferredLocation = FindPreferredFriendlyLocation(board, state.PreferredSourceId, state.PreferredSourceSlot);
                if (preferredLocation == null || preferredLocation.Template == null)
                    return false;

                if (HasPreferredTarget(state))
                    return false;

                p.LocationsModifiers.AddOrUpdate(preferredLocation.Template.Id, new Modifier(SignatureMismatchRelaxSourceValue));
                p.LocationsModifiers.AddOrUpdate(preferredLocation.Id, new Modifier(SignatureMismatchRelaxSourceValue));
                p.PlayOrderModifiers.AddOrUpdate(preferredLocation.Template.Id, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(preferredLocation.Id, new Modifier(9999));
                summary = "地标=" + BuildCardDisplayName(preferredLocation);
                return true;
            }

            bool heroPowerPlan =
                IsTeacherHeroPowerActionType(state.PreferredActionType)
                || string.Equals(state.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase)
                || state.RecommendedHeroPowers.Count > 0;
            if (heroPowerPlan)
            {
                if (board.Ability == null || board.Ability.Template == null)
                    return false;

                if (!CanUseHeroPowerNow(board, board.ManaAvailable))
                    return false;

                bool heroPowerMatches = state.RecommendedHeroPowers.Count == 0
                    || state.RecommendedHeroPowers.Contains(board.Ability.Template.Id)
                    || IsTeacherUnknownCardId(state.PreferredSourceId)
                    || state.PreferredSourceId == board.Ability.Template.Id;
                if (!heroPowerMatches)
                    return false;

                Card heroPowerTarget = HasPreferredTarget(state) ? FindPreferredTarget(board, state) : null;
                if (HasPreferredTarget(state) && (heroPowerTarget == null || heroPowerTarget.Template == null))
                    return false;

                if (heroPowerTarget != null
                    && TryApplyExactHeroPowerModifier(p, board.Ability.Template.Id, heroPowerTarget.Id, heroPowerTarget.Template.Id, SignatureMismatchRelaxSourceValue))
                {
                    ApplyHeroPowerCastAndOrderBias(p, board, SignatureMismatchRelaxSourceValue, 9999);
                    summary = "英雄技能=" + board.Ability.Template.Id + " -> " + BuildCardDisplayName(heroPowerTarget);
                    return true;
                }

                ApplyHeroPowerCastAndOrderBias(p, board, SignatureMismatchRelaxSourceValue, 9999);
                summary = "英雄技能=" + board.Ability.Template.Id;
                return true;
            }

            Card preferredTarget = HasPreferredTarget(state) ? FindPreferredTarget(board, state) : null;
            if (HasPreferredTarget(state) && (preferredTarget == null || preferredTarget.Template == null))
                return false;

            Card preferredCard = FindPreferredHandCard(board, state);
            if (preferredCard == null)
            {
                Card anchoredCard;
                string anchoredSummary;
                if (TryApplyTextAnchoredRecommendedCardRelax(p, board, state, boxAuditGuard, out anchoredCard, out anchoredSummary))
                {
                    summary = anchoredSummary;
                    return true;
                }

                return false;
            }

            int manaAvailable = GetAvailableManaIncludingBurstManaCards(board);
            if (preferredCard.Template == null || preferredCard.CurrentCost > manaAvailable)
                return false;

            ForcePreferredCardCombo(p, preferredCard);
            int preferredChoiceIndex = ResolvePreferredChoiceIndex(state, preferredCard, preferredTarget);
            if (preferredChoiceIndex > 0)
                ApplyChoiceBias(p, preferredCard.Template.Id, preferredChoiceIndex, SignatureMismatchRelaxSourceValue);

            if (preferredTarget != null
                && TryApplyExactCardCastModifier(
                    p,
                    preferredCard,
                    preferredTarget.Id,
                    preferredTarget.Template.Id,
                    SignatureMismatchRelaxSourceValue))
            {
                p.PlayOrderModifiers.AddOrUpdate(preferredCard.Template.Id, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(preferredCard.Id, new Modifier(9999));
                summary = BuildCardDisplayName(preferredCard)
                    + " -> "
                    + BuildCardDisplayName(preferredTarget)
                    + (preferredChoiceIndex > 0 ? " | 选项=" + preferredChoiceIndex : string.Empty);
                return true;
            }

            ApplyCardBias(
                p,
                preferredCard,
                SignatureMismatchRelaxSourceValue,
                SignatureMismatchRelaxSourceValue,
                9999,
                9999);
            summary = BuildCardDisplayName(preferredCard)
                + " | 签名失配安全降级"
                + (preferredChoiceIndex > 0 ? " | 选项=" + preferredChoiceIndex : string.Empty);
            return true;
        }

        private static Card FindTextAnchoredRecommendedHandCard(Board board, LocalBoxOcrState state, out int bestScore, out string matchSource)
        {
            bestScore = 0;
            matchSource = string.Empty;

            if (board == null || board.Hand == null || state == null || state.TextHints.Count == 0)
                return null;

            if (state.PreferredSourceSlot > 0 && state.PreferredSourceSlot <= board.Hand.Count)
            {
                Card bySlot = board.Hand[state.PreferredSourceSlot - 1];
                if (IsTeacherPreferredHandCard(state, bySlot))
                {
                    int slotScore = ScoreCardAgainstPlayTextHints(bySlot, state);
                    if (slotScore > 0)
                    {
                        bestScore = slotScore;
                        matchSource = "槽位+文本";
                        return bySlot;
                    }
                }
            }

            if (!IsTeacherUnknownCardId(state.PreferredSourceId))
            {
                foreach (Card card in board.Hand)
                {
                    if (!IsTeacherPreferredHandCard(state, card))
                        continue;

                    if (card.Template.Id != state.PreferredSourceId)
                        continue;

                    int idScore = ScoreCardAgainstPlayTextHints(card, state);
                    if (idScore > 0)
                    {
                        bestScore = idScore;
                        matchSource = "来源ID+文本";
                        return card;
                    }
                }
            }

            Card matched = FindBestTextMatchedCard(
                board.Hand,
                state,
                card => IsTeacherPreferredHandCard(state, card));
            if (matched == null)
                return null;

            bestScore = ScoreCardAgainstPlayTextHints(matched, state);
            if (bestScore <= 0)
                return null;

            matchSource = "文本";
            return matched;
        }

        private static bool IsTeacherPreferredHandCard(LocalBoxOcrState state, Card card)
        {
            if (state == null || card == null || card.Template == null)
                return false;

            if (state.RecommendedCards.Contains(card.Template.Id))
                return true;

            return !IsTeacherUnknownCardId(state.PreferredSourceId)
                && card.Template.Id == state.PreferredSourceId;
        }

        private static bool IsTeacherCurrentStepHandPlayAction(LocalBoxOcrState state)
        {
            return state != null
                && string.Equals(state.PreferredActionType, "PlayCard", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(state.PreferredSourceKind, "friendly_location", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(state.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTeacherCurrentStepHandCard(LocalBoxOcrState state, Card card, int handSlot)
        {
            if (state == null || card == null || card.Template == null)
                return false;

            if (!IsTeacherCurrentStepHandPlayAction(state))
                return false;

            if (state.PreferredSourceSlot > 0)
            {
                if (handSlot != state.PreferredSourceSlot)
                    return false;

                return IsTeacherUnknownCardId(state.PreferredSourceId)
                    || card.Template.Id == state.PreferredSourceId;
            }

            if (!IsTeacherUnknownCardId(state.PreferredSourceId))
                return card.Template.Id == state.PreferredSourceId;

            return state.RecommendedCards.Contains(card.Template.Id);
        }

        private static bool CanReuseFreshTurnTransitionPlayState(Board board, LocalBoxOcrState state)
        {
            if (board == null || state == null)
                return false;

            if (!board.IsOwnTurn)
                return false;

            if (!state.IsFresh(20))
                return false;

            if (!string.Equals(state.Stage, "play", StringComparison.OrdinalIgnoreCase))
                return false;

            return HasBoardBoundActionablePlayStateAnchorOnCurrentBoard(board, state);
        }

        private static bool HasBoardBoundActionablePlayStateAnchorOnCurrentBoard(Board board, LocalBoxOcrState state)
        {
            if (board == null || state == null)
                return false;

            if (IsCurrentTeacherEndTurnIntent(state))
                return false;

            bool attackPlan = string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase);
            if (attackPlan)
            {
                if (!HasExecutableTeacherAttackRecommendation(board, state))
                    return false;

                if (HasPreferredTarget(state) && FindPreferredTarget(board, state) == null)
                    return false;

                return true;
            }

            if (FindPreferredFriendlyLocation(board, state.PreferredSourceId, state.PreferredSourceSlot) != null)
            {
                if (HasPreferredTarget(state) && FindPreferredTarget(board, state) == null)
                    return false;

                return true;
            }

            if (IsTeacherHeroPowerActionType(state.PreferredActionType)
                || string.Equals(state.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (board.Ability == null || board.Ability.Template == null)
                        return false;

                    if (!IsTeacherUnknownCardId(state.PreferredSourceId)
                        && board.Ability.Template.Id != state.PreferredSourceId)
                    {
                        return false;
                    }

                    if (HasPreferredTarget(state) && FindPreferredTarget(board, state) == null)
                        return false;

                    return true;
                }
                catch
                {
                    return false;
                }
            }

            if (string.Equals(state.PreferredSourceKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "own_hero", StringComparison.OrdinalIgnoreCase))
            {
                if (!CanFriendlyHeroAttack(board))
                    return false;

                if (HasPreferredTarget(state) && FindPreferredTarget(board, state) == null)
                    return false;

                return true;
            }

            if (FindPreferredFriendlyMinion(board, state.PreferredSourceId, state.PreferredSourceSlot) != null)
            {
                if (HasPreferredTarget(state) && FindPreferredTarget(board, state) == null)
                    return false;

                return true;
            }

            try
            {
                if (board.Ability != null
                    && board.Ability.Template != null
                    && board.Ability.Template.Id == state.PreferredSourceId)
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if ((string.Equals(state.PreferredSourceKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(state.PreferredSourceKind, "own_hero", StringComparison.OrdinalIgnoreCase))
                    && board.HeroFriend != null)
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool HasTextualPlayStateAnchorOnCurrentBoard(Board board, LocalBoxOcrState state)
        {
            if (board == null || state == null || state.TextHints.Count == 0)
                return false;

            if (FindBestTextMatchedCard(board.Hand, state, null) != null)
                return true;

            if (FindBestTextMatchedCard(board.MinionFriend, state, null) != null)
                return true;

            if (FindBestTextMatchedCard(board.MinionEnemy, state, null) != null)
                return true;

            if (ScoreCardAgainstPlayTextHints(board.Ability, state) > 0)
                return true;

            if (ScoreCardAgainstPlayTextHints(board.HeroFriend, state) > 0)
                return true;

            if (ScoreCardAgainstPlayTextHints(board.HeroEnemy, state) > 0)
                return true;

            return false;
        }

        private static Card FindBestTextMatchedCard(IEnumerable<Card> cards, LocalBoxOcrState state, Predicate<Card> filter)
        {
            if (cards == null || state == null || state.TextHints.Count == 0)
                return null;

            Card bestCard = null;
            int bestScore = 0;

            foreach (Card card in cards)
            {
                if (card == null || card.Template == null)
                    continue;

                if (filter != null && !filter(card))
                    continue;

                int score = ScoreCardAgainstPlayTextHints(card, state);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCard = card;
                }
            }

            return bestCard;
        }

        private static int ScoreCardAgainstPlayTextHints(Card card, LocalBoxOcrState state)
        {
            if (card == null || card.Template == null || state == null || state.TextHints.Count == 0)
                return 0;

            List<string> aliases = BuildPlayCardAliases(card);
            if (aliases.Count == 0)
                return 0;

            int bestScore = 0;
            foreach (string rawHint in state.TextHints)
            {
                string normalizedHint = NormalizePlayTextHint(rawHint);
                if (string.IsNullOrWhiteSpace(normalizedHint))
                    continue;

                foreach (string alias in aliases)
                {
                    int score = ScorePlayAliasMatch(normalizedHint, alias);
                    if (score > bestScore)
                        bestScore = score;
                }
            }

            return bestScore;
        }

        private static List<string> BuildPlayCardAliases(Card card)
        {
            List<string> aliases = new List<string>();
            if (card == null || card.Template == null)
                return aliases;

            AddPlayCardAlias(aliases, card.Template.Id.ToString());
            AddPlayCardAlias(aliases, card.Template.NameCN);
            AddPlayCardAlias(aliases, card.Template.Name);
            return aliases;
        }

        private static void AddPlayCardAlias(ICollection<string> aliases, string raw)
        {
            if (aliases == null)
                return;

            string normalized = NormalizePlayTextHint(raw);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            if (!aliases.Contains(normalized))
                aliases.Add(normalized);
        }

        private static Card FindPreferredFriendlyMinion(Board board, Card.Cards sourceId, int sourceSlot)
        {
            if (board == null || board.MinionFriend == null || board.MinionFriend.Count == 0)
                return null;

            return FindCardByIdentity(board.MinionFriend, sourceId, sourceSlot);
        }

        private static int ResolvePreferredChoiceIndex(LocalBoxOcrState state, Card preferredCard, Card preferredTarget)
        {
            if (preferredCard == null || preferredCard.Template == null)
                return 0;

            return ResolvePreferredChoiceIndex(state, preferredCard.Template.Id, preferredTarget != null);
        }

        private static string DescribeLocalChoiceOption(LocalChoiceRuntimeOption option)
        {
            if (option == null)
                return "null";

            return "idx=" + option.ChoiceIndex.ToString(CultureInfo.InvariantCulture)
                + "/entity=" + option.EntityId.ToString(CultureInfo.InvariantCulture)
                + "/card=" + DescribeChoiceCardId(option.CardId);
        }

        private static string DescribeLocalChoicePreference(int preferredChoiceIndex, IEnumerable<Card.Cards> preferredCardIds)
        {
            List<Card.Cards> ids = preferredCardIds == null
                ? new List<Card.Cards>()
                : preferredCardIds.Where(id => id != default(Card.Cards)).Distinct().ToList();
            string preferredCardSummary = ids.Count > 0
                ? string.Join(",", ids.Select(DescribeChoiceCardId))
                : "none";
            return "choice_index=" + preferredChoiceIndex.ToString(CultureInfo.InvariantCulture)
                + " preferred_cards=" + preferredCardSummary;
        }

        private static string DescribeLocalChoiceOptionsSnapshot(IList<LocalChoiceRuntimeOption> options)
        {
            if (options == null || options.Count == 0)
                return "options=0 | 未暴露可反射 choice 集合";

            return "options=" + options.Count.ToString(CultureInfo.InvariantCulture)
                + " | " + string.Join(" | ", options.Where(option => option != null).Select(DescribeLocalChoiceOption));
        }

        private static string DescribeChoiceCardId(Card.Cards cardId)
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
                // ignore
            }

            return cardId.ToString();
        }

        private static bool TryGetCurrentChoiceOptionsLocal(Board board, out List<LocalChoiceRuntimeOption> options)
        {
            options = new List<LocalChoiceRuntimeOption>();
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
                object raw = TryGetPropertyObjectLocal(board, candidatePropertyNames[i]);
                if (raw == null)
                    continue;

                if (TryConvertChoiceCollectionLocal(raw, options) && options.Count > 0)
                    return true;

                options.Clear();
            }

            return false;
        }

        private static bool TryConvertChoiceCollectionLocal(object raw, List<LocalChoiceRuntimeOption> output)
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
                LocalChoiceRuntimeOption option;
                if (!TryResolveChoiceOptionLocal(item, choiceIndex, out option))
                    continue;

                output.Add(option);
                addedAny = true;
            }

            return addedAny;
        }

        private static bool TryResolveChoiceOptionLocal(object item, int choiceIndex, out LocalChoiceRuntimeOption option)
        {
            option = null;
            if (item == null)
                return false;

            option = new LocalChoiceRuntimeOption();
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

            PopulateChoiceOptionFromObjectLocal(item, option);

            object cardObj = TryGetPropertyObjectLocal(item, "Card");
            if (cardObj != null)
                PopulateChoiceOptionFromObjectLocal(cardObj, option);

            return option.EntityId > 0 || option.CardId != default(Card.Cards);
        }

        private static void PopulateChoiceOptionFromObjectLocal(object item, LocalChoiceRuntimeOption option)
        {
            if (item == null || option == null)
                return;

            object templateObj = TryGetPropertyObjectLocal(item, "Template");
            if (templateObj != null)
            {
                object templateIdObj = TryGetPropertyObjectLocal(templateObj, "Id");
                if (templateIdObj is Card.Cards)
                    option.CardId = (Card.Cards)templateIdObj;
            }

            if (option.EntityId <= 0)
            {
                object entityIdObj = TryGetPropertyObjectLocal(item, "EntityId");
                option.EntityId = TryConvertToIntLocal(entityIdObj, option.EntityId);
            }

            if (option.EntityId <= 0)
            {
                object entityIdObj = TryGetPropertyObjectLocal(item, "EntityID");
                option.EntityId = TryConvertToIntLocal(entityIdObj, option.EntityId);
            }

            object itemIdObj = TryGetPropertyObjectLocal(item, "Id");
            if (itemIdObj is Card.Cards)
            {
                if (option.CardId == default(Card.Cards))
                    option.CardId = (Card.Cards)itemIdObj;
            }
            else if (option.EntityId <= 0)
            {
                option.EntityId = TryConvertToIntLocal(itemIdObj, option.EntityId);
            }

            if (option.CardId == default(Card.Cards))
            {
                object cardIdObj = TryGetPropertyObjectLocal(item, "CardId");
                if (cardIdObj is Card.Cards)
                    option.CardId = (Card.Cards)cardIdObj;
            }
        }

        private static int TryConvertToIntLocal(object value, int fallback)
        {
            int parsed;
            return TryConvertToIntLocal(value, out parsed) ? parsed : fallback;
        }

        private static bool TryConvertToIntLocal(object value, out int parsed)
        {
            parsed = 0;
            if (value == null)
                return false;

            try
            {
                if (value is int)
                {
                    parsed = (int)value;
                    return true;
                }

                if (value is long)
                {
                    long longValue = (long)value;
                    if (longValue < int.MinValue || longValue > int.MaxValue)
                        return false;
                    parsed = (int)longValue;
                    return true;
                }

                if (value is short)
                {
                    parsed = (short)value;
                    return true;
                }

                if (value is byte)
                {
                    parsed = (byte)value;
                    return true;
                }

                string text = value as string;
                if (!string.IsNullOrWhiteSpace(text))
                    return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static object TryGetPropertyObjectLocal(object obj, string propertyName)
        {
            try
            {
                if (obj == null || string.IsNullOrWhiteSpace(propertyName))
                    return null;

                PropertyInfo prop = obj.GetType().GetProperty(propertyName);
                if (prop == null)
                    return null;
                return prop.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private static int ResolvePreferredChoiceIndex(LocalBoxOcrState state, Card.Cards sourceCardId, bool hasPreferredTarget)
        {
            if (state == null)
                return 0;

            if (state.PreferredChoiceIndex > 0)
                return state.PreferredChoiceIndex;

            // Living Roots: when OCR only says "play Living Roots" and does not provide
            // a target or explicit choice, choosing option 2 is safer than falling back
            // to SmartBot's default option 1.
            if ((sourceCardId == Card.Cards.CORE_AT_037
                    || sourceCardId == Card.Cards.AT_037)
                && !hasPreferredTarget)
                return 2;

            return 0;
        }

        private static List<Card.Cards> GetPreferredChoiceOptionCardIds(Card.Cards sourceCardId, int preferredChoiceIndex)
        {
            List<Card.Cards> ids = new List<Card.Cards>();

            Card.Cards optionCardId;
            if (TryResolveKnownChoiceOptionCardId(sourceCardId, preferredChoiceIndex, out optionCardId))
                ids.Add(optionCardId);

            return ids;
        }

        private static bool TryResolveKnownChoiceOptionCardId(Card.Cards sourceCardId, int choiceIndex, out Card.Cards optionCardId)
        {
            optionCardId = default(Card.Cards);
            if (choiceIndex <= 0)
                return false;

            if (sourceCardId == Card.Cards.CORE_AT_037 || sourceCardId == Card.Cards.AT_037)
            {
                if (choiceIndex == 1)
                {
                    optionCardId = Card.Cards.AT_037a;
                    return true;
                }

                if (choiceIndex == 2)
                {
                    optionCardId = Card.Cards.AT_037b;
                    return true;
                }
            }

            return false;
        }

        private static bool IsUsableTeacherChoiceState(LocalBoxOcrState state)
        {
            if (state == null)
                return false;

            if (!state.IsFresh(PendingTeacherRefreshHoldSeconds))
                return false;

            if (!string.Equals(state.Stage, "play", StringComparison.OrdinalIgnoreCase))
                return false;

            return string.Equals(state.Status, "ok", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.Status, "refreshing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.Status, "refresh_pending", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveIntegratedExecutorChoiceEscalation(
            Board board,
            LocalBoxOcrState state,
            out Card preferredCard,
            out int preferredChoiceIndex)
        {
            preferredCard = null;
            preferredChoiceIndex = 0;

            if (board == null || state == null)
                return false;

            if (!string.Equals(state.Status, "ok", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(state.Stage, "play", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            bool isPlayCardAction = string.Equals(state.PreferredActionType, "PlayCard", StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(state.PreferredActionType)
                    && (!IsTeacherUnknownCardId(state.PreferredSourceId) || state.PreferredSourceSlot > 0));
            if (!isPlayCardAction)
                return false;

            if (!string.IsNullOrWhiteSpace(state.PreferredSourceKind)
                && !string.Equals(state.PreferredSourceKind, "card", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Card preferredTarget = null;
            if (HasPreferredTarget(state))
            {
                preferredTarget = FindPreferredTarget(board, state);
                if (preferredTarget == null || preferredTarget.Template == null)
                    return false;
            }

            preferredCard = FindPreferredHandCard(board, state);
            if (preferredCard == null || preferredCard.Template == null)
                return false;

            preferredChoiceIndex = ResolvePreferredChoiceIndex(state, preferredCard, preferredTarget);
            return preferredChoiceIndex > 0;
        }

        private static void ApplyChoiceBias(ProfileParameters p, Card.Cards sourceId, int choiceIndex, int value)
        {
            if (p == null || IsTeacherUnknownCardId(sourceId) || choiceIndex <= 0)
                return;

            try
            {
                p.ChoicesModifiers.AddOrUpdate(sourceId, new Modifier(value, choiceIndex));
            }
            catch
            {
                // ignore
            }
        }

        private static Card FindPreferredFriendlyLocation(Board board, Card.Cards sourceId, int sourceSlot)
        {
            if (board == null || board.MinionFriend == null || board.MinionFriend.Count == 0)
                return null;

            Card slotFallback = null;
            if (sourceSlot > 0 && sourceSlot <= board.MinionFriend.Count)
            {
                Card bySlot = board.MinionFriend[sourceSlot - 1];
                if (IsLocationCard(bySlot))
                {
                    slotFallback = bySlot;
                    if (CardMatches(bySlot, sourceId))
                        return bySlot;
                }
            }

            for (int i = 0; i < board.MinionFriend.Count; i++)
            {
                Card card = board.MinionFriend[i];
                if (IsLocationCard(card) && CardMatches(card, sourceId))
                    return card;
            }

            return slotFallback;
        }

        private static bool HasPreferredTarget(LocalBoxOcrState state)
        {
            if (state == null)
                return false;

            return !string.IsNullOrWhiteSpace(state.PreferredTargetKind)
                || !IsTeacherUnknownCardId(state.PreferredTargetId)
                || state.PreferredTargetSlot > 0;
        }

        private static Card FindPreferredTarget(Board board, LocalBoxOcrState state)
        {
            if (board == null || state == null || !HasPreferredTarget(state))
                return null;

            string kind = state.PreferredTargetKind ?? string.Empty;
            if (string.Equals(kind, "enemy_hero", StringComparison.OrdinalIgnoreCase))
                return board.HeroEnemy;

            if (string.Equals(kind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "own_hero", StringComparison.OrdinalIgnoreCase))
            {
                return board.HeroFriend;
            }

            if (string.Equals(kind, "enemy_minion", StringComparison.OrdinalIgnoreCase))
                return FindCardByIdentity(board.MinionEnemy, state.PreferredTargetId, state.PreferredTargetSlot);

            if (string.Equals(kind, "friendly_minion", StringComparison.OrdinalIgnoreCase))
                return FindCardByIdentity(board.MinionFriend, state.PreferredTargetId, state.PreferredTargetSlot);

            if (string.Equals(kind, "enemy_character", StringComparison.OrdinalIgnoreCase))
            {
                Card enemyMinion = FindCardByIdentity(board.MinionEnemy, state.PreferredTargetId, state.PreferredTargetSlot);
                if (enemyMinion != null)
                    return enemyMinion;
                if (CardMatches(board.HeroEnemy, state.PreferredTargetId))
                    return board.HeroEnemy;
                return board.HeroEnemy;
            }

            if (string.Equals(kind, "friendly_character", StringComparison.OrdinalIgnoreCase))
            {
                Card friendlyMinion = FindCardByIdentity(board.MinionFriend, state.PreferredTargetId, state.PreferredTargetSlot);
                if (friendlyMinion != null)
                    return friendlyMinion;
                if (CardMatches(board.HeroFriend, state.PreferredTargetId))
                    return board.HeroFriend;
                return board.HeroFriend;
            }

            Card fallbackEnemy = FindCardByIdentity(board.MinionEnemy, state.PreferredTargetId, state.PreferredTargetSlot);
            if (fallbackEnemy != null)
                return fallbackEnemy;

            Card fallbackFriendly = FindCardByIdentity(board.MinionFriend, state.PreferredTargetId, state.PreferredTargetSlot);
            if (fallbackFriendly != null)
                return fallbackFriendly;

            if (CardMatches(board.HeroEnemy, state.PreferredTargetId))
                return board.HeroEnemy;

            if (CardMatches(board.HeroFriend, state.PreferredTargetId))
                return board.HeroFriend;

            return null;
        }

        private static Card FindCardByIdentity(IList<Card> cards, Card.Cards cardId, int slot)
        {
            if (cards == null || cards.Count == 0)
                return null;

            Card slotFallback = null;
            if (slot > 0 && slot <= cards.Count)
            {
                Card bySlot = cards[slot - 1];
                if (bySlot != null)
                    slotFallback = bySlot;
                if (CardMatches(bySlot, cardId))
                    return bySlot;
            }

            if (cardId == default(Card.Cards))
                return null;

            for (int i = 0; i < cards.Count; i++)
            {
                Card card = cards[i];
                if (CardMatches(card, cardId))
                    return card;
            }

            return slotFallback;
        }

        private static bool TryResolveExactCardIdentity(IList<Card> cards, Card.Cards cardId, int slot, out Card exactCard)
        {
            exactCard = null;
            if (cards == null || cards.Count == 0)
                return false;

            // ── 优先用 cardId 匹配 ──
            if (!IsTeacherUnknownCardId(cardId))
            {
                Card matched = null;
                int matchCount = 0;
                for (int i = 0; i < cards.Count; i++)
                {
                    Card candidate = cards[i];
                    if (candidate == null || candidate.Template == null || candidate.Template.Id != cardId)
                        continue;
                    matchCount++;
                    matched = candidate;
                }

                if (matchCount == 1)
                {
                    // 唯一匹配，直接返回（无需 slot）
                    exactCard = matched;
                    return true;
                }

                if (matchCount > 1 && slot > 0 && slot <= cards.Count)
                {
                    // 多张同名卡，用 slot 消歧
                    Card bySlot = cards[slot - 1];
                    if (bySlot != null && bySlot.Template != null && bySlot.Template.Id == cardId)
                    {
                        exactCard = bySlot;
                        return true;
                    }
                }

                // 多张同名但 slot 无法消歧（slot=0 或不匹配）：返回第一张匹配的
                // 攻击场景下 daemon 强制 slot=0，此处不应因多张同名随从直接失败
                if (matchCount > 1)
                {
                    for (int i = 0; i < cards.Count; i++)
                    {
                        Card candidate = cards[i];
                        if (candidate != null && candidate.Template != null && candidate.Template.Id == cardId)
                        {
                            exactCard = candidate;
                            return true;
                        }
                    }
                }

                // matchCount == 0
                return false;
            }

            // ── cardId 未知时，仅在有 slot 时按位置返回 ──
            if (slot > 0 && slot <= cards.Count)
            {
                Card bySlot = cards[slot - 1];
                if (bySlot != null && bySlot.Template != null)
                {
                    exactCard = bySlot;
                    return true;
                }
            }

            return false;
        }

        private static bool CardMatches(Card card, Card.Cards cardId)
        {
            if (card == null || card.Template == null)
                return false;

            return IsTeacherUnknownCardId(cardId) || card.Template.Id == cardId;
        }

        private static int GetTag(Card card, Card.GAME_TAG tag)
        {
            if (card == null || card.tags == null || !card.tags.ContainsKey(tag))
                return -1;

            return card.tags[tag];
        }

        private static bool IsLocationCard(Card card)
        {
            if (card == null)
                return false;

            try
            {
                return card.Type == Card.CType.LOCATION;
            }
            catch
            {
                return false;
            }
        }

        private static void ForcePreferredCardCombo(ProfileParameters p, Card card)
        {
            if (p == null || card == null || card.Template == null)
                return;

            try
            {
                p.ComboModifier = new ComboSet(card.Id);
            }
            catch
            {
                // ignore
            }
        }

        private static bool IsSupportedHandActionType(Card.CType type)
        {
            return type == Card.CType.MINION
                || type == Card.CType.SPELL
                || type == Card.CType.WEAPON;
        }

        private static Card.CType ResolveEffectiveHandActionType(Card card)
        {
            if (card == null)
                return default(Card.CType);

            try
            {
                if (IsSupportedHandActionType(card.Type))
                    return card.Type;
            }
            catch
            {
                // ignore
            }

            try
            {
                if (card.Template != null && IsSupportedHandActionType(card.Template.Type))
                    return card.Template.Type;
            }
            catch
            {
                // ignore
            }

            return default(Card.CType);
        }

        private static bool TryApplyTypedCardCastBias(
            ProfileParameters p,
            Card.CType sourceType,
            Card.Cards sourceCardId,
            int sourceEntityId,
            int templateCastValue,
            int instanceCastValue)
        {
            if (p == null || sourceCardId == default(Card.Cards) || sourceEntityId <= 0)
                return false;

            if (sourceType == Card.CType.MINION)
            {
                p.CastMinionsModifiers.AddOrUpdate(sourceCardId, new Modifier(templateCastValue));
                p.CastMinionsModifiers.AddOrUpdate(sourceEntityId, new Modifier(instanceCastValue));
                return true;
            }

            if (sourceType == Card.CType.SPELL)
            {
                p.CastSpellsModifiers.AddOrUpdate(sourceCardId, new Modifier(templateCastValue));
                p.CastSpellsModifiers.AddOrUpdate(sourceEntityId, new Modifier(instanceCastValue));
                return true;
            }

            if (sourceType == Card.CType.WEAPON)
            {
                p.CastWeaponsModifiers.AddOrUpdate(sourceCardId, new Modifier(templateCastValue));
                p.CastWeaponsModifiers.AddOrUpdate(sourceEntityId, new Modifier(instanceCastValue));
                return true;
            }

            return false;
        }

        private static bool TryApplyGenericCardCastBias(
            ProfileParameters p,
            Card card,
            int templateCastValue,
            int instanceCastValue)
        {
            if (p == null || card == null || card.Template == null)
                return false;

            Card.CType effectiveType = ResolveEffectiveHandActionType(card);
            if (IsSupportedHandActionType(effectiveType))
            {
                return TryApplyTypedCardCastBias(
                    p,
                    effectiveType,
                    card.Template.Id,
                    card.Id,
                    templateCastValue,
                    instanceCastValue);
            }

            bool applied = false;
            applied = TryApplyTypedCardCastBias(p, Card.CType.MINION, card.Template.Id, card.Id, templateCastValue, instanceCastValue) || applied;
            applied = TryApplyTypedCardCastBias(p, Card.CType.SPELL, card.Template.Id, card.Id, templateCastValue, instanceCastValue) || applied;
            applied = TryApplyTypedCardCastBias(p, Card.CType.WEAPON, card.Template.Id, card.Id, templateCastValue, instanceCastValue) || applied;
            return applied;
        }

        private static bool TryApplyTypedCardInstanceCastBias(
            ProfileParameters p,
            Card.CType sourceType,
            int sourceEntityId,
            int instanceCastValue)
        {
            if (p == null || sourceEntityId <= 0)
                return false;

            if (sourceType == Card.CType.MINION)
            {
                p.CastMinionsModifiers.AddOrUpdate(sourceEntityId, new Modifier(instanceCastValue));
                return true;
            }

            if (sourceType == Card.CType.SPELL)
            {
                p.CastSpellsModifiers.AddOrUpdate(sourceEntityId, new Modifier(instanceCastValue));
                return true;
            }

            if (sourceType == Card.CType.WEAPON)
            {
                p.CastWeaponsModifiers.AddOrUpdate(sourceEntityId, new Modifier(instanceCastValue));
                return true;
            }

            return false;
        }

        private static bool TryApplyGenericCardInstanceCastBias(
            ProfileParameters p,
            Card card,
            int instanceCastValue)
        {
            if (p == null || card == null || card.Template == null)
                return false;

            Card.CType effectiveType = ResolveEffectiveHandActionType(card);
            if (IsSupportedHandActionType(effectiveType))
                return TryApplyTypedCardInstanceCastBias(p, effectiveType, card.Id, instanceCastValue);

            bool applied = false;
            applied = TryApplyTypedCardInstanceCastBias(p, Card.CType.MINION, card.Id, instanceCastValue) || applied;
            applied = TryApplyTypedCardInstanceCastBias(p, Card.CType.SPELL, card.Id, instanceCastValue) || applied;
            applied = TryApplyTypedCardInstanceCastBias(p, Card.CType.WEAPON, card.Id, instanceCastValue) || applied;
            return applied;
        }

        private static bool TryApplyExactCardCastModifier(
            ProfileParameters p,
            Card sourceCard,
            int targetEntityId,
            Card.Cards targetCardId,
            int modifier)
        {
            if (p == null || sourceCard == null || sourceCard.Template == null || modifier == 0)
                return false;

            Card.CType effectiveType = ResolveEffectiveHandActionType(sourceCard);
            if (IsSupportedHandActionType(effectiveType))
            {
                return TryApplyExactCastModifier(
                    p,
                    effectiveType,
                    sourceCard.Id,
                    sourceCard.Template.Id,
                    targetEntityId,
                    targetCardId,
                    modifier);
            }

            bool applied = false;
            applied = TryApplyExactCastModifier(p, Card.CType.MINION, sourceCard.Id, sourceCard.Template.Id, targetEntityId, targetCardId, modifier) || applied;
            applied = TryApplyExactCastModifier(p, Card.CType.SPELL, sourceCard.Id, sourceCard.Template.Id, targetEntityId, targetCardId, modifier) || applied;
            applied = TryApplyExactCastModifier(p, Card.CType.WEAPON, sourceCard.Id, sourceCard.Template.Id, targetEntityId, targetCardId, modifier) || applied;
            return applied;
        }

        private static bool TryApplyTypedBoxAuditCardPenalty(
            ProfileParameters p,
            Card.CType sourceType,
            Card.Cards sourceCardId,
            int sourceEntityId,
            int value)
        {
            if (p == null || sourceCardId == default(Card.Cards) || sourceEntityId <= 0)
                return false;

            if (sourceType == Card.CType.MINION)
            {
                if (!CanApplyBoxAuditPenalty(p.CastMinionsModifiers, sourceCardId, sourceEntityId))
                    return false;
                p.CastMinionsModifiers.AddOrUpdate(sourceCardId, new Modifier(value));
                p.CastMinionsModifiers.AddOrUpdate(sourceEntityId, new Modifier(value));
                return true;
            }

            if (sourceType == Card.CType.SPELL)
            {
                if (!CanApplyBoxAuditPenalty(p.CastSpellsModifiers, sourceCardId, sourceEntityId))
                    return false;
                p.CastSpellsModifiers.AddOrUpdate(sourceCardId, new Modifier(value));
                p.CastSpellsModifiers.AddOrUpdate(sourceEntityId, new Modifier(value));
                return true;
            }

            if (sourceType == Card.CType.WEAPON)
            {
                if (!CanApplyBoxAuditPenalty(p.CastWeaponsModifiers, sourceCardId, sourceEntityId))
                    return false;
                p.CastWeaponsModifiers.AddOrUpdate(sourceCardId, new Modifier(value));
                p.CastWeaponsModifiers.AddOrUpdate(sourceEntityId, new Modifier(value));
                return true;
            }

            return false;
        }

        private static void ApplyCardBias(ProfileParameters p, Card card, int templateCastValue, int instanceCastValue, int templateOrderValue, int instanceOrderValue)
        {
            if (p == null || card == null || card.Template == null)
                return;

            TryApplyGenericCardCastBias(p, card, templateCastValue, instanceCastValue);

            p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(templateOrderValue));
            p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(instanceOrderValue));
        }

        private static void ApplyCardInstanceBias(ProfileParameters p, Card card, int instanceCastValue, int instanceOrderValue)
        {
            if (p == null || card == null || card.Template == null)
                return;

            TryApplyGenericCardInstanceCastBias(p, card, instanceCastValue);
            p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(instanceOrderValue));
        }

        private static string BuildCardDisplayName(Card card)
        {
            if (card == null || card.Template == null)
                return string.Empty;

            string name = card.Template.NameCN;
            if (string.IsNullOrWhiteSpace(name))
                name = card.Template.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = card.Template.Id.ToString();
            return name ?? string.Empty;
        }

        private static string BuildBoardSummaryZoneLine(IList<Card> cards)
        {
            if (cards == null || cards.Count == 0)
                return "(空)";

            List<string> parts = new List<string>();
            for (int i = 0; i < cards.Count; i++)
            {
                string part = BuildBoardSummaryPart(cards[i], i + 1);
                if (!string.IsNullOrWhiteSpace(part))
                    parts.Add(part);
            }

            return parts.Count > 0 ? string.Join("/", parts) : "(空)";
        }

        private static string BuildBoardSummarySingleLine(Card card, int slot)
        {
            string part = BuildBoardSummaryPart(card, slot);
            return string.IsNullOrWhiteSpace(part) ? "(空)" : part;
        }

        private static string BuildBoardSummaryPart(Card card, int slot)
        {
            if (card == null || card.Template == null)
                return string.Empty;

            string name = SanitizeBoardSummaryField(BuildCardDisplayName(card));
            string templateId = card.Template.Id.ToString();

            return name
                + "+"
                + templateId
                + "+"
                + Math.Max(0, slot).ToString(CultureInfo.InvariantCulture);
        }

        private static Card TryGetEnemyAbility(Board board)
        {
            if (board == null)
                return null;

            try
            {
                PropertyInfo property = board.GetType().GetProperty("EnemyAbility", BindingFlags.Instance | BindingFlags.Public);
                if (property == null)
                    return null;

                return property.GetValue(board, null) as Card;
            }
            catch
            {
                return null;
            }
        }

        private static string SanitizeBoardSummaryField(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            return raw
                .Replace('/', ' ')
                .Replace('+', ' ')
                .Replace('\t', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
        }

        private static string[] SafeReadAllLines(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    var lines = new List<string>();
                    string line;
                    while ((line = sr.ReadLine()) != null)
                        lines.Add(line);
                    return lines.ToArray();
                }
            }
            catch
            {
                return new string[0];
            }
        }

        private static LocalBoxOcrState LoadLocalBoxOcrState()
        {
            string path = ResolveBoxOcrStatePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new LocalBoxOcrState();

            try
            {
                string[] lines = SafeReadAllLines(path);
                LocalBoxOcrState state = new LocalBoxOcrState();
                state.SourcePath = path;
                try
                {
                    state.FileLastWriteUtc = File.GetLastWriteTimeUtc(path);
                }
                catch
                {
                    state.FileLastWriteUtc = DateTime.MinValue;
                }
                string pendingMatchKind = string.Empty;
                Card.Cards pendingMatchId = default(Card.Cards);
                int pendingMatchSlot = 0;
                LocalBoxOcrActionStep pendingActionStep = null;
                string inferredAttackSourceKind = string.Empty;
                Card.Cards inferredAttackSourceId = default(Card.Cards);
                int inferredAttackSourceSlot = 0;
                string inferredAttackTargetKind = string.Empty;
                Card.Cards inferredAttackTargetId = default(Card.Cards);
                int inferredAttackTargetSlot = 0;

                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    int idx = rawLine.IndexOf('=');
                    if (idx <= 0)
                        continue;

                    string key = rawLine.Substring(0, idx).Trim();
                    string value = rawLine.Substring(idx + 1).Trim();
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
                    else if (string.Equals(key, "status_reason", StringComparison.OrdinalIgnoreCase))
                    {
                        state.StatusReason = value;
                    }
                    else if (string.Equals(key, "refresh_pending", StringComparison.OrdinalIgnoreCase))
                    {
                        state.RefreshPending = string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(key, "observed_stage", StringComparison.OrdinalIgnoreCase))
                    {
                        state.ObservedStage = value;
                    }
                    else if (string.Equals(key, "refresh_trigger", StringComparison.OrdinalIgnoreCase))
                    {
                        state.RefreshTrigger = value;
                    }
                    else if (string.Equals(key, "plugin_version", StringComparison.OrdinalIgnoreCase))
                    {
                        state.PluginVersion = value;
                    }
                    else if (string.Equals(key, "sb_profile", StringComparison.OrdinalIgnoreCase))
                    {
                        state.SBProfile = value;
                    }
                    else if (string.Equals(key, "sb_action_delivery", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(state.PreferredDelivery))
                            state.PreferredDelivery = value;
                    }
                    else if (string.Equals(key, "sb_board_sig", StringComparison.OrdinalIgnoreCase))
                    {
                        state.BoardSignature = value;
                    }
                    else if (string.Equals(key, "ts_utc", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime parsed;
                        if (DateTime.TryParse(
                            value,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out parsed))
                        {
                            state.TimestampUtc = parsed;
                        }
                    }
                    else if (string.Equals(key, "card_id", StringComparison.OrdinalIgnoreCase))
                    {
                        Card.Cards cardId;
                        if (TryParseCardId(value, out cardId))
                            state.RecommendedCards.Add(cardId);
                    }
                    else if (string.Equals(key, "hero_power_id", StringComparison.OrdinalIgnoreCase))
                    {
                        Card.Cards heroPowerId;
                        if (TryParseCardId(value, out heroPowerId))
                            state.RecommendedHeroPowers.Add(heroPowerId);
                    }
                    else if (string.Equals(key, "box_turn_num", StringComparison.OrdinalIgnoreCase))
                    {
                        int turnNum;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out turnNum))
                            state.BoxTurnNum = turnNum;
                    }
                    else if (string.Equals(key, "box_choice_id", StringComparison.OrdinalIgnoreCase))
                    {
                        int choiceId;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out choiceId))
                            state.BoxChoiceId = choiceId;
                    }
                    else if (string.Equals(key, "box_option_id", StringComparison.OrdinalIgnoreCase))
                    {
                        int optionId;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out optionId))
                            state.BoxOptionId = optionId;
                    }
                    else if (string.Equals(key, "box_status_code", StringComparison.OrdinalIgnoreCase))
                    {
                        int statusCode;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out statusCode))
                            state.BoxStatusCode = statusCode;
                    }
                    else if (string.Equals(key, "box_data_count", StringComparison.OrdinalIgnoreCase))
                    {
                        int dataCount;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out dataCount))
                            state.BoxDataCount = dataCount;
                    }
                    else if (string.Equals(key, "box_raw_json", StringComparison.OrdinalIgnoreCase))
                    {
                        state.RawBoxJson = value;
                    }
                    else if (string.Equals(key, "keyword", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!state.EndTurnRecommended && ContainsEndTurnHint(value))
                            state.EndTurnRecommended = true;
                    }
                    else if (string.Equals(key, "match_name", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "line", StringComparison.OrdinalIgnoreCase))
                    {
                        AddPlayTextHint(state.TextHints, value);
                        if (!state.EndTurnRecommended && ContainsEndTurnHint(value))
                            state.EndTurnRecommended = true;
                    }
                    else if (string.Equals(key, "match_kind", StringComparison.OrdinalIgnoreCase))
                    {
                        TryFinalizeInferredAttackMatch(
                            pendingMatchKind,
                            pendingMatchId,
                            pendingMatchSlot,
                            ref inferredAttackSourceKind,
                            ref inferredAttackSourceId,
                            ref inferredAttackSourceSlot,
                            ref inferredAttackTargetKind,
                            ref inferredAttackTargetId,
                            ref inferredAttackTargetSlot);
                        pendingMatchKind = value;
                        pendingMatchId = default(Card.Cards);
                        pendingMatchSlot = 0;
                    }
                    else if (string.Equals(key, "match_id", StringComparison.OrdinalIgnoreCase))
                    {
                        Card.Cards matchId;
                        if (TryParseCardId(value, out matchId))
                            pendingMatchId = matchId;
                    }
                    else if (string.Equals(key, "match_slot", StringComparison.OrdinalIgnoreCase))
                    {
                        int matchSlot;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out matchSlot) && matchSlot > 0)
                            pendingMatchSlot = matchSlot;

                        TryFinalizeInferredAttackMatch(
                            pendingMatchKind,
                            pendingMatchId,
                            pendingMatchSlot,
                            ref inferredAttackSourceKind,
                            ref inferredAttackSourceId,
                            ref inferredAttackSourceSlot,
                            ref inferredAttackTargetKind,
                            ref inferredAttackTargetId,
                            ref inferredAttackTargetSlot);
                        pendingMatchKind = string.Empty;
                        pendingMatchId = default(Card.Cards);
                        pendingMatchSlot = 0;
                    }
                    else if (string.Equals(key, "action_type", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingActionStep == null)
                            pendingActionStep = new LocalBoxOcrActionStep();
                        pendingActionStep.ActionType = value;

                        if (string.IsNullOrWhiteSpace(state.PreferredActionType))
                            state.PreferredActionType = value;
                        if (!state.EndTurnRecommended && ContainsEndTurnHint(value))
                            state.EndTurnRecommended = true;
                    }
                    else if (string.Equals(key, "action_step_index", StringComparison.OrdinalIgnoreCase))
                    {
                        TryFinalizeLocalBoxOcrActionStep(state, pendingActionStep);
                        pendingActionStep = new LocalBoxOcrActionStep();

                        int stepIndex;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out stepIndex) && stepIndex > 0)
                            pendingActionStep.StepIndex = stepIndex;

                        if (state.PreferredStepIndex <= 0)
                        {
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out stepIndex) && stepIndex > 0)
                                state.PreferredStepIndex = stepIndex;
                        }
                    }
                    else if (string.Equals(key, "action_source_kind", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingActionStep == null)
                            pendingActionStep = new LocalBoxOcrActionStep();
                        pendingActionStep.SourceKind = value;

                        if (string.IsNullOrWhiteSpace(state.PreferredSourceKind))
                            state.PreferredSourceKind = value;
                    }
                    else if (string.Equals(key, "action_source_id", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingActionStep == null)
                            pendingActionStep = new LocalBoxOcrActionStep();

                        Card.Cards actionSourceId;
                        if (TryParseCardId(value, out actionSourceId))
                            pendingActionStep.SourceId = actionSourceId;

                        if (IsTeacherUnknownCardId(state.PreferredSourceId))
                        {
                            if (TryParseCardId(value, out actionSourceId))
                                state.PreferredSourceId = actionSourceId;
                        }
                    }
                    else if (string.Equals(key, "action_source_slot", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingActionStep == null)
                            pendingActionStep = new LocalBoxOcrActionStep();

                        int slot;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) && slot > 0)
                            pendingActionStep.SourceSlot = slot;

                        if (state.PreferredSourceSlot <= 0)
                        {
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) && slot > 0)
                                state.PreferredSourceSlot = slot;
                        }
                    }
                    else if (string.Equals(key, "action_choice_index", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingActionStep == null)
                            pendingActionStep = new LocalBoxOcrActionStep();

                        int choiceIndex;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out choiceIndex) && choiceIndex > 0)
                            pendingActionStep.ChoiceIndex = choiceIndex;

                        if (state.PreferredChoiceIndex <= 0)
                        {
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out choiceIndex) && choiceIndex > 0)
                                state.PreferredChoiceIndex = choiceIndex;
                        }
                    }
                    else if (string.Equals(key, "action_delivery", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingActionStep == null)
                            pendingActionStep = new LocalBoxOcrActionStep();
                        pendingActionStep.Delivery = value;

                        if (string.IsNullOrWhiteSpace(state.PreferredDelivery))
                            state.PreferredDelivery = value;
                    }
                    else if (string.Equals(key, "action_board_slot", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingActionStep == null)
                            pendingActionStep = new LocalBoxOcrActionStep();

                        int boardSlot;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out boardSlot) && boardSlot > 0)
                            pendingActionStep.BoardSlot = boardSlot;

                        if (state.PreferredBoardSlot <= 0)
                        {
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out boardSlot) && boardSlot > 0)
                                state.PreferredBoardSlot = boardSlot;
                        }
                    }
                    else if (string.Equals(key, "action_required", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingActionStep == null)
                            pendingActionStep = new LocalBoxOcrActionStep();

                        pendingActionStep.ActionRequired = string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                        state.PreferredActionRequired = pendingActionStep.ActionRequired;
                    }
                    else if (string.Equals(key, "action_target_kind", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingActionStep == null)
                            pendingActionStep = new LocalBoxOcrActionStep();
                        pendingActionStep.TargetKind = value;

                        if (string.IsNullOrWhiteSpace(state.PreferredTargetKind))
                            state.PreferredTargetKind = value;
                    }
                    else if (string.Equals(key, "action_target_id", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingActionStep == null)
                            pendingActionStep = new LocalBoxOcrActionStep();

                        Card.Cards targetId;
                        if (TryParseCardId(value, out targetId))
                            pendingActionStep.TargetId = targetId;

                        if (IsTeacherUnknownCardId(state.PreferredTargetId))
                        {
                            if (TryParseCardId(value, out targetId))
                                state.PreferredTargetId = targetId;
                        }
                    }
                    else if (string.Equals(key, "action_target_slot", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingActionStep == null)
                            pendingActionStep = new LocalBoxOcrActionStep();

                        int targetSlot;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out targetSlot) && targetSlot > 0)
                            pendingActionStep.TargetSlot = targetSlot;

                        if (state.PreferredTargetSlot <= 0)
                        {
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out targetSlot) && targetSlot > 0)
                                state.PreferredTargetSlot = targetSlot;
                        }
                    }
                    else if (string.Equals(key, "rec_hash", StringComparison.OrdinalIgnoreCase))
                    {
                        state.RecHash = value;
                    }
                }

                TryFinalizeLocalBoxOcrActionStep(state, pendingActionStep);
                TryFinalizeInferredAttackMatch(
                    pendingMatchKind,
                    pendingMatchId,
                    pendingMatchSlot,
                    ref inferredAttackSourceKind,
                    ref inferredAttackSourceId,
                    ref inferredAttackSourceSlot,
                    ref inferredAttackTargetKind,
                    ref inferredAttackTargetId,
                    ref inferredAttackTargetSlot);

                if (string.IsNullOrWhiteSpace(state.PreferredActionType)
                    && IsTeacherUnknownCardId(state.PreferredSourceId)
                    && IsTeacherUnknownCardId(state.PreferredTargetId)
                    && HasInferredAttackIntent(
                        inferredAttackSourceKind,
                        inferredAttackSourceId,
                        inferredAttackSourceSlot,
                        inferredAttackTargetKind,
                        inferredAttackTargetId,
                        inferredAttackTargetSlot))
                {
                    // 保留 partial attack intent，让后续链路还能继续利用 kind/slot 做安全解析。
                    state.PreferredActionType = "Attack";
                    state.PreferredSourceKind = inferredAttackSourceKind;
                    state.PreferredSourceId = inferredAttackSourceId;
                    state.PreferredSourceSlot = inferredAttackSourceSlot;
                    state.PreferredTargetKind = inferredAttackTargetKind;
                    state.PreferredTargetId = inferredAttackTargetId;
                    state.PreferredTargetSlot = inferredAttackTargetSlot;
                }

                if (state.ActionSteps.Count > 0)
                    ApplyLocalBoxOcrActionStep(state, state.ActionSteps[0]);

                state.RawText = string.Join(
                    " | ",
                    lines.Where(line => !line.StartsWith("box_raw_json=", StringComparison.OrdinalIgnoreCase)));
                return state;
            }
            catch
            {
                return new LocalBoxOcrState();
            }
        }

        private static void TryFinalizeInferredAttackMatch(
            string pendingMatchKind,
            Card.Cards pendingMatchId,
            int pendingMatchSlot,
            ref string inferredAttackSourceKind,
            ref Card.Cards inferredAttackSourceId,
            ref int inferredAttackSourceSlot,
            ref string inferredAttackTargetKind,
            ref Card.Cards inferredAttackTargetId,
            ref int inferredAttackTargetSlot)
        {
            if (string.IsNullOrWhiteSpace(pendingMatchKind))
                return;

            bool hasIdentity = pendingMatchId != default(Card.Cards)
                || pendingMatchSlot > 0
                || string.Equals(pendingMatchKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pendingMatchKind, "own_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pendingMatchKind, "enemy_hero", StringComparison.OrdinalIgnoreCase);
            if (!hasIdentity)
                return;

            if (string.IsNullOrWhiteSpace(inferredAttackSourceKind)
                && (string.Equals(pendingMatchKind, "friendly_minion", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pendingMatchKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pendingMatchKind, "own_hero", StringComparison.OrdinalIgnoreCase)))
            {
                inferredAttackSourceKind = pendingMatchKind;
                inferredAttackSourceId = pendingMatchId;
                inferredAttackSourceSlot = pendingMatchSlot;
                return;
            }

            if (string.IsNullOrWhiteSpace(inferredAttackTargetKind)
                && (string.Equals(pendingMatchKind, "enemy_minion", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pendingMatchKind, "enemy_hero", StringComparison.OrdinalIgnoreCase)))
            {
                inferredAttackTargetKind = pendingMatchKind;
                inferredAttackTargetId = pendingMatchId;
                inferredAttackTargetSlot = pendingMatchSlot;
            }
        }

        private static bool HasInferredAttackIntent(
            string sourceKind,
            Card.Cards sourceId,
            int sourceSlot,
            string targetKind,
            Card.Cards targetId,
            int targetSlot)
        {
            bool hasSource = !string.IsNullOrWhiteSpace(sourceKind)
                && (sourceId != default(Card.Cards)
                    || sourceSlot > 0
                    || string.Equals(sourceKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sourceKind, "own_hero", StringComparison.OrdinalIgnoreCase));
            bool hasTarget = !string.IsNullOrWhiteSpace(targetKind)
                && (targetId != default(Card.Cards)
                    || targetSlot > 0
                    || string.Equals(targetKind, "enemy_hero", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(targetKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(targetKind, "own_hero", StringComparison.OrdinalIgnoreCase));
            return hasSource && hasTarget;
        }

        private static string _cachedBoxOcrStatePath;
        private static DateTime _cachedBoxOcrStatePathTime = DateTime.MinValue;

        private static string ResolveBoxOcrStatePath()
        {
            // 缓存有效期 30 秒，避免频繁磁盘探测；但不永久缓存空值，
            // 以防插件启用状态变更或文件延迟出现。
            if (_cachedBoxOcrStatePath != null
                && (DateTime.UtcNow - _cachedBoxOcrStatePathTime).TotalSeconds < 30
                && (_cachedBoxOcrStatePath.Length > 0 || !IsRankedWinOcrPluginEnabled()))
            {
                return _cachedBoxOcrStatePath;
            }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new[]
                {
                    Path.Combine(baseDir, "runtime", "decision_teacher_state.txt"),
                    Path.Combine(baseDir, "runtime", "netease_box_ocr_state.txt"),
                    Path.Combine(baseDir, "decision_teacher_state.txt"),
                    Path.Combine(baseDir, "netease_box_ocr_state.txt"),
                    Path.Combine(baseDir, "Temp", "runtime", "decision_teacher_state.txt"),
                    Path.Combine(baseDir, "Temp", "runtime", "netease_box_ocr_state.txt"),
                    Path.Combine(baseDir, "..", "Temp", "runtime", "decision_teacher_state.txt"),
                    Path.Combine(baseDir, "..", "Temp", "runtime", "netease_box_ocr_state.txt")
                };

                foreach (string candidate in candidates)
                {
                    try
                    {
                        string normalized = Path.GetFullPath(candidate);
                        if (File.Exists(normalized))
                        {
                            _cachedBoxOcrStatePath = normalized;
                            _cachedBoxOcrStatePathTime = DateTime.UtcNow;
                            return normalized;
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                // 未找到任何候选文件时，短暂缓存空值
                _cachedBoxOcrStatePath = string.Empty;
                _cachedBoxOcrStatePathTime = DateTime.UtcNow;
                return _cachedBoxOcrStatePath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsRankedWinOcrPluginEnabled()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "RankedWinOcrPlugin.json");
                if (!File.Exists(configPath))
                    return false;

                string raw = File.ReadAllText(configPath);
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                string compact = raw.Replace(" ", string.Empty)
                    .Replace("\t", string.Empty)
                    .Replace("\r", string.Empty)
                    .Replace("\n", string.Empty);
                return compact.IndexOf("\"Enabled\":true", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryRefreshLocalBoxOcrBoardSignature(string currentBoardSignature)
        {
            if (string.IsNullOrWhiteSpace(currentBoardSignature))
                return false;

            _overrideBoardSig = currentBoardSignature;
            return true;
        }

        private static string BuildCurrentPlayBoardSignatureKey(Board board, string profileName)
        {
            List<string> candidateRows = BuildLocalPlayCandidateRows(board);
            StringBuilder sb = new StringBuilder();
            sb.Append(profileName ?? string.Empty);
            sb.Append('|').Append(SafeCurrentModeName());

            if (board != null)
            {
                sb.Append('|').Append(board.IsOwnTurn ? '1' : '0');
                sb.Append('|').Append(board.ManaAvailable);
                sb.Append('|').Append(board.MaxMana);
            }

            for (int i = 0; i < candidateRows.Count; i++)
                sb.Append('|').Append(candidateRows[i]);

            return ComputeStableHash(sb.ToString());
        }

        private static List<string> BuildLocalPlayCandidateRows(Board board)
        {
            List<string> rows = new List<string>();
            if (board == null)
                return rows;

            try
            {
                if (board.Hand != null)
                {
                    for (int i = 0; i < board.Hand.Count; i++)
                    {
                        Card card = board.Hand[i];
                        if (card == null || card.Template == null)
                            continue;
                        rows.Add(BuildLocalCandidateRow("card", card.Template.Id, GetLocalCardName(card), (i + 1).ToString()));
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.Ability != null && board.Ability.Template != null)
                    rows.Add(BuildLocalCandidateRow("hero_power", board.Ability.Template.Id, GetLocalCardName(board.Ability), "0"));
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.WeaponFriend != null && board.WeaponFriend.Template != null)
                    rows.Add(BuildLocalCandidateRow("weapon", board.WeaponFriend.Template.Id, GetLocalCardName(board.WeaponFriend), "0"));
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.MinionFriend != null)
                {
                    for (int i = 0; i < board.MinionFriend.Count; i++)
                    {
                        Card minion = board.MinionFriend[i];
                        if (minion == null || minion.Template == null)
                            continue;
                        rows.Add(BuildLocalCandidateRow("friendly_minion", minion.Template.Id, GetLocalCardName(minion), (i + 1).ToString()));
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.MinionEnemy != null)
                {
                    for (int i = 0; i < board.MinionEnemy.Count; i++)
                    {
                        Card minion = board.MinionEnemy[i];
                        if (minion == null || minion.Template == null)
                            continue;
                        rows.Add(BuildLocalCandidateRow("enemy_minion", minion.Template.Id, GetLocalCardName(minion), (i + 1).ToString()));
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.HeroEnemy != null && board.HeroEnemy.Template != null)
                    rows.Add(BuildLocalCandidateRow("enemy_hero", board.HeroEnemy.Template.Id, GetLocalCardName(board.HeroEnemy), "0"));
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.HeroFriend != null && board.HeroFriend.Template != null)
                    rows.Add(BuildLocalCandidateRow("friendly_hero", board.HeroFriend.Template.Id, GetLocalCardName(board.HeroFriend), "0"));
            }
            catch
            {
                // ignore
            }

            return rows;
        }

        private static string BuildLocalCandidateRow(string kind, Card.Cards cardId, string name, string slot)
        {
            return (kind ?? "card")
                + "\t" + cardId
                + "\t" + SanitizeLocalCandidateField(name)
                + "\t" + (slot ?? "0");
        }

        private static string GetLocalCardName(Card card)
        {
            if (card == null || card.Template == null)
                return string.Empty;

            string name = card.Template.NameCN;
            if (string.IsNullOrWhiteSpace(name))
                name = card.Template.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = card.Template.Id.ToString();
            return name ?? string.Empty;
        }

        private static string SanitizeLocalCandidateField(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            return raw.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static string SafeCurrentModeName()
        {
            try
            {
                Bot.Mode mode = Bot.CurrentMode();
                if (mode == Bot.Mode.Practice || mode == Bot.Mode.Casual)
                {
                    try
                    {
                        string deckName = Bot.CurrentDeck() != null ? Bot.CurrentDeck().Name : string.Empty;
                        if (!string.IsNullOrWhiteSpace(deckName) && deckName.Length > 1)
                        {
                            string key = deckName.Substring(1, 1);
                            return key == "S" ? "Standard" : "Wild";
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    return "Wild";
                }

                if (mode == Bot.Mode.Arena || mode == Bot.Mode.ArenaAuto)
                    return "Arena";
                if (mode == Bot.Mode.Standard)
                    return "Standard";

                return "Wild";
            }
            catch
            {
                return "Wild";
            }
        }

        private static string ComputeStableHash(string raw)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                string value = raw ?? string.Empty;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 1099511628211UL;
                }

                return hash.ToString("X16");
            }
        }

        private static bool CanApplyWeakPositiveBias(RulesSet rules, Card.Cards cardId, int instanceId)
        {
            int cardLevel = GetRuleValue(rules, cardId, (int)cardId);
            int instanceLevel = GetRuleValue(rules, cardId, instanceId);
            int current = MergeRuleValue(cardLevel, instanceLevel);

            if (current == int.MinValue)
                return true;

            if (current >= 1600 || current <= -1200)
                return false;

            return true;
        }

        private static void TryApplyWeakPositiveRule(RulesSet rules, Card.Cards cardId, int instanceId, int value)
        {
            if (rules == null || value <= 0)
                return;

            if (cardId == default(Card.Cards) && instanceId <= 0)
                return;

            if (!CanApplyWeakPositiveBias(rules, cardId, instanceId))
                return;

            if (cardId != default(Card.Cards))
                rules.AddOrUpdate(cardId, new Modifier(value));

            if (instanceId > 0)
                rules.AddOrUpdate(instanceId, new Modifier(value));
        }

        private static int MergeRuleValue(int first, int second)
        {
            if (first == int.MinValue)
                return second;
            if (second == int.MinValue)
                return first;
            return Math.Abs(first) >= Math.Abs(second) ? first : second;
        }

        private static int GetRuleValue(RulesSet rules, Card.Cards cardId, int instanceId)
        {
            if (rules == null)
                return int.MinValue;

            try
            {
                Rule rule = null;

                try { rule = rules.RulesCardIds != null ? rules.RulesCardIds[cardId] : null; }
                catch { rule = null; }
                if (rule != null && rule.CardModifier != null)
                    return rule.CardModifier.Value;

                try { rule = rules.RulesIntIds != null ? rules.RulesIntIds[instanceId] : null; }
                catch { rule = null; }
                if (rule != null && rule.CardModifier != null)
                    return rule.CardModifier.Value;
            }
            catch
            {
                return int.MinValue;
            }

            return int.MinValue;
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

        private sealed class BoxAuditGuardState
        {
            public DateTime TimestampUtc = DateTime.MinValue;
            public int ExpiresAfterSeconds = 900;
            public int ConsistencyScore = 100;
            public int Penalty = 500;
            public string Summary = string.Empty;
            public readonly HashSet<string> SuppressStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<Card.Cards> PenalizedCardIds = new HashSet<Card.Cards>();
            public readonly HashSet<Card.Cards> PenalizedHeroPowerIds = new HashSet<Card.Cards>();

            public bool IsFresh()
            {
                if (TimestampUtc == DateTime.MinValue)
                    return false;

                return TimestampUtc >= DateTime.UtcNow.AddSeconds(-Math.Max(30, ExpiresAfterSeconds));
            }

            public bool SuppressesStage(string stage)
            {
                if (string.IsNullOrWhiteSpace(stage))
                    return false;

                return SuppressStages.Contains("all") || SuppressStages.Contains(stage.Trim());
            }
        }

        private static string _cachedBoxAuditGuardPath;

        private static string ResolveBoxAuditGuardPath()
        {
            if (_cachedBoxAuditGuardPath != null)
                return _cachedBoxAuditGuardPath;

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new[]
                {
                    Path.Combine(baseDir, "runtime", "box_audit_guard.txt"),
                    Path.Combine(baseDir, "Temp", "runtime", "box_audit_guard.txt"),
                    Path.Combine(baseDir, "..", "Temp", "runtime", "box_audit_guard.txt")
                };

                foreach (string candidate in candidates)
                {
                    try
                    {
                        string normalized = Path.GetFullPath(candidate);
                        if (File.Exists(normalized))
                        {
                            _cachedBoxAuditGuardPath = normalized;
                            return normalized;
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                _cachedBoxAuditGuardPath = Path.GetFullPath(candidates[0]);
                return _cachedBoxAuditGuardPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static BoxAuditGuardState LoadBoxAuditGuardState()
        {
            string path = ResolveBoxAuditGuardPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new BoxAuditGuardState();

            BoxAuditGuardState state = new BoxAuditGuardState();
            try
            {
                string[] lines = SafeReadAllLines(path);
                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    int idx = rawLine.IndexOf('=');
                    if (idx <= 0)
                        continue;

                    string key = rawLine.Substring(0, idx).Trim();
                    string value = rawLine.Substring(idx + 1).Trim();
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    if (string.Equals(key, "generated_at_utc", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime parsed;
                        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                            state.TimestampUtc = parsed;
                    }
                    else if (string.Equals(key, "expires_after_seconds", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                            state.ExpiresAfterSeconds = parsed;
                    }
                    else if (string.Equals(key, "consistency_score", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                            state.ConsistencyScore = parsed;
                    }
                    else if (string.Equals(key, "penalty", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                            state.Penalty = parsed;
                    }
                    else if (string.Equals(key, "summary", StringComparison.OrdinalIgnoreCase))
                    {
                        state.Summary = value;
                    }
                    else if (string.Equals(key, "suppress_stage", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                            state.SuppressStages.Add(value.Trim());
                    }
                    else if (string.Equals(key, "penalize_card_id", StringComparison.OrdinalIgnoreCase))
                    {
                        Card.Cards parsed;
                        if (TryParseCardId(value, out parsed))
                            state.PenalizedCardIds.Add(parsed);
                    }
                    else if (string.Equals(key, "penalize_hero_power_id", StringComparison.OrdinalIgnoreCase))
                    {
                        Card.Cards parsed;
                        if (TryParseCardId(value, out parsed))
                            state.PenalizedHeroPowerIds.Add(parsed);
                    }
                }
            }
            catch
            {
                return new BoxAuditGuardState();
            }

            return state;
        }

        private static bool IsCardPenalized(BoxAuditGuardState state, Card.Cards cardId)
        {
            return state != null
                && state.IsFresh()
                && cardId != default(Card.Cards)
                && state.PenalizedCardIds.Contains(cardId);
        }

        private static bool IsHeroPowerPenalized(BoxAuditGuardState state, Card.Cards cardId)
        {
            return state != null
                && state.IsFresh()
                && cardId != default(Card.Cards)
                && state.PenalizedHeroPowerIds.Contains(cardId);
        }

        private static bool IsPreferredPlayStepPenalized(BoxAuditGuardState state, LocalBoxOcrState teacherState)
        {
            if (state == null || teacherState == null || !state.IsFresh())
                return false;

            if (string.Equals(teacherState.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase))
                return IsHeroPowerPenalized(state, teacherState.PreferredSourceId);

            return IsCardPenalized(state, teacherState.PreferredSourceId);
        }

        private static bool CanApplyBoxAuditPenalty(RulesSet rules, Card.Cards cardId, int instanceId)
        {
            int cardLevel = GetRuleValue(rules, cardId, (int)cardId);
            int instanceLevel = GetRuleValue(rules, cardId, instanceId);
            int current = MergeRuleValue(cardLevel, instanceLevel);
            if (current == int.MinValue)
                return true;

            return current > -1800 && current < 4800;
        }

        private static bool TryApplyBoxAuditCardPenalty(ProfileParameters p, Card card, int penalty)
        {
            if (p == null || card == null || card.Template == null)
                return false;

            int value = Math.Max(180, penalty);
            int orderPenalty = -Math.Max(120, penalty / 2);

            bool applied = false;
            Card.CType effectiveType = ResolveEffectiveHandActionType(card);
            if (IsSupportedHandActionType(effectiveType))
            {
                applied = TryApplyTypedBoxAuditCardPenalty(p, effectiveType, card.Template.Id, card.Id, value);
            }
            else
            {
                applied = TryApplyTypedBoxAuditCardPenalty(p, Card.CType.MINION, card.Template.Id, card.Id, value) || applied;
                applied = TryApplyTypedBoxAuditCardPenalty(p, Card.CType.SPELL, card.Template.Id, card.Id, value) || applied;
                applied = TryApplyTypedBoxAuditCardPenalty(p, Card.CType.WEAPON, card.Template.Id, card.Id, value) || applied;
            }

            if (!applied)
                return false;

            p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(orderPenalty));
            p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(orderPenalty));
            return true;
        }

        private static bool TryApplyBoxAuditHeroPowerPenalty(ProfileParameters p, Card.Cards heroPowerId, int penalty)
        {
            if (p == null || heroPowerId == default(Card.Cards))
                return false;

            if (!CanApplyBoxAuditPenalty(p.CastHeroPowerModifier, heroPowerId, (int)heroPowerId))
                return false;

            p.CastHeroPowerModifier.AddOrUpdate(heroPowerId, new Modifier(Math.Max(180, penalty)));
            p.PlayOrderModifiers.AddOrUpdate(heroPowerId, new Modifier(-Math.Max(120, penalty / 2)));
            return true;
        }

        private static bool TryApplyExactAttackModifier(
            ProfileParameters p,
            int sourceEntityId,
            Card.Cards sourceCardId,
            int targetEntityId,
            Card.Cards targetCardId,
            int modifier)
        {
            if (p == null || modifier == 0)
                return false;

            try
            {
                if (sourceEntityId > 0 && targetEntityId > 0)
                {
                    p.MinionsAttackModifiers.AddOrUpdate(sourceEntityId, modifier, targetEntityId);
                    return true;
                }

                if (sourceEntityId > 0 && targetCardId != default(Card.Cards))
                {
                    p.MinionsAttackModifiers.AddOrUpdate(sourceEntityId, modifier, targetCardId);
                    return true;
                }

                if (sourceCardId != default(Card.Cards) && targetEntityId > 0)
                {
                    p.MinionsAttackModifiers.AddOrUpdate(sourceCardId, modifier, targetEntityId);
                    return true;
                }

                if (sourceCardId != default(Card.Cards) && targetCardId != default(Card.Cards))
                {
                    p.MinionsAttackModifiers.AddOrUpdate(sourceCardId, modifier, targetCardId);
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool TryApplyExactWeaponAttackModifier(
            ProfileParameters p,
            int sourceEntityId,
            Card.Cards sourceCardId,
            int targetEntityId,
            Card.Cards targetCardId,
            int modifier)
        {
            if (p == null || modifier == 0)
                return false;

            try
            {
                if (sourceEntityId > 0 && targetEntityId > 0)
                {
                    p.WeaponsAttackModifiers.AddOrUpdate(sourceEntityId, modifier, targetEntityId);
                    return true;
                }

                if (sourceEntityId > 0 && targetCardId != default(Card.Cards))
                {
                    p.WeaponsAttackModifiers.AddOrUpdate(sourceEntityId, modifier, targetCardId);
                    return true;
                }

                if (sourceCardId != default(Card.Cards) && targetEntityId > 0)
                {
                    p.WeaponsAttackModifiers.AddOrUpdate(sourceCardId, modifier, targetEntityId);
                    return true;
                }

                if (sourceCardId != default(Card.Cards) && targetCardId != default(Card.Cards))
                {
                    p.WeaponsAttackModifiers.AddOrUpdate(sourceCardId, modifier, targetCardId);
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool TryApplyExactCastModifier(
            ProfileParameters p,
            Card.CType sourceType,
            int sourceEntityId,
            Card.Cards sourceCardId,
            int targetEntityId,
            Card.Cards targetCardId,
            int modifier)
        {
            if (p == null || modifier == 0)
                return false;

            try
            {
                if (sourceType == Card.CType.MINION)
                {
                    if (sourceEntityId > 0 && targetEntityId > 0)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceEntityId > 0 && targetCardId != default(Card.Cards))
                    {
                        p.CastMinionsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetCardId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetEntityId > 0)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetCardId != default(Card.Cards))
                    {
                        p.CastMinionsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetCardId));
                        return true;
                    }
                }
                else if (sourceType == Card.CType.SPELL)
                {
                    if (sourceEntityId > 0 && targetEntityId > 0)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceEntityId > 0 && targetCardId != default(Card.Cards))
                    {
                        p.CastSpellsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetCardId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetEntityId > 0)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetCardId != default(Card.Cards))
                    {
                        p.CastSpellsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetCardId));
                        return true;
                    }
                }
                else if (sourceType == Card.CType.WEAPON)
                {
                    if (sourceEntityId > 0 && targetEntityId > 0)
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceEntityId > 0 && targetCardId != default(Card.Cards))
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(sourceEntityId, new Modifier(modifier, targetCardId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetEntityId > 0)
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetEntityId));
                        return true;
                    }

                    if (sourceCardId != default(Card.Cards) && targetCardId != default(Card.Cards))
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(sourceCardId, new Modifier(modifier, targetCardId));
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool TryApplyExactHeroPowerModifier(
            ProfileParameters p,
            Card.Cards heroPowerId,
            int targetEntityId,
            Card.Cards targetCardId,
            int modifier)
        {
            if (p == null || heroPowerId == default(Card.Cards) || modifier == 0)
                return false;

            try
            {
                if (targetEntityId > 0)
                {
                    p.CastHeroPowerModifier.AddOrUpdate(heroPowerId, new Modifier(modifier, targetEntityId));
                    return true;
                }

                if (targetCardId != default(Card.Cards))
                {
                    p.CastHeroPowerModifier.AddOrUpdate(heroPowerId, new Modifier(modifier, targetCardId));
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static void ApplyHeroPowerCastAndOrderBias(ProfileParameters p, Board board, int castValue, int orderValue)
        {
            if (p == null || board == null || board.Ability == null || board.Ability.Template == null)
                return;

            try
            {
                p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Template.Id, new Modifier(castValue));
                if (board.Ability.Id > 0)
                    p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Id, new Modifier(castValue));
            }
            catch
            {
                // ignore
            }

            try
            {
                p.PlayOrderModifiers.AddOrUpdate(board.Ability.Template.Id, new Modifier(orderValue));
                if (board.Ability.Id > 0)
                    p.PlayOrderModifiers.AddOrUpdate(board.Ability.Id, new Modifier(orderValue));
            }
            catch
            {
                // ignore
            }
        }

        public Card.Cards ChooseOneCard(Board board, Card.Cards ChooseOneCard)
        {
            LocalBoxOcrState state = null;
            try
            {
                state = LoadLocalBoxOcrState();
            }
            catch
            {
                state = null;
            }

            bool teacherStateUsable = IsUsableTeacherChoiceState(state);
            Card.Cards sourceCardId = ChooseOneCard;
            if (teacherStateUsable && !IsTeacherUnknownCardId(state.PreferredSourceId))
                sourceCardId = state.PreferredSourceId;

            int preferredChoiceIndex = teacherStateUsable
                ? ResolvePreferredChoiceIndex(state, sourceCardId, state != null && HasPreferredTarget(state))
                : 0;
            List<Card.Cards> preferredCardIds = teacherStateUsable
                ? GetPreferredChoiceOptionCardIds(sourceCardId, preferredChoiceIndex)
                : new List<Card.Cards>();

            List<LocalChoiceRuntimeOption> options;
            bool hasOptions = TryGetCurrentChoiceOptionsLocal(board, out options) && options != null && options.Count > 0;

            string stateSummary = state == null
                ? "state=none"
                : "state="
                    + (string.IsNullOrWhiteSpace(state.Status) ? "(empty)" : state.Status)
                    + "/"
                    + (string.IsNullOrWhiteSpace(state.Stage) ? "(empty)" : state.Stage)
                    + " source="
                    + DescribeChoiceCardId(state.PreferredSourceId)
                    + " source_slot="
                    + state.PreferredSourceSlot.ToString(CultureInfo.InvariantCulture)
                    + " choice_index="
                    + state.PreferredChoiceIndex.ToString(CultureInfo.InvariantCulture)
                    + " target="
                    + DescribeChoiceCardId(state.PreferredTargetId)
                    + " age="
                    + GetLocalBoxOcrStateAgeSeconds(state).ToString(CultureInfo.InvariantCulture)
                    + "s";

            LogImmediate("[BOXOCR][CHOICE][CALL] request="
                + DescribeChoiceCardId(ChooseOneCard)
                + " | source="
                + DescribeChoiceCardId(sourceCardId)
                + " | teacher_usable="
                + (teacherStateUsable ? "1" : "0")
                + " | "
                + stateSummary);
            LogImmediate("[BOXOCR][CHOICE][RUNTIME] "
                + DescribeLocalChoiceOptionsSnapshot(options));
            LogImmediate("[BOXOCR][CHOICE][PREF] "
                + DescribeLocalChoicePreference(preferredChoiceIndex, preferredCardIds));

            Card.Cards resolvedChoice = default(Card.Cards);
            string reason = string.Empty;

            if (hasOptions && preferredChoiceIndex > 0)
            {
                LocalChoiceRuntimeOption indexedOption = options.FirstOrDefault(option =>
                    option != null
                    && option.ChoiceIndex == preferredChoiceIndex
                    && option.CardId != default(Card.Cards));
                if (indexedOption != null)
                {
                    resolvedChoice = indexedOption.CardId;
                    reason = "index->runtime_card";
                }
            }

            if (resolvedChoice == default(Card.Cards) && hasOptions && preferredCardIds.Count > 0)
            {
                List<LocalChoiceRuntimeOption> matches = options
                    .Where(option => option != null
                        && option.CardId != default(Card.Cards)
                        && preferredCardIds.Contains(option.CardId))
                    .ToList();
                if (matches.Count == 1)
                {
                    resolvedChoice = matches[0].CardId;
                    reason = "preferred_card_match";
                }
                else if (matches.Count > 1)
                {
                    LogImmediate("[BOXOCR][CHOICE][MATCH] preferred_card_matches="
                        + matches.Count.ToString(CultureInfo.InvariantCulture)
                        + " | 卡牌匹配不唯一，继续回退");
                }
            }

            if (resolvedChoice == default(Card.Cards) && preferredCardIds.Count == 1)
            {
                resolvedChoice = preferredCardIds[0];
                reason = "static_preferred_card";
            }

            if (resolvedChoice == default(Card.Cards) && hasOptions)
            {
                LocalChoiceRuntimeOption firstOption = options.FirstOrDefault(option =>
                    option != null && option.CardId != default(Card.Cards));
                if (firstOption != null)
                {
                    resolvedChoice = firstOption.CardId;
                    reason = teacherStateUsable ? "fallback_first_runtime_option" : "default_first_runtime_option";
                }
            }

            if (resolvedChoice != default(Card.Cards))
            {
                LogImmediate("[BOXOCR][CHOICE][RETURN] "
                    + DescribeChoiceCardId(resolvedChoice)
                    + " | reason="
                    + reason);
                return resolvedChoice;
            }

            LogImmediate("[BOXOCR][CHOICE][RETURN] unresolved | fallback=default(Card.Cards)");
            return default(Card.Cards);
        }

        public Card.Cards SirFinleyChoice(System.Collections.Generic.List<Card.Cards> choices)
        {
            return choices != null && choices.Count > 0 ? choices[0] : default(Card.Cards);
        }

        public Card.Cards KazakusChoice(System.Collections.Generic.List<Card.Cards> choices)
        {
            return choices != null && choices.Count > 0 ? choices[0] : default(Card.Cards);
        }
    }
}

