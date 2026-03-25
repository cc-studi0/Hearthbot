using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBotProfiles
{
    [Serializable]
    public class UniversalPlayProfile : Profile
    {
        private string _log = string.Empty;
        private const string ProfileVersion = "2026-03-23.4";
        private const Card.Cards TheCoin = Card.Cards.GAME_005;
        private const Card.Cards Innervate = Card.Cards.CORE_EX1_169;
        private const int DelayAttackValue = 9999;
        private const int PreferAttackOrderValue = -9999;
        private const int PreferAttackBodyValue = -2600;
        private const int DelayAttackBodyValue = 4200;
        private const int ForceEnemyAttackTargetValue = 9800;
        private const int BlockHeroAttackValue = 9999;
        private const int PreferHeroAttackValue = -2600;
        private const int FreshTeacherRefreshHoldSeconds = 8;
        private const int SignatureMismatchRelaxSourceValue = -9800;
        private const int FriendlyBoardLimit = 7;

        public ProfileParameters GetParameters(Board board)
        {
            _log = string.Empty;

            ProfileParameters p = new ProfileParameters(BaseProfile.Rush)
            {
                DiscoverSimulationValueThresholdPercent = -10
            };

            AddLog("============== 通用学习出牌 v" + ProfileVersion + " ==============");
            AddLog("配置=" + FormatVersionedProfileName(SafeCurrentProfileName()) + " | 牌组=" + SafeCurrentDeckName());

            if (board == null)
            {
                AddLog("[BOXOCR][APPLY][MISS] 当前棋盘不可用");
                FlushLog();
                return p;
            }

            AddLog(
                "对手=" + board.EnemyClass
                + " | 法力=" + board.ManaAvailable + "/" + board.MaxMana
                + " | 手牌=" + (board.Hand != null ? board.Hand.Count : 0)
                + " | 牌库=" + board.FriendDeckCount);

            ConfigureForcedResimulation(board, p);

            // 标准模式优先保住单文件盒子链路；外部执行器只作为可选增强。
            bool boxOcrBiasApplied = false;
            bool strongGuidanceApplied = false;
            try
            {
                boxOcrBiasApplied = TryApplyBoxOcrWeakBias(
                    p,
                    board,
                    AddLog,
                    SafeCurrentProfileName(),
                    out strongGuidanceApplied);
            }
            catch
            {
                boxOcrBiasApplied = false;
                strongGuidanceApplied = false;
            }

            if (boxOcrBiasApplied)
            {
                if (strongGuidanceApplied)
                {
                    AddLog("[BOXOCR][PRIMARY][OK] 已由单文件盒子链路接管（强引导）");
                }
                else
                {
                    AddLog("[BOXOCR][PRIMARY][OK] 已由单文件盒子链路接管（弱引导）");
                }
            }
            else
            {
                bool appliedByExecutor = TryApplyPlayExecutorCompat(board, p, AddLog);
                if (appliedByExecutor)
                {
                    AddLog("[PLAY][INTEGRATED][OK] 已由外部统一执行器接管");
                }
                else
                {
                    AddLog("[PLAY][INTEGRATED][OPTIONAL][MISS] 外部统一执行器未接管，单文件盒子链路也未命中，回退到基础逻辑");
                }
            }

            FlushLog();
            return p;
        }

        private void AddLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            if (!string.IsNullOrWhiteSpace(_log))
                _log += "\r\n";
            _log += line;
        }

        private void FlushLog()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_log))
                    Bot.Log(_log);
            }
            catch
            {
                // ignore
            }
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
            string[] candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "compilecheck_decisionplayexecutor", "bin", "Debug", "net8.0", "compilecheck_decisionplayexecutor.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "compilecheck_decisionplayexecutor", "bin", "Release", "net8.0", "compilecheck_decisionplayexecutor.dll")
            };

            foreach (string candidate in candidates)
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

            if (unique.Count > 0)
                AddLog("[PLAY][INFO] 强制重算卡数=" + unique.Count);
        }

        // 单文件 Profile 运行时不会自动并入 SupportSources，这里内置最小 play 状态模型以保证动态编译可用。
        private sealed class LocalBoxOcrState
        {
            public DateTime TimestampUtc = DateTime.MinValue;
            public string Status = string.Empty;
            public string Stage = string.Empty;
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
            public readonly HashSet<Card.Cards> RecommendedCards = new HashSet<Card.Cards>();
            public readonly HashSet<Card.Cards> RecommendedHeroPowers = new HashSet<Card.Cards>();
            public readonly List<string> TextHints = new List<string>();

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

                return string.Equals(BoardSignature.Trim(), expectedSignature.Trim(), StringComparison.Ordinal);
            }

            private static string NormalizeStrategyName(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return string.Empty;

                string value = raw.Trim().Replace('/', '\\');
                return Path.GetFileName(value).Trim();
            }
        }

        private static bool TryApplyBoxOcrWeakBias(ProfileParameters p, Board board, Action<string> addLog, string expectedProfileName, out bool strongGuidanceApplied)
        {
            strongGuidanceApplied = false;
            if (p == null || board == null)
                return false;

            BoxAuditGuardState boxAuditGuard = LoadBoxAuditGuardState();
            LocalBoxOcrState state = LoadLocalBoxOcrState();
            if (state == null)
            {
                if (addLog != null)
                    addLog("[BOXOCR][STATE][MISS] 未找到状态文件");
                return false;
            }

            if (!string.Equals(state.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                if (addLog != null)
                    addLog("[BOXOCR][STATE][MISS] 状态=" + DescribeBoxOcrStatusForLog(state.Status));
                return false;
            }

            if (!string.Equals(state.Stage, "play", StringComparison.OrdinalIgnoreCase))
            {
                if (addLog != null)
                    addLog("[BOXOCR][STATE][MISS] 阶段=" + DescribeBoxOcrStageForLog(state.Stage));
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
                        if (TryApplySignatureMismatchSafeBias(p, board, state, boxAuditGuard, out signatureRelaxSummary))
                        {
                            TryRefreshLocalBoxOcrBoardSignature(currentBoardSignature);
                            strongGuidanceApplied = true;
                            if (addLog != null)
                                addLog("[BOXOCR][STATE][RELAX] 棋盘签名不匹配，但已安全继续低风险动作 -> " + signatureRelaxSummary);
                            return true;
                        }

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

                if (!state.IsFresh(20))
                {
                    if (addLog != null)
                        addLog("[BOXOCR][STATE][MISS] 状态已过期 | 已过秒数=" + GetLocalBoxOcrStateAgeSeconds(state) + " | 阈值=20");
                    return false;
                }
            }
            else if (!state.IsFresh(12))
            {
                if (addLog != null)
                    addLog("[BOXOCR][STATE][MISS] 状态已过期 | 已过秒数=" + GetLocalBoxOcrStateAgeSeconds(state) + " | 阈值=12");
                return false;
            }

            bool teacherRecommendationPresent = HasTeacherActionRecommendation(board, state);
            if (!teacherRecommendationPresent)
            {
                if (addLog != null
                    && string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
                {
                    string sourceKind = DescribeBoxOcrSourceKindForLog(state.PreferredSourceKind);
                    string sourceId = state.PreferredSourceId != default(Card.Cards) ? state.PreferredSourceId.ToString() : "(无)";
                    addLog("[BOXOCR][ATTACK][STALE] 攻击推荐已失效 | 来源类型=" + sourceKind
                        + " | 来源ID=" + sourceId
                        + " | 来源槽位=" + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "(无)")
                        + " | 目标类型=" + (string.IsNullOrWhiteSpace(state.PreferredTargetKind) ? "(无)" : state.PreferredTargetKind)
                        + " | 目标ID=" + (state.PreferredTargetId != default(Card.Cards) ? state.PreferredTargetId.ToString() : "(无)")
                        + " | 目标槽位=" + (state.PreferredTargetSlot > 0 ? state.PreferredTargetSlot.ToString() : "(无)"));
                }

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
                    addLog("[BOXOCR][APPLY][FORCED] " + forcedSummary);
            }

            string strictSummary;
            if (CanStrictlyEnforceTeacherPlan(board, state, forcedApplied)
                && TryApplyTeacherOnlyPlanBias(p, board, state, forcedApplied, out strictSummary))
            {
                strongGuidanceApplied = true;
                if (addLog != null)
                    addLog("[BOXOCR][APPLY][STRICT] " + strictSummary);
                return true;
            }

            if (forcedApplied)
            {
                if (addLog != null)
                    addLog("[BOXOCR][APPLY][STRICT] 盒子首选动作已锁定");
                return true;
            }

            // OCR 文本里可能混入“结束回合”，但只要当前仍有立即动作，尤其是有场攻，就不能直接接管结束回合。
            if (state.EndTurnRecommended && !attackIntentPresent)
            {
                int endTurnManaNow;
                bool hasPlayableHandNow;
                bool canHeroPowerNow;
                bool hasAttackReadyNow;
                if (!CanAcceptBoxOcrEndTurn(board, out endTurnManaNow, out hasPlayableHandNow, out canHeroPowerNow, out hasAttackReadyNow))
                {
                    if (addLog != null)
                    {
                        addLog("[BOXOCR][STATE][ENDTURN_BLOCK] playableHand=" + (hasPlayableHandNow ? "1" : "0")
                            + " | heroPower=" + (canHeroPowerNow ? "1" : "0")
                            + " | attackReady=" + (hasAttackReadyNow ? "1" : "0")
                            + " | mana=" + endTurnManaNow.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    string endTurnSummary;
                    if (TryApplyBoxOcrEndTurnBias(p, board, out endTurnSummary))
                    {
                        strongGuidanceApplied = true;
                        if (addLog != null)
                            addLog("[BOXOCR][APPLY][ENDTURN] " + endTurnSummary);
                        return true;
                    }

                    if (addLog != null)
                        addLog("[BOXOCR][STATE][MISS] 结束回合引导应用失败");
                    return false;
                }
            }

            if (state.EndTurnRecommended && attackIntentPresent && addLog != null)
                addLog("[BOXOCR][STATE][HOLD] 检测到攻击意图，暂不接受结束回合提示");

            Card anchoredCard;
            string relaxSummary;
            if (TryApplyTextAnchoredRecommendedCardRelax(p, board, state, boxAuditGuard, out anchoredCard, out relaxSummary))
            {
                strongGuidanceApplied = true;
                if (addLog != null)
                    addLog("[BOXOCR][APPLY][RELAX] 精确映射不足，改用文本锚点强偏置 -> " + relaxSummary);
                return true;
            }

            if (addLog != null)
            {
                string preferredId = state.PreferredSourceId != default(Card.Cards)
                    ? state.PreferredSourceId.ToString()
                    : "(无)";
                string preferredTargetId = state.PreferredTargetId != default(Card.Cards)
                    ? state.PreferredTargetId.ToString()
                    : "(无)";
                addLog(
                    "[BOXOCR][STATE] 动作=" + DescribeBoxOcrActionTypeForLog(state.PreferredActionType)
                    + " | 来源类型=" + DescribeBoxOcrSourceKindForLog(state.PreferredSourceKind)
                    + " | 来源ID=" + preferredId
                    + " | 来源槽位=" + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "(无)")
                    + " | 执行方式=" + DescribeBoxOcrDeliveryForLog(state.PreferredDelivery)
                    + " | 场上槽位=" + (state.PreferredBoardSlot > 0 ? state.PreferredBoardSlot.ToString() : "(无)")
                    + " | 目标类型=" + (string.IsNullOrWhiteSpace(state.PreferredTargetKind) ? "(无)" : state.PreferredTargetKind)
                    + " | 目标ID=" + preferredTargetId
                    + " | 目标槽位=" + (state.PreferredTargetSlot > 0 ? state.PreferredTargetSlot.ToString() : "(无)")
                    + " | 选项=" + (state.PreferredChoiceIndex > 0 ? state.PreferredChoiceIndex.ToString() : "(无)")
                    + " | 推荐卡牌数=" + state.RecommendedCards.Count
                    + " | 推荐技能数=" + state.RecommendedHeroPowers.Count);
            }

            // 盒子已经给了动作时，优先挡住默认逻辑抢跑，等下一拍刷新后再精确落地。
            if (TryApplyFreshTeacherRefreshHoldOnMiss(
                p,
                board,
                state,
                addLog,
                "盒子有动作但当前映射未落地，阻止默认逻辑等待刷新",
                true,
                "mapping_not_ready"))
            {
                strongGuidanceApplied = true;
                return true;
            }

            if (addLog != null)
                addLog("[BOXOCR][STATE][MISS] 盒子有动作，但当前无法精确映射，回退默认逻辑");

            return false;
        }

        private static bool TryApplyBoxOcrEndTurnBias(ProfileParameters p, Board board, out string summary)
        {
            summary = "OCR建议结束回合";
            if (p == null || board == null)
                return false;

            int manaNow;
            bool hasPlayableHandNow;
            bool canHeroPowerNow;
            bool hasAttackReadyNow;
            if (!CanAcceptBoxOcrEndTurn(board, out manaNow, out hasPlayableHandNow, out canHeroPowerNow, out hasAttackReadyNow))
            {
                summary = "OCR建议结束回合，但当前仍有立即动作 | 手牌=" + (hasPlayableHandNow ? "1" : "0")
                    + " | 技能=" + (canHeroPowerNow ? "1" : "0")
                    + " | 场攻=" + (hasAttackReadyNow ? "1" : "0")
                    + " | 法力=" + manaNow.ToString(CultureInfo.InvariantCulture);
                return false;
            }

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
                    p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Template.Id, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(board.Ability.Template.Id, new Modifier(-9999));
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

            try
            {
                if (p.GlobalAggroModifier != null)
                    p.GlobalAggroModifier.Value = Math.Min(p.GlobalAggroModifier.Value, -3600);
                else
                    p.GlobalAggroModifier = new Modifier(-3600);
                appliedAny = true;
            }
            catch
            {
                // ignore
            }

            try
            {
                if (p.GlobalDefenseModifier != null)
                    p.GlobalDefenseModifier.Value = Math.Max(p.GlobalDefenseModifier.Value, 1800);
                else
                    p.GlobalDefenseModifier = new Modifier(1800);
                appliedAny = true;
            }
            catch
            {
                // ignore
            }

            summary = "OCR建议结束回合 | 手牌已封锁=" + blockedHandActions + " | 场攻已保留=" + heldBoardAttackers;
            return appliedAny;
        }

        private static bool CanAcceptBoxOcrEndTurn(
            Board board,
            out int manaNow,
            out bool hasPlayableHandNow,
            out bool canHeroPowerNow,
            out bool hasAttackReadyNow)
        {
            manaNow = GetAvailableManaIncludingCoin(board);
            hasPlayableHandNow = HasPlayableHandActionNow(board, manaNow);
            canHeroPowerNow = CanUseHeroPowerNow(board, manaNow);
            hasAttackReadyNow = HasAnyAttackReady(board);
            return !hasPlayableHandNow && !canHeroPowerNow && !hasAttackReadyNow;
        }

        private static int GetAvailableManaIncludingCoin(Board board)
        {
            if (board == null)
                return 0;

            int mana = Math.Max(0, board.ManaAvailable);
            if (board.Hand != null && board.Hand.Exists(card => card != null && card.Template != null && card.Template.Id == TheCoin))
                return mana + 1;

            return mana;
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

            return board.Ability.CurrentCost <= Math.Max(0, availableMana);
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
                    return "英雄技能";
                case "attack":
                    return "攻击";
                case "endturn":
                    return "结束回合";
                default:
                    return raw.Trim();
            }
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

            return raw.IndexOf("结束回合", StringComparison.OrdinalIgnoreCase) >= 0
                || raw.IndexOf("結束回合", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(raw.Trim(), "end_turn", StringComparison.OrdinalIgnoreCase);
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

        private static bool TryApplyBoxOcrPreferredStepBias(ProfileParameters p, Board board, LocalBoxOcrState state, out string summary)
        {
            summary = string.Empty;
            if (p == null || board == null || state == null)
                return false;

            if (string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
                return TryApplyBoxOcrPreferredAttackBias(p, board, state, out summary);

            bool expectsTarget = HasPreferredTarget(state);
            Card preferredTarget = expectsTarget ? FindPreferredTarget(board, state) : null;
            if (expectsTarget && (preferredTarget == null || preferredTarget.Template == null))
                return false;

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
                        summary = BuildCardDisplayName(preferredLocation) + " slot=" + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "?");
                        return true;
                    }

                    if (state.PreferredSourceId != default(Card.Cards))
                    {
                        p.LocationsModifiers.AddOrUpdate(state.PreferredSourceId, new Modifier(-9800));
                        p.PlayOrderModifiers.AddOrUpdate(state.PreferredSourceId, new Modifier(9999));
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
                        && board.Ability.Template.Id == state.PreferredSourceId)
                    {
                        if (preferredTarget != null
                            && TryApplyExactHeroPowerModifier(p, board.Ability.Template.Id, preferredTarget.Id, preferredTarget.Template.Id, -9800))
                        {
                            summary = "英雄技能(" + board.Ability.Template.Id + ") -> " + BuildCardDisplayName(preferredTarget);
                            return true;
                        }

                        p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Template.Id, new Modifier(-9800));
                        p.PlayOrderModifiers.AddOrUpdate(board.Ability.Template.Id, new Modifier(9999));
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
                return false;

            if (preferredCard.CurrentCost > board.ManaAvailable)
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
                summary = BuildCardDisplayName(preferredCard)
                    + " -> "
                    + BuildCardDisplayName(preferredTarget)
                    + " 槽位="
                    + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "?")
                    + (preferredChoiceIndex > 0 ? " | 选项=" + preferredChoiceIndex : string.Empty);
                return true;
            }

            ApplyCardBias(p, preferredCard, -9800, -9800, 9999, 9999);
            summary = BuildCardDisplayName(preferredCard)
                + " 槽位="
                + (state.PreferredSourceSlot > 0 ? state.PreferredSourceSlot.ToString() : "?")
                + (preferredChoiceIndex > 0 ? " | 选项=" + preferredChoiceIndex : string.Empty);
            return true;
        }

        private static bool TryApplyBoxOcrPreferredAttackBias(ProfileParameters p, Board board, LocalBoxOcrState state, out string summary)
        {
            summary = string.Empty;
            if (p == null || board == null || state == null)
                return false;

            Card target = FindPreferredTarget(board, state);
            if (target == null)
                return false;

            bool heroAttack = string.Equals(state.PreferredSourceKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "own_hero", StringComparison.OrdinalIgnoreCase);

            Card attackSource = heroAttack
                ? board.HeroFriend
                : FindPreferredFriendlyMinion(board, state.PreferredSourceId, state.PreferredSourceSlot);

            if (attackSource == null || attackSource.Template == null)
                return false;

            SuppressNonAttackActionsForForcedAttack(board, p);
            if (!ConfigureForcedAttackSource(board, p, attackSource, heroAttack))
                return false;

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
                    p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Template.Id, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(board.Ability.Template.Id, new Modifier(-9999));
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
            if (board == null || p == null || board.HeroFriend == null)
                return;

            try
            {
                p.GlobalWeaponsAttackModifier = new Modifier(BlockHeroAttackValue);
                p.WeaponsAttackModifiers.AddOrUpdate(board.HeroFriend.Id, new Modifier(BlockHeroAttackValue));
                if (board.WeaponFriend != null && board.WeaponFriend.Template != null)
                    p.WeaponsAttackModifiers.AddOrUpdate(board.WeaponFriend.Template.Id, new Modifier(BlockHeroAttackValue));
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

            if (state.PreferredSourceSlot > 0 && state.PreferredSourceSlot <= board.Hand.Count)
            {
                Card bySlot = board.Hand[state.PreferredSourceSlot - 1];
                if (bySlot != null
                    && bySlot.Template != null
                    && (state.PreferredSourceId == default(Card.Cards)
                        || bySlot.Template.Id == state.PreferredSourceId
                        || ScoreCardAgainstPlayTextHints(bySlot, state) > 0))
                {
                    return bySlot;
                }
            }

            foreach (Card card in board.Hand)
            {
                if (card == null || card.Template == null)
                    continue;

                if (card.Template.Id == state.PreferredSourceId)
                    return card;
            }

            return FindBestTextMatchedCard(board.Hand, state, null);
        }

        private static bool HasTeacherActionRecommendation(Board board, LocalBoxOcrState state)
        {
            if (state == null)
                return false;

            if (HasTeacherAttackIntent(state))
                return true;

            if (state.EndTurnRecommended)
                return true;

            if (string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase))
                return HasExecutableTeacherAttackRecommendation(board, state);

            if (!string.IsNullOrWhiteSpace(state.PreferredActionType))
                return true;

            if (!string.IsNullOrWhiteSpace(state.PreferredSourceKind))
                return true;

            if (state.PreferredSourceId != default(Card.Cards))
                return true;

            if (state.PreferredSourceSlot > 0
                || state.PreferredChoiceIndex > 0
                || state.PreferredTargetSlot > 0
                || !string.IsNullOrWhiteSpace(state.PreferredTargetKind)
                || state.PreferredTargetId != default(Card.Cards))
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
                || state.PreferredSourceId != default(Card.Cards)
                || state.PreferredSourceSlot > 0;
            bool hasTarget = !string.IsNullOrWhiteSpace(state.PreferredTargetKind)
                || state.PreferredTargetId != default(Card.Cards)
                || state.PreferredTargetSlot > 0;
            return hasSource && hasTarget;
        }

        private static bool HasExecutableTeacherAttackRecommendation(Board board, LocalBoxOcrState state)
        {
            if (board == null || state == null)
                return false;

            if (string.Equals(state.PreferredSourceKind, "friendly_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "own_hero", StringComparison.OrdinalIgnoreCase))
            {
                return CanFriendlyHeroAttack(board);
            }

            Card preferredSource = FindPreferredFriendlyMinion(board, state.PreferredSourceId, state.PreferredSourceSlot);
            if (preferredSource == null || preferredSource.Template == null)
                return false;

            // 攻击指令只有在来源当前真的能攻击时才算有效推荐，避免把已攻击/不可攻击的随从误当成强引导而直接空过。
            return preferredSource.CanAttack && preferredSource.CurrentAtk > 0;
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

            if (!board.IsOwnTurn || !state.IsFresh(FreshTeacherRefreshHoldSeconds))
                return false;

            if (!HasFreshTeacherSignal(state))
                return false;

            ApplyHoldForTeacherRefresh(board, p, holdAttacks);
            if (addLog != null)
            {
                addLog(
                    "[BOXOCR][STATE][HOLD] 棋盘签名不匹配，但上一拍OCR仍新鲜，阻止默认逻辑等待刷新"
                    + " | 原因=" + (string.IsNullOrWhiteSpace(holdReasonCode) ? "signature_mismatch" : holdReasonCode)
                    + " | 拦攻击=" + (holdAttacks ? "是" : "否")
                    + " | 已过秒数=" + GetLocalBoxOcrStateAgeSeconds(state));
            }

            return true;
        }

        private static bool TryApplyFreshTeacherRefreshHoldOnMiss(
            ProfileParameters p,
            Board board,
            LocalBoxOcrState state,
            Action<string> addLog,
            string reason,
            bool holdAttacks,
            string holdReasonCode)
        {
            if (p == null || board == null || state == null)
                return false;

            if (!board.IsOwnTurn || !state.IsFresh(FreshTeacherRefreshHoldSeconds))
                return false;

            if (!HasFreshTeacherSignal(state))
                return false;

            ApplyHoldForTeacherRefresh(board, p, holdAttacks);
            if (addLog != null)
            {
                addLog(
                    "[BOXOCR][STATE][HOLD] "
                    + (string.IsNullOrWhiteSpace(reason) ? "上一拍OCR仍新鲜，阻止默认逻辑等待刷新" : reason)
                    + " | 原因=" + (string.IsNullOrWhiteSpace(holdReasonCode) ? "refresh_wait" : holdReasonCode)
                    + " | 拦攻击=" + (holdAttacks ? "是" : "否")
                    + " | 已过秒数=" + GetLocalBoxOcrStateAgeSeconds(state));
            }

            return true;
        }

        private static bool HasFreshTeacherSignal(LocalBoxOcrState state)
        {
            if (state == null)
                return false;

            if (state.EndTurnRecommended)
                return true;

            if (!string.IsNullOrWhiteSpace(state.PreferredActionType)
                || !string.IsNullOrWhiteSpace(state.PreferredSourceKind)
                || state.PreferredSourceId != default(Card.Cards)
                || state.PreferredSourceSlot > 0
                || state.PreferredChoiceIndex > 0
                || !string.IsNullOrWhiteSpace(state.PreferredTargetKind)
                || state.PreferredTargetId != default(Card.Cards)
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
                string.Equals(state.PreferredActionType, "HeroPower", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase)
                || state.RecommendedHeroPowers.Count > 0;
            bool locationPlan =
                string.Equals(state.PreferredActionType, "UseLocation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "friendly_location", StringComparison.OrdinalIgnoreCase);

            if (attackPlan)
            {
                if (forcedApplied)
                {
                    summary = "仅保留盒子攻击动作";
                    return true;
                }

                ApplyHoldForTeacherRefresh(board, p, true);
                summary = "盒子要求攻击，但当前无法精确落地，已阻止默认逻辑等待刷新";
                return true;
            }

            DelayAllAttacksForTeacher(board, p);

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

            if (board.Hand != null)
            {
                foreach (Card card in board.Hand)
                {
                    if (card == null || card.Template == null)
                        continue;

                    bool preferred = IsTeacherPreferredHandCard(state, card)
                        || (preferredCard != null && preferredCard.Id == card.Id);
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
                    && (state.RecommendedHeroPowers.Count == 0
                        || state.RecommendedHeroPowers.Contains(board.Ability.Template.Id)
                        || state.PreferredSourceId == default(Card.Cards)
                        || state.PreferredSourceId == board.Ability.Template.Id);
                if (preferredHeroPower)
                {
                    p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Template.Id, new Modifier(-9800));
                    p.PlayOrderModifiers.AddOrUpdate(board.Ability.Template.Id, new Modifier(9999));
                    heroPowerApplied = true;
                }
                else
                {
                    SuppressHeroPowerForTeacher(p, board);
                    heroPowerBlocked = true;
                }
            }

            if (board.MinionFriend != null)
            {
                Card preferredLocation = locationPlan
                    ? FindPreferredFriendlyLocation(board, state.PreferredSourceId, state.PreferredSourceSlot)
                    : null;
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

            if (forcedApplied)
            {
                summary = "仅保留盒子动作 | 已封锁非推荐动作";
                return true;
            }

            if (allowedHand > 0 || heroPowerApplied || locationApplied)
            {
                summary = "仅保留盒子动作 | 手牌=" + allowedHand
                    + " | 技能=" + (heroPowerApplied ? "1" : "0")
                    + " | 地标=" + (locationApplied ? "1" : "0")
                    + " | 已封锁手牌=" + blockedHand
                    + " | 已封锁技能=" + (heroPowerBlocked ? "1" : "0")
                    + " | 已封锁地标=" + blockedLocations;
                return true;
            }

            ApplyHoldForTeacherRefresh(board, p, true);
            summary = "盒子有推荐动作，但当前无法建立可执行映射，已阻止默认逻辑等待刷新";
            return true;
        }

        private static bool CanStrictlyEnforceTeacherPlan(Board board, LocalBoxOcrState state, bool forcedApplied)
        {
            if (forcedApplied)
                return true;

            if (board == null || state == null)
                return false;

            bool attackPlan = string.Equals(state.PreferredActionType, "Attack", StringComparison.OrdinalIgnoreCase);
            if (attackPlan)
                return false;

            bool heroPowerPlan =
                string.Equals(state.PreferredActionType, "HeroPower", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase)
                || state.RecommendedHeroPowers.Count > 0;
            if (heroPowerPlan
                && board.Ability != null
                && board.Ability.Template != null
                && (state.RecommendedHeroPowers.Count == 0
                    || state.RecommendedHeroPowers.Contains(board.Ability.Template.Id)
                    || state.PreferredSourceId == default(Card.Cards)
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

            return preferredCard != null
                && preferredCard.Template != null
                && preferredCard.CurrentCost <= board.ManaAvailable;
        }

        private static void ApplyHoldForTeacherRefresh(Board board, ProfileParameters p, bool holdAttacks)
        {
            if (board == null || p == null)
                return;

            DelayNonAttackActionsForTeacher(board, p);
            if (holdAttacks)
                DelayAllAttacksForTeacher(board, p);
        }

        private static void DelayNonAttackActionsForTeacher(Board board, ProfileParameters p)
        {
            if (board == null || p == null)
                return;

            if (board.Hand != null)
            {
                foreach (Card card in board.Hand)
                {
                    if (card == null || card.Template == null)
                        continue;

                    BlockHandCardForTeacher(p, card);
                }
            }

            SuppressHeroPowerForTeacher(p, board);
        }

        private static void DelayAllAttacksForTeacher(Board board, ProfileParameters p)
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

                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-2200));
                    }
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
                        if (friend == null || friend.Template == null || !friend.CanAttack)
                            continue;

                        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                        p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(DelayAttackValue));
                    }
                }
            }
            catch
            {
                // ignore
            }

            BlockHeroAttack(board, p);
            p.GlobalAggroModifier = new Modifier(-2200);
        }

        private static void SuppressHeroPowerForTeacher(ProfileParameters p, Board board)
        {
            if (p == null || board == null || board.Ability == null || board.Ability.Template == null)
                return;

            p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Template.Id, new Modifier(9800));
            p.PlayOrderModifiers.AddOrUpdate(board.Ability.Template.Id, new Modifier(-9999));
        }

        private static void BlockHandCardForTeacher(ProfileParameters p, Card card)
        {
            if (p == null || card == null || card.Template == null)
                return;

            ApplyCardBias(p, card, 9800, 9800, -9999, -9999);
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

            if (anchoredCard.CurrentCost > board.ManaAvailable)
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
                string.Equals(state.PreferredActionType, "HeroPower", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.PreferredSourceKind, "hero_power", StringComparison.OrdinalIgnoreCase)
                || state.RecommendedHeroPowers.Count > 0;
            if (heroPowerPlan)
            {
                if (board.Ability == null || board.Ability.Template == null)
                    return false;

                bool heroPowerMatches = state.RecommendedHeroPowers.Count == 0
                    || state.RecommendedHeroPowers.Contains(board.Ability.Template.Id)
                    || state.PreferredSourceId == default(Card.Cards)
                    || state.PreferredSourceId == board.Ability.Template.Id;
                if (!heroPowerMatches)
                    return false;

                Card heroPowerTarget = HasPreferredTarget(state) ? FindPreferredTarget(board, state) : null;
                if (HasPreferredTarget(state) && (heroPowerTarget == null || heroPowerTarget.Template == null))
                    return false;

                if (heroPowerTarget != null
                    && TryApplyExactHeroPowerModifier(p, board.Ability.Template.Id, heroPowerTarget.Id, heroPowerTarget.Template.Id, SignatureMismatchRelaxSourceValue))
                {
                    p.PlayOrderModifiers.AddOrUpdate(board.Ability.Template.Id, new Modifier(9999));
                    summary = "英雄技能=" + board.Ability.Template.Id + " -> " + BuildCardDisplayName(heroPowerTarget);
                    return true;
                }

                p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Template.Id, new Modifier(SignatureMismatchRelaxSourceValue));
                p.PlayOrderModifiers.AddOrUpdate(board.Ability.Template.Id, new Modifier(9999));
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

            if (preferredCard.Template == null || preferredCard.CurrentCost > board.ManaAvailable)
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

            if (state.PreferredSourceId != default(Card.Cards))
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

            return state.PreferredSourceId != default(Card.Cards)
                && card.Template.Id == state.PreferredSourceId;
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

            return HasActionablePlayStateAnchorOnCurrentBoard(board, state);
        }

        private static bool HasActionablePlayStateAnchorOnCurrentBoard(Board board, LocalBoxOcrState state)
        {
            if (board == null || state == null)
                return false;

            if (FindPreferredHandCard(board, state) != null)
                return true;

            if (FindPreferredFriendlyLocation(board, state.PreferredSourceId, state.PreferredSourceSlot) != null)
                return true;

            if (FindPreferredFriendlyMinion(board, state.PreferredSourceId, state.PreferredSourceSlot) != null)
                return true;

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

            try
            {
                if (board.Hand != null && state.RecommendedCards.Count > 0)
                {
                    foreach (Card card in board.Hand)
                    {
                        if (card == null || card.Template == null)
                            continue;

                        if (state.RecommendedCards.Contains(card.Template.Id))
                            return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.Ability != null
                    && board.Ability.Template != null
                    && state.RecommendedHeroPowers.Contains(board.Ability.Template.Id))
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            if (HasPreferredTarget(state) && FindPreferredTarget(board, state) != null)
                return true;

            if (HasTextualPlayStateAnchorOnCurrentBoard(board, state))
                return true;

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
            if (state == null || preferredCard == null || preferredCard.Template == null)
                return 0;

            if (state.PreferredChoiceIndex > 0)
                return state.PreferredChoiceIndex;

            // Living Roots: when OCR only says "play Living Roots" and does not provide
            // a target or explicit choice, choosing option 2 is safer than falling back
            // to SmartBot's default option 1.
            if (preferredCard.Template.Id == Card.Cards.CORE_AT_037 && preferredTarget == null)
                return 2;

            return 0;
        }

        private static void ApplyChoiceBias(ProfileParameters p, Card.Cards sourceId, int choiceIndex, int value)
        {
            if (p == null || sourceId == default(Card.Cards) || choiceIndex <= 0)
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

            if (sourceSlot > 0 && sourceSlot <= board.MinionFriend.Count)
            {
                Card bySlot = board.MinionFriend[sourceSlot - 1];
                if (IsLocationCard(bySlot) && CardMatches(bySlot, sourceId))
                    return bySlot;
            }

            for (int i = 0; i < board.MinionFriend.Count; i++)
            {
                Card card = board.MinionFriend[i];
                if (IsLocationCard(card) && CardMatches(card, sourceId))
                    return card;
            }

            if (sourceId == default(Card.Cards))
                return null;

            return FindCardByIdentity(board.MinionFriend, sourceId, sourceSlot);
        }

        private static bool HasPreferredTarget(LocalBoxOcrState state)
        {
            if (state == null)
                return false;

            return !string.IsNullOrWhiteSpace(state.PreferredTargetKind)
                || state.PreferredTargetId != default(Card.Cards)
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

            if (slot > 0 && slot <= cards.Count)
            {
                Card bySlot = cards[slot - 1];
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

            return null;
        }

        private static bool CardMatches(Card card, Card.Cards cardId)
        {
            if (card == null || card.Template == null)
                return false;

            return cardId == default(Card.Cards) || card.Template.Id == cardId;
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

        private static LocalBoxOcrState LoadLocalBoxOcrState()
        {
            string path = ResolveBoxOcrStatePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new LocalBoxOcrState();

            try
            {
                string[] lines = File.ReadAllLines(path);
                LocalBoxOcrState state = new LocalBoxOcrState();
                string pendingMatchKind = string.Empty;
                Card.Cards pendingMatchId = default(Card.Cards);
                int pendingMatchSlot = 0;
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
                        if (string.IsNullOrWhiteSpace(state.PreferredActionType))
                            state.PreferredActionType = value;
                        if (!state.EndTurnRecommended && ContainsEndTurnHint(value))
                            state.EndTurnRecommended = true;
                    }
                    else if (string.Equals(key, "action_step_index", StringComparison.OrdinalIgnoreCase))
                    {
                        if (state.PreferredStepIndex <= 0)
                        {
                            int stepIndex;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out stepIndex) && stepIndex > 0)
                                state.PreferredStepIndex = stepIndex;
                        }
                    }
                    else if (string.Equals(key, "action_source_kind", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(state.PreferredSourceKind))
                            state.PreferredSourceKind = value;
                    }
                    else if (string.Equals(key, "action_source_id", StringComparison.OrdinalIgnoreCase))
                    {
                        if (state.PreferredSourceId == default(Card.Cards))
                        {
                            Card.Cards actionSourceId;
                            if (TryParseCardId(value, out actionSourceId))
                                state.PreferredSourceId = actionSourceId;
                        }
                    }
                    else if (string.Equals(key, "action_source_slot", StringComparison.OrdinalIgnoreCase))
                    {
                        if (state.PreferredSourceSlot <= 0)
                        {
                            int slot;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) && slot > 0)
                                state.PreferredSourceSlot = slot;
                        }
                    }
                    else if (string.Equals(key, "action_choice_index", StringComparison.OrdinalIgnoreCase))
                    {
                        if (state.PreferredChoiceIndex <= 0)
                        {
                            int choiceIndex;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out choiceIndex) && choiceIndex > 0)
                                state.PreferredChoiceIndex = choiceIndex;
                        }
                    }
                    else if (string.Equals(key, "action_delivery", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(state.PreferredDelivery))
                            state.PreferredDelivery = value;
                    }
                    else if (string.Equals(key, "action_board_slot", StringComparison.OrdinalIgnoreCase))
                    {
                        if (state.PreferredBoardSlot <= 0)
                        {
                            int boardSlot;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out boardSlot) && boardSlot > 0)
                                state.PreferredBoardSlot = boardSlot;
                        }
                    }
                    else if (string.Equals(key, "action_required", StringComparison.OrdinalIgnoreCase))
                    {
                        state.PreferredActionRequired = string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(key, "action_target_kind", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(state.PreferredTargetKind))
                            state.PreferredTargetKind = value;
                    }
                    else if (string.Equals(key, "action_target_id", StringComparison.OrdinalIgnoreCase))
                    {
                        if (state.PreferredTargetId == default(Card.Cards))
                        {
                            Card.Cards targetId;
                            if (TryParseCardId(value, out targetId))
                                state.PreferredTargetId = targetId;
                        }
                    }
                    else if (string.Equals(key, "action_target_slot", StringComparison.OrdinalIgnoreCase))
                    {
                        if (state.PreferredTargetSlot <= 0)
                        {
                            int targetSlot;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out targetSlot) && targetSlot > 0)
                                state.PreferredTargetSlot = targetSlot;
                        }
                    }
                }

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
                    && state.PreferredSourceId == default(Card.Cards)
                    && state.PreferredTargetId == default(Card.Cards)
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

        private static string ResolveBoxOcrStatePath()
        {
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
                            return normalized;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                return Path.GetFullPath(candidates[0]);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryRefreshLocalBoxOcrBoardSignature(string currentBoardSignature)
        {
            if (string.IsNullOrWhiteSpace(currentBoardSignature))
                return false;

            string path = ResolveBoxOcrStatePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                string[] lines = File.ReadAllLines(path);
                const string prefix = "sb_board_sig=";
                bool found = false;
                bool changed = false;
                List<string> updated = new List<string>(lines.Length + 1);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] ?? string.Empty;
                    if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string normalized = prefix + currentBoardSignature;
                        updated.Add(normalized);
                        found = true;
                        if (!string.Equals(line, normalized, StringComparison.Ordinal))
                            changed = true;
                    }
                    else
                    {
                        updated.Add(line);
                    }
                }

                if (!found)
                {
                    updated.Add(prefix + currentBoardSignature);
                    changed = true;
                }

                if (!changed)
                    return true;

                File.WriteAllLines(path, updated.ToArray());
                return true;
            }
            catch
            {
                return false;
            }
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

        private static string ResolveBoxAuditGuardPath()
        {
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
                            return normalized;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                return Path.GetFullPath(candidates[0]);
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
                string[] lines = File.ReadAllLines(path);
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
