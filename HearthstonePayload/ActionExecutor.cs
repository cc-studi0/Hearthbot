using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BotMain;

namespace HearthstonePayload
{
    /// <summary>
    /// 在炉石进程内执行操作（打牌、攻击、结束回合）
    /// 通过鼠标模拟执行游戏操作，不依赖内部API
    /// 协议格式: "类型|源EntityId|目标EntityId|位置"
    /// </summary>
    public static class ActionExecutor
    {
        private const int ClickScreenTimeoutMs = 1500;
        private const int DiscoverChoiceReadyTimeoutMs = 2000;
        private const int DiscoverChoiceReadyPollMs = 40;
        private static Assembly _asm;
        private static Type _gameStateType;
        private static Type _entityType;
        private static Type _networkType;
        private static Type _inputMgrType;
        private static Type _connectApiType;
        private static Type _choiceCardMgrType;
        private static Type _iTweenType;
        private static readonly string[] ChoiceTextMemberNames =
        {
            "TextCN",
            "Text",
            "DescriptionCN",
            "Description",
            "CardTextCN",
            "CardText",
            "CardTextInHandCN",
            "CardTextInHand",
            "GetText",
            "GetDescription",
            "GetCardText",
            "GetCardTextInHand"
        };

        private static CoroutineExecutor _coroutine;

        private sealed class ChoiceSnapshot
        {
            public string SnapshotId = string.Empty;
            public int ChoiceId;
            public int SourceEntityId;
            public string SourceCardId = string.Empty;
            public string RawChoiceType = string.Empty;
            public string Mode = "GENERAL";
            public int CountMin;
            public int CountMax;
            public bool InChoiceMode;
            public bool PacketReady;
            public bool ChoiceStateActive;
            public bool ChoiceStateWaitingToStart;
            public bool ChoiceStateRevealed;
            public bool ChoiceStateConcealed;
            public bool UiShown;
            public bool IsSubOptionChoice;
            public bool IsTitanAbility;
            public bool IsRewindChoice;
            public bool IsMagicItemDiscover;
            public bool IsShopChoice;
            public bool IsLaunchpadAbility;
            public string Signature = string.Empty;
            public List<string> ChoiceParts = new List<string>();
            public List<int> ChoiceEntityIds = new List<int>();
            public List<string> ChoiceCardIds = new List<string>();
            public List<int> ChosenEntityIds = new List<int>();
        }

        private enum ChoiceExecutionMechanismKind
        {
            Unknown = 0,
            EntityChoice = 1,
            SubOptionChoice = 2
        }

        /// <summary>
        /// 初始化协程执行器引用
        /// </summary>
        private sealed class FriendlyDrawGateState
        {
            public bool Active;
            public object Card;
            public int EntityId;
            public int FallbackReleaseTick;
            public int RecentReleasedEntityId;
            public object RecentReleasedCard;
            public int RecentReleasedUntilTick;
        }

        private sealed class HeroEnchantmentBusyObservationState
        {
            public string Signature = string.Empty;
            public int FirstSeenTick;
        }

        private sealed class HeroEnchantmentBusySnapshot
        {
            public string Signature = string.Empty;
            public string Detail = string.Empty;
            public int PlayerEnchantmentCount;
            public int HeroEnchantmentCount;
        }

        private const int FriendlyDrawFallbackDelayMs = 420;
        private const int FriendlyDrawRecentGuardWindowMs = 2500;
        private const int HeroEnchantmentBusyStableDelayMs = 180;
        private const int ChoiceDecisionTraceDedupWindowMs = 1200;
        private const int PendingOptionTargetSourceWindowMs = 5000;
        private static readonly object _friendlyDrawGateSync = new object();
        private static readonly FriendlyDrawGateState _friendlyDrawGate = new FriendlyDrawGateState();
        private static readonly object _heroEnchantmentBusySync = new object();
        private static readonly HeroEnchantmentBusyObservationState _heroEnchantmentBusy = new HeroEnchantmentBusyObservationState();
        private static readonly object _choiceDecisionTraceSync = new object();
        private static readonly object _pendingOptionTargetSourceSync = new object();
        private static readonly object _humanizerConfigSync = new object();
        private static readonly object _humanizeRandomSync = new object();
        private static string _lastChoiceDecisionTrace = string.Empty;
        private static int _lastChoiceDecisionTraceTick;
        private static int _pendingOptionTargetSourceEntityId;
        private static int _pendingOptionTargetSourceUntilTick;
        private static HumanizerConfig _humanizerConfig = new HumanizerConfig();
        private static readonly Random _humanizeRandom = new Random();

        public static void Init(CoroutineExecutor executor)
        {
            _coroutine = executor;
            ChoiceController.Init(executor);
        }

        public static string SetHumanizerConfig(string payload)
        {
            if (!HumanizerProtocol.TryParse(payload, out var config))
                return "FAIL:HUMANIZER_CONFIG:parse";

            lock (_humanizerConfigSync)
            {
                _humanizerConfig = config ?? new HumanizerConfig();
            }

            return "OK:HUMANIZER_CONFIG:" + HumanizerProtocol.GetIntensityToken(_humanizerConfig.Intensity);
        }

        private static HumanizerConfig GetHumanizerConfigSnapshot()
        {
            lock (_humanizerConfigSync)
            {
                return new HumanizerConfig
                {
                    Enabled = _humanizerConfig.Enabled,
                    Intensity = _humanizerConfig.Intensity
                };
            }
        }

        private static bool IsConstructedHumanizerEnabled()
        {
            return GetHumanizerConfigSnapshot().Enabled;
        }

        private static int NextHumanizeInt32(int minInclusive, int maxInclusive)
        {
            if (maxInclusive <= minInclusive)
                return minInclusive;

            lock (_humanizeRandomSync)
            {
                return _humanizeRandom.Next(minInclusive, maxInclusive + 1);
            }
        }

        private static double NextHumanizeDouble(double minInclusive, double maxInclusive)
        {
            if (maxInclusive <= minInclusive)
                return minInclusive;

            lock (_humanizeRandomSync)
            {
                return minInclusive + (_humanizeRandom.NextDouble() * (maxInclusive - minInclusive));
            }
        }

        private static string ExecuteOption(int sourceId, int targetId, int position, string subOptionCardId)
        {
            if (sourceId <= 0)
                return "FAIL:OPTION:source_invalid";

            var needsStructuredOption =
                targetId > 0
                || position > 0
                || !string.IsNullOrWhiteSpace(subOptionCardId);

            if (!needsStructuredOption)
            {
                var pendingSourceEntityId = GetPendingOptionTargetSourceEntityId();
                if (WaitForPendingOptionTargetReady(pendingSourceEntityId, 3000))
                {
                    var targetClickResult = TryClickPendingTarget(sourceId, pendingSourceEntityId);
                    if (targetClickResult != null)
                    {
                        if (targetClickResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                            ClearPendingOptionTargetSource("target_click_confirmed");
                        return targetClickResult;
                    }
                }

                ClearPendingOptionTargetSource("target_mode_not_ready");
                return "FAIL:OPTION:target_mode_not_ready:" + sourceId;
            }

            var submitDetail = TrySubmitStructuredOption(sourceId, targetId, position, subOptionCardId);
            if (submitDetail == null)
                return "OK:OPTION:network:" + sourceId;

            // 构筑抉择类指向法术（例如 活体根须）在运行时顺序是：
            // 打出卡牌 -> 选择子选项 -> 再选择目标。
            // 这类场景把 subOption + target 一次性提交通常会失败，
            // 因为目标列表只有在子选项确定后才会真正出现。
            if (targetId > 0
                && position <= 0
                && !string.IsNullOrWhiteSpace(subOptionCardId))
            {
                var subOptionThenTargetResult = TryExecuteSubOptionThenTarget(sourceId, targetId, position, subOptionCardId);
                if (subOptionThenTargetResult != null)
                    return subOptionThenTargetResult;
            }

            // 二选一 / 子选项路径：等待 EntityChoices 包 或
            // ChoiceCardMgr 的 SubOption UI 出现，然后解析并点击/提交。
            if (targetId <= 0
                && position <= 0
                && !string.IsNullOrWhiteSpace(subOptionCardId))
            {
                // 先检查 SubOption UI 是否已经打开（Choose One 打出后自动弹出的情况）
                if (WaitForSubOptionUiReady(800))
                {
                    var chooseOneResult = TryResolveAndClickSubOption(sourceId, subOptionCardId);
                    if (chooseOneResult != null)
                    {
                        RememberPendingOptionTargetSource(sourceId, "suboption_inline");
                        return chooseOneResult;
                    }
                }

                // SubOption UI 未自动弹出 —— 尝试泰坦/锻造等需要先点击实体才弹出选择 UI 的路径
                var titanResult = TryExecuteTitanStyleOption(sourceId, subOptionCardId);
                if (titanResult != null)
                {
                    RememberPendingOptionTargetSource(sourceId, "suboption_titan");
                    return titanResult;
                }

                return "FAIL:OPTION:suboption_not_found:" + sourceId + ":" + subOptionCardId;
            }

            // 指向性选项（有 targetId 或 position）：使用原始鼠标打开 + 重试流程
            var openResult = _coroutine.RunAndWait(MouseClickChoice(sourceId));
            if (!openResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                return openResult;

            if (!WaitForChoicePacketReady(700))
                return openResult;

            submitDetail = TrySubmitStructuredOption(sourceId, targetId, position, subOptionCardId);
            if (submitDetail == null)
                return "OK:OPTION:open_then_network:" + sourceId;

            return openResult;
        }

        private static string TryExecuteSubOptionThenTarget(int sourceId, int targetId, int position, string subOptionCardId)
        {
            if (sourceId <= 0 || string.IsNullOrWhiteSpace(subOptionCardId) || (targetId <= 0 && position <= 0))
                return null;

            string subOptionResult = null;

            // 先走“打出后自动弹出抉择 UI”的常规路径。
            if (WaitForSubOptionUiReady(1200))
                subOptionResult = TryResolveAndClickSubOption(sourceId, subOptionCardId);

            // UI 没自动弹出时，再尝试需要手动点实体打开抉择的路径。
            if (subOptionResult == null)
                subOptionResult = TryExecuteTitanStyleOption(sourceId, subOptionCardId);

            if (subOptionResult == null)
                return null;

            // 子选项已选定后，目标列表有时会晚于“子选项关闭”信号出现。
            // 因此这里不能把“未探测到 TARGET 模式”直接当作结束条件，
            // 而是继续在短时间内重试网络提交，并在后半段直接尝试鼠标点目标。
            WaitForTargetSelectionReady(700);

            for (var retry = 0; retry < 14; retry++)
            {
                var targetSubmitDetail = TrySubmitStructuredOption(sourceId, targetId, position, null);
                if (targetSubmitDetail == null)
                    return "OK:OPTION:sub_then_target_network:" + sourceId;

                var allowDirectTargetClick = retry >= 3 || HasTargetSelectionReady();
                if (targetId > 0 && allowDirectTargetClick && TryClickOptionTarget(sourceId, targetId))
                    return "OK:OPTION:sub_then_target_click:" + sourceId;

                Thread.Sleep(90);
            }

            return subOptionResult;
        }

        /// <summary>
        /// 在 SubOption/EntityChoices UI 已就绪时，尝试通过网络 API 或鼠标点击提交选项。
        /// 用于 Choose One 等打出卡牌后自动弹出子选项的场景。
        /// 返回 null 表示全部回退方案均失败。
        /// </summary>
        private static string TryResolveAndClickSubOption(int sourceId, string subOptionCardId)
        {
            // 重试结构化选项（此时 SubOption UI 已就绪，Options 包可能已更新）
            var submitDetail = TrySubmitStructuredOption(sourceId, 0, 0, subOptionCardId);
            if (submitDetail == null)
                return "OK:OPTION:open_then_network:" + sourceId;

            // 回退方案1：从 EntityChoices 包解析实体
            if (TryResolveChoiceEntityIdByCardId(GetGameState(), subOptionCardId, out var choiceEntityId))
                return _coroutine.RunAndWait(MouseClickChoice(choiceEntityId));

            // 回退方案2：从 ChoiceCardMgr 的 SubOption/友方卡牌解析实体
            if (TryResolveSubOptionEntityIdByCardId(subOptionCardId, out var subOptionEntityId))
                return _coroutine.RunAndWait(MouseClickChoice(subOptionEntityId));

            return null;
        }

        private static bool WaitForOptionTargetSelectionReady(int sourceId, int timeoutMs)
        {
            var deadline = Environment.TickCount + Math.Max(80, timeoutMs);
            while (Environment.TickCount < deadline)
            {
                if (HasTargetSelectionReady())
                    return true;

                Thread.Sleep(40);
            }

            return false;
        }

        private static bool WaitForTargetSelectionReady(int timeoutMs)
        {
            var deadline = Environment.TickCount + Math.Max(80, timeoutMs);
            while (Environment.TickCount < deadline)
            {
                if (HasTargetSelectionReady())
                    return true;

                Thread.Sleep(40);
            }

            return false;
        }

        private static bool WaitForPendingOptionTargetReady(int sourceEntityId, int timeoutMs)
        {
            var deadline = Environment.TickCount + Math.Max(80, timeoutMs);
            while (Environment.TickCount - deadline < 0)
            {
                if (HasTargetSelectionReady())
                    return true;

                if (sourceEntityId > 0
                    && HasPendingTargetSelection(sourceEntityId)
                    && !IsSubOptionUiReady())
                {
                    return true;
                }

                Thread.Sleep(40);
            }

            return false;
        }

        private static bool IsTargetSelectionResponseMode(object responseMode)
        {
            if (responseMode == null)
                return false;

            var modeName = responseMode.ToString();
            if (string.IsNullOrWhiteSpace(modeName))
                return false;

            return string.Equals(modeName, "TARGET", StringComparison.OrdinalIgnoreCase)
                || string.Equals(modeName, "OPTION_TARGET", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasPendingTargetSelection(int sourceId)
        {
            try
            {
                if (IsPlayTargetConfirmationPending(sourceId))
                    return true;

                var gs = GetGameState();
                if (gs == null)
                    return false;

                if (TryInvokeMethod(gs, "GetResponseMode", Array.Empty<object>(), out var modeObj) && modeObj != null)
                {
                    if (IsTargetSelectionResponseMode(modeObj))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool HasTargetSelectionReady()
        {
            try
            {
                var gs = GetGameState();
                if (gs == null)
                    return false;

                if (TryInvokeMethod(gs, "GetResponseMode", Array.Empty<object>(), out var modeObj) && modeObj != null)
                    return IsTargetSelectionResponseMode(modeObj);
            }
            catch
            {
            }

            return false;
        }

        private static bool TryClickOptionTarget(int sourceId, int targetId)
        {
            if (sourceId <= 0 || targetId <= 0)
                return false;

            // 必须先确认游戏确实处于 TARGET 模式，避免假阳性。
            if (!HasTargetSelectionReady())
                return false;

            var targetHeroSide = -1;
            if (IsFriendlyHeroEntityId(targetId))
                targetHeroSide = 0;
            else if (IsEnemyHeroEntityId(targetId))
                targetHeroSide = 1;

            if (!TryResolvePlayTargetScreenPos(targetId, targetHeroSide, out var tx, out var ty))
                return false;

            // 通过协程在主线程执行点击，确保 PressFrame/ReleaseFrame 与 Unity 帧同步。
            var clickResult = _coroutine.RunAndWait(MouseClickTargetCoroutine(tx, ty, targetId, targetHeroSide));
            if (clickResult == null || !clickResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                return false;

            var deadline = Environment.TickCount + 1800;
            while (Environment.TickCount < deadline)
            {
                if (!HasTargetSelectionReady())
                    return true;

                Thread.Sleep(60);
            }

            return false;
        }

        private static string TryClickPendingTarget(int targetId, int sourceEntityId)
        {
            if (targetId <= 0)
                return null;

            var canClickPendingTarget =
                HasTargetSelectionReady()
                || (sourceEntityId > 0 && HasPendingTargetSelection(sourceEntityId) && !IsSubOptionUiReady());
            if (!canClickPendingTarget)
                return null;

            var targetHeroSide = -1;
            if (IsFriendlyHeroEntityId(targetId))
                targetHeroSide = 0;
            else if (IsEnemyHeroEntityId(targetId))
                targetHeroSide = 1;

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                if (!TryResolvePlayTargetScreenPos(targetId, targetHeroSide, out var tx, out var ty))
                    return "FAIL:OPTION_TARGET:pos_not_found:" + targetId;

                var clickResult = _coroutine.RunAndWait(MouseClickTargetCoroutine(tx, ty, targetId, targetHeroSide));
                if (clickResult == null || !clickResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                    return clickResult ?? "FAIL:OPTION_TARGET:click_failed:" + targetId;

                var deadline = Environment.TickCount + 1800;
                while (Environment.TickCount - deadline < 0)
                {
                    var stillPending =
                        sourceEntityId > 0
                        ? HasPendingTargetSelection(sourceEntityId)
                        : HasTargetSelectionReady();
                    if (!stillPending)
                        return "OK:OPTION:target_click:" + targetId + ":mouse" + attempt;

                    Thread.Sleep(60);
                }
            }

            return "FAIL:OPTION_TARGET:not_confirmed:" + targetId;
        }

        private static void RememberPendingOptionTargetSource(int sourceEntityId, string reason)
        {
            if (sourceEntityId <= 0)
                return;

            lock (_pendingOptionTargetSourceSync)
            {
                _pendingOptionTargetSourceEntityId = sourceEntityId;
                _pendingOptionTargetSourceUntilTick = Environment.TickCount + PendingOptionTargetSourceWindowMs;
            }

            AppendActionTrace("option_target_source remember source=" + sourceEntityId + " reason=" + (reason ?? "unknown"));
        }

        private static int GetPendingOptionTargetSourceEntityId()
        {
            lock (_pendingOptionTargetSourceSync)
            {
                if (_pendingOptionTargetSourceEntityId <= 0)
                    return 0;

                if (_pendingOptionTargetSourceUntilTick != 0
                    && Environment.TickCount - _pendingOptionTargetSourceUntilTick >= 0)
                {
                    _pendingOptionTargetSourceEntityId = 0;
                    _pendingOptionTargetSourceUntilTick = 0;
                    return 0;
                }

                return _pendingOptionTargetSourceEntityId;
            }
        }

        private static void ClearPendingOptionTargetSource(string reason)
        {
            int clearedSourceId;
            lock (_pendingOptionTargetSourceSync)
            {
                clearedSourceId = _pendingOptionTargetSourceEntityId;
                _pendingOptionTargetSourceEntityId = 0;
                _pendingOptionTargetSourceUntilTick = 0;
            }

            if (clearedSourceId > 0)
                AppendActionTrace("option_target_source clear source=" + clearedSourceId + " reason=" + (reason ?? "unknown"));
        }

        private struct HumanizedTargetCandidate
        {
            public int EntityId;
            public int HeroSide;
        }

        private struct HumanizedCursorStep
        {
            public int X;
            public int Y;
            public float DelaySeconds;
        }

        private static IEnumerator<float> MouseClickTargetCoroutine(int x, int y, int targetEntityId, int targetHeroSide)
        {
            InputHook.Simulating = true;

            if (IsConstructedHumanizerEnabled())
            {
                foreach (var wait in MaybePreviewAlternateTarget(targetEntityId, targetHeroSide, false))
                    yield return wait;
            }

            foreach (var w in MoveCursorConstructed(x, y, 10, 0.012f, false)) yield return w;

            MouseSimulator.MoveTo(x, y);
            yield return 0.06f;
            MouseSimulator.LeftDown();
            yield return 0.08f;
            MouseSimulator.LeftUp();
            yield return 0.15f;

            _coroutine.SetResult("OK:TARGET_CLICK:" + x + "," + y);
        }

        private static IEnumerator<float> HumanizedTurnStart(int turn, string handEntityIdsCsv, int actionCount = 0)
        {
            InputHook.Simulating = true;
            var config = GetHumanizerConfigSnapshot();
            if (config == null || !config.Enabled)
            {
                _coroutine.SetResult("OK:HUMAN_TURN_START:disabled");
                yield break;
            }

            int thinkMs;
            if (actionCount > 0)
            {
                // 基于操作复杂度的思考时间
                thinkMs = ConstructedHumanizerPlanner.ComputeTurnStartDelayMs(
                    turn, actionCount, config.Intensity, null);
            }
            else
            {
                // 兼容旧协议：回退到线性递增模型
                int minExtraMs;
                int maxExtraMs;
                ConstructedHumanizerPlanner.GetTurnStartRandomRange(config.Intensity, out minExtraMs, out maxExtraMs);
                thinkMs = ConstructedHumanizerPlanner.ComputeTurnStartDelayMs(
                    turn,
                    config.Intensity,
                    NextHumanizeInt32(minExtraMs, maxExtraMs));
            }

            var startedUtc = DateTime.UtcNow;
            var shouldScanHand = ConstructedHumanizerPlanner.ShouldScanHandAtTurnStart(
                config.Intensity,
                turn,
                NextHumanizeInt32(1, 100));
            var scanMode = "pause_only";
            var orderedHandEntityIds = ParseEntityIdsCsv(handEntityIdsCsv);
            if (orderedHandEntityIds.Count == 0)
                orderedHandEntityIds = GameObjectFinder.GetHandEntityIds() ?? new List<int>();

            // 回合开始：有概率先扫视敌方场面
            if (ConstructedHumanizerPlanner.ShouldScanEnemyBoard(
                    config.Intensity, NextHumanizeInt32(1, 100))
                && GetRemainingTurnStartThinkMs(startedUtc, thinkMs) > 400)
            {
                var enemyScanIds = BuildEnemyBoardScanEntityIds();
                if (enemyScanIds.Count > 0)
                {
                    scanMode = "enemy_scan";
                    var scanCount = Math.Min(enemyScanIds.Count, NextHumanizeInt32(1, 3));
                    for (int i = 0; i < scanCount; i++)
                    {
                        if (GetRemainingTurnStartThinkMs(startedUtc, thinkMs) <= 300)
                            break;

                        if (!GameObjectFinder.GetEntityScreenPos(enemyScanIds[i], out var ex, out var ey))
                            continue;

                        foreach (var w in MoveCursorConstructed(ex, ey, 10, 0.010f, false))
                            yield return w;

                        // 悬停阅读敌方随从
                        yield return GetHumanizePauseSeconds(150, 350);
                    }
                }
            }

            if (shouldScanHand)
            {
                var scanHandEntityIds = PickTurnStartScanHandEntities(orderedHandEntityIds);
                var performedHandGlide = false;
                if (scanHandEntityIds.Count > 0)
                {
                    foreach (var handEntityId in scanHandEntityIds)
                    {
                        if (GetRemainingTurnStartThinkMs(startedUtc, thinkMs) <= 180)
                            break;

                        if (!GameObjectFinder.GetEntityScreenPos(handEntityId, out var x, out var y))
                            continue;

                        performedHandGlide = true;
                        scanMode = "glide";
                        foreach (var wait in MoveCursorConstructed(x, y, 10, 0.010f, false))
                            yield return wait;
                    }

                    if (performedHandGlide
                        && GetRemainingTurnStartThinkMs(startedUtc, thinkMs) > 180
                        && TryGetTurnStartIdlePoint(out var idleX, out var idleY))
                    {
                        foreach (var wait in MoveCursorConstructed(idleX, idleY, 10, 0.010f, false))
                            yield return wait;
                    }
                }

                if (!performedHandGlide)
                {
                    foreach (var fallbackPoint in BuildTurnStartFallbackPoints())
                    {
                        if (GetRemainingTurnStartThinkMs(startedUtc, thinkMs) <= 180)
                            break;

                        scanMode = "scan";
                        foreach (var wait in MoveCursorConstructed(fallbackPoint.Item1, fallbackPoint.Item2, 10, 0.010f, false))
                            yield return wait;

                        var remainingMs = GetRemainingTurnStartThinkMs(startedUtc, thinkMs);
                        if (remainingMs <= 0)
                            break;

                        yield return Math.Min(remainingMs, NextHumanizeInt32(120, 240)) / 1000f;
                    }
                }
            }

            var tailMs = GetRemainingTurnStartThinkMs(startedUtc, thinkMs);
            if (tailMs > 300 && config.Enabled)
            {
                foreach (var w in IdleMicroMovement(tailMs))
                    yield return w;
            }
            else if (tailMs > 0)
            {
                yield return tailMs / 1000f;
            }

            _coroutine.SetResult(
                "OK:HUMAN_TURN_START:"
                + HumanizerProtocol.GetIntensityToken(config.Intensity)
                + ":"
                + thinkMs
                + ":"
                + scanMode);
        }

        private static List<int> ParseEntityIdsCsv(string csv)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(csv))
                return result;

            foreach (var part in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out var entityId) && entityId > 0)
                    result.Add(entityId);
            }

            return result;
        }

        private static List<int> PickTurnStartScanHandEntities(List<int> orderedHandEntityIds)
        {
            var unique = new List<int>();
            foreach (var entityId in orderedHandEntityIds ?? new List<int>())
            {
                if (entityId > 0 && !unique.Contains(entityId))
                    unique.Add(entityId);
            }

            if (unique.Count <= 3)
                return unique;

            var picks = new List<int>();
            var cursor = NextHumanizeInt32(0, unique.Count - 1);
            var direction = NextHumanizeInt32(0, 1) == 0 ? -1 : 1;
            while (picks.Count < 3 && unique.Count > 0)
            {
                if (cursor < 0)
                {
                    cursor = 0;
                    direction = 1;
                }
                else if (cursor >= unique.Count)
                {
                    cursor = unique.Count - 1;
                    direction = -1;
                }

                var entityId = unique[cursor];
                if (!picks.Contains(entityId))
                    picks.Add(entityId);

                cursor += direction;
            }

            return picks;
        }

        private static bool TryGetTurnStartIdlePoint(out int x, out int y)
        {
            x = 0;
            y = 0;

            var screenWidth = MouseSimulator.GetScreenWidth();
            var screenHeight = MouseSimulator.GetScreenHeight();
            if (screenWidth > 0 && screenHeight > 0)
            {
                x = (int)(screenWidth * 0.50f);
                y = (int)(screenHeight * 0.57f);
                return true;
            }

            if (GameObjectFinder.GetHeroPowerScreenPos(out x, out y))
                return true;

            if (GameObjectFinder.GetHeroScreenPos(true, out x, out y))
                return true;

            return false;
        }

        /// <summary>
        /// 获取敌方场上随从的 EntityId 列表（随机打乱顺序）
        /// </summary>
        private static List<int> BuildEnemyBoardScanEntityIds()
        {
            var result = new List<int>();
            try
            {
                var state = SafeReadGameState();
                if (state?.MinionEnemy == null)
                    return result;

                foreach (var entity in state.MinionEnemy)
                {
                    if (entity != null && entity.EntityId > 0)
                        result.Add(entity.EntityId);
                }

                // 随机打乱顺序，模拟真人不会从左到右依次看
                for (int i = result.Count - 1; i > 0; i--)
                {
                    int j = NextHumanizeInt32(0, i);
                    var tmp = result[i];
                    result[i] = result[j];
                    result[j] = tmp;
                }
            }
            catch { }

            return result;
        }

        private static List<Tuple<int, int>> BuildTurnStartFallbackPoints()
        {
            var result = new List<Tuple<int, int>>();
            if (GameObjectFinder.GetHeroScreenPos(true, out var heroX, out var heroY))
                result.Add(Tuple.Create(heroX, heroY));
            if (GameObjectFinder.GetHeroPowerScreenPos(out var powerX, out var powerY))
                result.Add(Tuple.Create(powerX, powerY));

            var screenWidth = MouseSimulator.GetScreenWidth();
            var screenHeight = MouseSimulator.GetScreenHeight();
            if (screenWidth > 0 && screenHeight > 0)
                result.Add(Tuple.Create((int)(screenWidth * 0.50f), (int)(screenHeight * 0.57f)));

            return result;
        }

        private static int GetRemainingTurnStartThinkMs(DateTime startedUtc, int thinkMs)
        {
            var elapsedMs = (int)(DateTime.UtcNow - startedUtc).TotalMilliseconds;
            return Math.Max(0, thinkMs - elapsedMs);
        }

        private static IEnumerable<float> MaybePreviewAlternateTarget(int targetEntityId, int targetHeroSide, bool dragging)
        {
            var config = GetHumanizerConfigSnapshot();
            if (config == null
                || !config.Enabled
                || !ConstructedHumanizerPlanner.ShouldPreviewAlternateTarget(
                    config.Intensity,
                    NextHumanizeInt32(1, 100)))
            {
                yield break;
            }

            if (!TryResolveAlternateTargetScreenPos(targetEntityId, targetHeroSide, out var previewX, out var previewY))
                yield break;

            foreach (var wait in MoveCursorConstructed(previewX, previewY, 10, 0.010f, dragging))
                yield return wait;

            // 根据目标阵营调整悬停时间：敌方随从最久（"阅读效果"），己方最短
            int hoverMinMs;
            int hoverMaxMs;
            if (targetHeroSide == 0)
            {
                // 己方目标
                hoverMinMs = 80;
                hoverMaxMs = 180;
            }
            else if (targetHeroSide == 1)
            {
                // 敌方英雄
                hoverMinMs = 120;
                hoverMaxMs = 280;
            }
            else
            {
                // 敌方随从或未知（需要更多"阅读时间"）
                hoverMinMs = 200;
                hoverMaxMs = 450;
            }

            yield return GetHumanizePauseSeconds(hoverMinMs, hoverMaxMs);
        }

        private static bool TryResolveAlternateTargetScreenPos(
            int actualTargetEntityId,
            int actualTargetHeroSide,
            out int previewX,
            out int previewY)
        {
            previewX = 0;
            previewY = 0;
            if (!IsConstructedHumanizerEnabled())
                return false;

            var state = SafeReadGameState();
            if (state == null)
                return false;

            var candidates = BuildAlternateTargetCandidates(state, actualTargetEntityId, actualTargetHeroSide);
            if (candidates.Count == 0)
                return false;

            var startIndex = NextHumanizeInt32(0, candidates.Count - 1);
            for (int offset = 0; offset < candidates.Count; offset++)
            {
                var candidate = candidates[(startIndex + offset) % candidates.Count];
                if (TryResolvePlayTargetScreenPos(candidate.EntityId, candidate.HeroSide, out previewX, out previewY))
                    return true;
            }

            return false;
        }

        private static List<HumanizedTargetCandidate> BuildAlternateTargetCandidates(
            GameStateData state,
            int actualTargetEntityId,
            int actualTargetHeroSide)
        {
            var candidates = new List<HumanizedTargetCandidate>();
            if (state == null)
                return candidates;

            var resolvedHeroSide = actualTargetHeroSide;
            if (resolvedHeroSide < 0)
            {
                if (state.HeroFriend != null && state.HeroFriend.EntityId == actualTargetEntityId)
                    resolvedHeroSide = 0;
                else if (state.HeroEnemy != null && state.HeroEnemy.EntityId == actualTargetEntityId)
                    resolvedHeroSide = 1;
            }

            if (resolvedHeroSide == 1 || IsEnemyMinionEntity(state, actualTargetEntityId))
            {
                if (state.HeroEnemy != null && state.HeroEnemy.EntityId != actualTargetEntityId)
                    candidates.Add(new HumanizedTargetCandidate { EntityId = state.HeroEnemy.EntityId, HeroSide = 1 });

                foreach (var entity in state.MinionEnemy ?? new List<EntityData>())
                {
                    if (entity != null && entity.EntityId > 0 && entity.EntityId != actualTargetEntityId)
                        candidates.Add(new HumanizedTargetCandidate { EntityId = entity.EntityId, HeroSide = -1 });
                }

                return candidates;
            }

            if (resolvedHeroSide == 0
                || (state.MinionFriend != null && state.MinionFriend.Any(entity => entity != null && entity.EntityId == actualTargetEntityId)))
            {
                if (state.HeroFriend != null && state.HeroFriend.EntityId != actualTargetEntityId)
                    candidates.Add(new HumanizedTargetCandidate { EntityId = state.HeroFriend.EntityId, HeroSide = 0 });

                foreach (var entity in state.MinionFriend ?? new List<EntityData>())
                {
                    if (entity != null && entity.EntityId > 0 && entity.EntityId != actualTargetEntityId)
                        candidates.Add(new HumanizedTargetCandidate { EntityId = entity.EntityId, HeroSide = -1 });
                }
            }

            return candidates;
        }

        private static GameStateData SafeReadGameState()
        {
            try
            {
                return new GameReader().ReadGameState();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 泰坦/锻造等需要先点击场上实体才弹出选择 UI 的路径。
        /// 流程：点击场上实体 → 等待选择 UI 出现 → 通过 CardId 解析目标实体 → 点击。
        /// 返回 null 表示该路径不适用或全部回退方案均失败。
        /// </summary>
        private static string TryExecuteTitanStyleOption(int sourceId, string subOptionCardId)
        {
            // 阶段1：点击场上实体以打开选择 UI
            var clickResult = _coroutine.RunAndWait(MouseClickChoice(sourceId));

            // 阶段2：等待选择 UI 出现（泰坦能力 / 锻造等）
            if (!WaitForSubOptionUiReady(3000))
            {
                // 再等一轮，某些泰坦在动画较长时需要更多时间
                if (!WaitForSubOptionUiReady(2000))
                    return null;
            }

            // 阶段3：重试结构化网络提交
            var submitDetail = TrySubmitStructuredOption(sourceId, 0, 0, subOptionCardId);
            if (submitDetail == null)
                return "OK:OPTION:titan_network:" + sourceId;

            // 阶段4：从 EntityChoices 包解析实体
            if (TryResolveChoiceEntityIdByCardId(GetGameState(), subOptionCardId, out var choiceEntityId))
                return _coroutine.RunAndWait(MouseClickChoice(choiceEntityId));

            // 阶段5：从 ChoiceCardMgr 的 SubOption/友方卡牌解析实体
            if (TryResolveSubOptionEntityIdByCardId(subOptionCardId, out var subOptionEntityId))
                return _coroutine.RunAndWait(MouseClickChoice(subOptionEntityId));

            return null;
        }

        private static string TrySubmitStructuredOption(int sourceId, int targetId, int position, string desiredSubOptionCardId)
        {
            try
            {
                return SendOptionForEntity(sourceId, targetId, position, desiredSubOptionCardId, requireSourceLeaveHand: false);
            }
            catch (Exception ex)
            {
                return "ex:" + SimplifyException(ex);
            }
        }

        private static bool WaitForChoicePacketReady(int timeoutMs)
        {
            var deadline = Environment.TickCount + Math.Max(80, timeoutMs);
            while (Environment.TickCount < deadline)
            {
                var gs = GetGameState();
                if (gs != null)
                {
                    var choicePacket = TryGetFriendlyChoicePacket(gs);
                    if (TryGetChoicePacketEntityIds(choicePacket, out var entityIds) && entityIds.Count > 0)
                        return true;
                }

                Thread.Sleep(40);
            }

            return false;
        }

        /// <summary>
        /// 等待 EntityChoices 包 或 ChoiceCardMgr SubOption / 选择 UI 就绪。
        /// Choose One 卡牌使用 SubOption 机制（Options 包），不是 EntityChoices 包。
        /// Discover 使用 EntityChoices 包。此方法同时等待两种机制。
        /// </summary>
        private static bool WaitForEntityChoiceUiReady(int timeoutMs)
        {
            var deadline = Environment.TickCount + Math.Max(80, timeoutMs);
            while (Environment.TickCount < deadline)
            {
                if (IsEntityChoiceUiReady())
                    return true;

                Thread.Sleep(40);
            }

            return false;
        }

        private static bool WaitForSubOptionUiReady(int timeoutMs)
        {
            var deadline = Environment.TickCount + Math.Max(80, timeoutMs);
            while (Environment.TickCount < deadline)
            {
                if (IsSubOptionUiReady())
                    return true;

                Thread.Sleep(40);
            }

            return false;
        }

        private static bool WaitForSubOptionOrChoiceReady(int timeoutMs)
        {
            var deadline = Environment.TickCount + Math.Max(80, timeoutMs);
            while (Environment.TickCount < deadline)
            {
                if (IsEntityChoiceUiReady() || IsSubOptionUiReady())
                    return true;

                Thread.Sleep(40);
            }

            return false;
        }

        private static bool TryResolveChoiceEntityIdByCardId(object gameState, string cardId, out int entityId)
        {
            entityId = 0;
            if (gameState == null || string.IsNullOrWhiteSpace(cardId))
                return false;

            var choicePacket = TryGetFriendlyChoicePacket(gameState);
            if (!TryGetChoicePacketEntityIds(choicePacket, out var entityIds))
                return false;

            foreach (var candidateEntityId in entityIds)
            {
                var resolvedCardId = ResolveEntityCardId(gameState, candidateEntityId);
                if (!string.Equals(resolvedCardId, cardId, StringComparison.OrdinalIgnoreCase))
                    continue;

                entityId = candidateEntityId;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 从 ChoiceCardMgr 的 GetFriendlyCards（SubOption / Choose One 卡片）中
        /// 按 CardId 查找对应的 entity ID。
        /// Choose One 的子选项卡片由 ChoiceCardMgr 内部管理，不在 EntityChoices 包中。
        /// </summary>
        private static bool TryResolveSubOptionEntityIdByCardId(string cardId, out int entityId)
        {
            entityId = 0;
            if (string.IsNullOrWhiteSpace(cardId))
                return false;

            if (!TryGetChoiceCardMgrFriendlyCards(out var friendlyCards)
                || friendlyCards == null
                || friendlyCards.Count == 0)
            {
                return false;
            }

            var gs = GetGameState();
            foreach (var card in friendlyCards)
            {
                var entity = ResolveChoiceCardEntity(card);
                if (entity == null)
                    continue;

                var candidateEntityId = ResolveEntityId(entity);
                if (candidateEntityId <= 0)
                    continue;

                var resolvedCardId = ResolveCardIdFromObject(entity);
                if (string.IsNullOrWhiteSpace(resolvedCardId) && gs != null)
                    resolvedCardId = ResolveEntityCardId(gs, candidateEntityId);

                if (string.Equals(resolvedCardId, cardId, StringComparison.OrdinalIgnoreCase))
                {
                    entityId = candidateEntityId;
                    return true;
                }
            }

            return false;
        }

        private static bool EnsureTypes()
        {
            if (_asm != null) return true;
            // 从共享反射上下文获取已缓存的类型，避免重复扫描 AppDomain
            var ctx = ReflectionContext.Instance;
            if (!ctx.Init()) return false;
            _asm = ctx.AsmCSharp;
            _gameStateType = ctx.GameStateType;
            _entityType = ctx.EntityType;
            _networkType = ctx.NetworkType;
            _inputMgrType = ctx.InputMgrType;
            _connectApiType = ctx.ConnectApiType;
            _choiceCardMgrType = _asm?.GetType("ChoiceCardMgr");
            _iTweenType = _asm?.GetType("iTween")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("iTween"))
                    .FirstOrDefault(t => t != null);
            return _gameStateType != null;
        }

        /// <summary>
        /// 通过协程+鼠标模拟执行操作（从后台线程调用，阻塞等待完成）
        /// </summary>
        public static string Execute(GameReader reader, string actionData)
        {
            if (string.IsNullOrEmpty(actionData)) return "SKIP:empty";
            if (!EnsureTypes()) return "SKIP:no_asm";

            var parts = actionData.IndexOf('|') >= 0
                ? actionData.Split('|')
                : actionData.Split(':');
            var type = parts[0];

            switch (type)
            {
                case "END_TURN":
                    return _coroutine.RunAndWait(MouseEndTurn());
                case "CANCEL":
                    return _coroutine.RunAndWait(MouseCancel());
                case "PLAY":
                    {
                        int sourceId = int.Parse(parts[1]);
                        int targetId = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                        int position = parts.Length > 3 ? int.Parse(parts[3]) : 0;
                        int targetHeroSide = -1; // -1: 不是英雄目标, 0: 我方英雄, 1: 敌方英雄
                        bool sourceUsesBoardDrop = false;

                        try
                        {
                            var s = reader?.ReadGameState();
                            if (targetId > 0)
                            {
                                if (s?.HeroFriend != null && s.HeroFriend.EntityId == targetId) targetHeroSide = 0;
                                else if (s?.HeroEnemy != null && s.HeroEnemy.EntityId == targetId) targetHeroSide = 1;
                            }

                            var gs = GetGameState();
                            if (gs != null)
                                TryUsesBoardDropForPlay(gs, sourceId, out sourceUsesBoardDrop);
                        }
                        catch { }

                        return _coroutine.RunAndWait(MousePlayCardByMouseFlow(sourceId, targetId, position, targetHeroSide, sourceUsesBoardDrop));
                    }
                case "ATTACK":
                    {
                        int attackerId = int.Parse(parts[1]);
                        int targetId = int.Parse(parts[2]);
                        bool sourceIsFriendlyHero = false;
                        bool targetIsEnemyHero = false;
                        const int attackConfirmPollCount = 1;
                        const int attackConfirmSleepMs = 10;
                        GameStateData beforeState = null;
                        AttackStateSnapshot beforeSnapshot = default;
                        var hasBeforeSnapshot = false;
                        try
                        {
                            beforeState = reader?.ReadGameState();
                            sourceIsFriendlyHero = beforeState?.HeroFriend != null && beforeState.HeroFriend.EntityId == attackerId;
                            // 兜底：ReadGameState 返回的 HeroFriend 可能为 null，通过反射再检查一次
                            if (!sourceIsFriendlyHero)
                                sourceIsFriendlyHero = IsFriendlyHeroEntityId(attackerId);
                            targetIsEnemyHero = beforeState?.HeroEnemy != null && beforeState.HeroEnemy.EntityId == targetId;
                            if (!targetIsEnemyHero)
                                targetIsEnemyHero = IsEnemyHeroEntityId(targetId);
                            // EXHAUSTED / NUM_ATTACKS_THIS_TURN / ATK 等 tag 在客户端帧上频繁出现
                            // 短暂错位，对英雄和随从都可能导致 false-reject。
                            // 统一跳过预检查，先执行鼠标操作，再由后置 DidAttackApply 确认是否生效。
                            // 若攻击者确实无法攻击，鼠标点击不会产生效果，DidAttackApply 返回 false，
                            // 最终以 FAIL:ATTACK:not_confirmed 结束。
                            if (beforeState != null)
                            {
                                CanEntityAttackNow(beforeState, attackerId, targetId, out var preCheckReason);
                                AppendActionTrace(
                                    "ATTACK pre-check attacker=" + attackerId
                                    + " target=" + targetId
                                    + " reason=" + preCheckReason);
                            }

                            hasBeforeSnapshot = TryCaptureAttackState(beforeState, attackerId, targetId, out beforeSnapshot);
                        }
                        catch { }

                        const int maxAttempts = 1;
                        long lastMouseMs = 0;
                        long lastConfirmMs = 0;
                        int lastConfirmPolls = 0;
                        var lastApplyReason = "not_started";
                        for (int attempt = 0; attempt < maxAttempts; attempt++)
                        {
                            if (attempt > 0)
                            {
                                try { _coroutine.RunAndWait(MouseCancel(), 1200); } catch { }
                                Thread.Sleep(120);
                                beforeState = reader?.ReadGameState();
                                hasBeforeSnapshot = TryCaptureAttackState(beforeState, attackerId, targetId, out beforeSnapshot);
                            }

                            var mouseSw = Stopwatch.StartNew();
                            var attackResult = _coroutine.RunAndWait(
                                MouseAttack(
                                    attackerId,
                                    targetId,
                                    sourceIsFriendlyHero,
                                    targetIsEnemyHero));
                            mouseSw.Stop();
                            lastMouseMs = mouseSw.ElapsedMilliseconds;
                            if (!attackResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                            {
                                AppendActionTrace(
                                    "ATTACK mouse_result attacker=" + attackerId
                                    + " target=" + targetId
                                    + " attempt=" + (attempt + 1)
                                    + " mouseMs=" + lastMouseMs
                                    + " result=" + attackResult);
                                return AppendAttackTimingToResult(
                                    attackResult,
                                    attempt + 1,
                                    lastMouseMs,
                                    0,
                                    0,
                                    "mouse_failed");
                            }

                            if (!hasBeforeSnapshot)
                            {
                                AppendActionTrace(
                                    "ATTACK confirm_skipped attacker=" + attackerId
                                    + " target=" + targetId
                                    + " attempt=" + (attempt + 1)
                                    + " mouseMs=" + lastMouseMs
                                    + " reason=no_before_snapshot");
                                return AppendAttackTimingToResult(
                                    attackResult,
                                    attempt + 1,
                                    lastMouseMs,
                                    0,
                                    0,
                                    "confirm_skipped_no_before_snapshot");
                            }

                            var confirmSw = Stopwatch.StartNew();
                            var confirmPolls = 0;
                            var confirmReason = "unchanged";
                            for (int i = 0; i < attackConfirmPollCount; i++)
                            {
                                Thread.Sleep(attackConfirmSleepMs);
                                confirmPolls++;
                                var afterState = reader?.ReadGameState();
                                if (!TryCaptureAttackState(afterState, attackerId, targetId, out var afterSnapshot))
                                {
                                    confirmReason = "after_snapshot_missing";
                                    continue;
                                }

                                var applyObservation = GetAttackApplyObservation(beforeSnapshot, afterSnapshot);
                                confirmReason = applyObservation.Reason;
                                if (applyObservation.Applied)
                                {
                                    confirmSw.Stop();
                                    AppendActionTrace(
                                        "ATTACK confirm_ok attacker=" + attackerId
                                        + " target=" + targetId
                                        + " attempt=" + (attempt + 1)
                                        + " mouseMs=" + lastMouseMs
                                        + " confirmMs=" + confirmSw.ElapsedMilliseconds
                                        + " confirmPolls=" + confirmPolls
                                        + " apply=" + confirmReason);
                                    return AppendAttackTimingToResult(
                                        attackResult,
                                        attempt + 1,
                                        lastMouseMs,
                                        confirmSw.ElapsedMilliseconds,
                                        confirmPolls,
                                        confirmReason);
                                }
                            }

                            confirmSw.Stop();
                            lastConfirmMs = confirmSw.ElapsedMilliseconds;
                            lastConfirmPolls = confirmPolls;
                            lastApplyReason = confirmReason;
                            AppendActionTrace(
                                "ATTACK confirm_retry attacker=" + attackerId
                                + " target=" + targetId
                                + " attempt=" + (attempt + 1)
                                + " mouseMs=" + lastMouseMs
                                + " confirmMs=" + lastConfirmMs
                                + " confirmPolls=" + lastConfirmPolls
                                + " apply=" + lastApplyReason);
                        }

                        if (lastMouseMs > 0)
                        {
                            AppendActionTrace(
                                "ATTACK confirm_soft_timeout attacker=" + attackerId
                                + " target=" + targetId
                                + " attempt=1"
                                + " mouseMs=" + lastMouseMs
                                + " confirmMs=" + lastConfirmMs
                                + " confirmPolls=" + lastConfirmPolls
                                + " apply=" + lastApplyReason);
                            return AppendAttackTimingToResult(
                                "OK:ATTACK:" + attackerId + ":click_drag_click",
                                1,
                                lastMouseMs,
                                lastConfirmMs,
                                lastConfirmPolls,
                                "confirm_short_timeout");
                        }

                        AppendActionTrace(
                            "ATTACK confirm_fail attacker=" + attackerId
                            + " target=" + targetId
                            + " attempts=" + maxAttempts
                            + " lastMouseMs=" + lastMouseMs
                            + " lastConfirmMs=" + lastConfirmMs
                            + " lastConfirmPolls=" + lastConfirmPolls
                            + " apply=" + lastApplyReason);
                        return "FAIL:ATTACK:not_confirmed:" + attackerId
                            + ":attempts=" + maxAttempts
                            + ":lastMouseMs=" + lastMouseMs
                            + ":lastConfirmMs=" + lastConfirmMs
                            + ":lastConfirmPolls=" + lastConfirmPolls
                            + ":apply=" + lastApplyReason;
                    }
                case "HERO_POWER":
                    return _coroutine.RunAndWait(MouseHeroPower(
                        0,
                        parts.Length > 2 ? int.Parse(parts[2]) : 0));
                case "USE_LOCATION":
                    {
                        int sourceId = int.Parse(parts[1]);
                        int targetId = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                        int targetHeroSide = -1; // -1: 非英雄目标, 0: 我方英雄, 1: 敌方英雄

                        if (targetId > 0)
                        {
                            try
                            {
                                var s = reader?.ReadGameState();
                                if (s?.HeroFriend != null && s.HeroFriend.EntityId == targetId) targetHeroSide = 0;
                                else if (s?.HeroEnemy != null && s.HeroEnemy.EntityId == targetId) targetHeroSide = 1;
                            }
                            catch { }
                        }

                        return _coroutine.RunAndWait(MouseUseLocation(sourceId, targetId, targetHeroSide));
                    }
                case "OPTION":
                    {
                        int sourceId = int.Parse(parts[1]);
                        int targetId = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                        int position = parts.Length > 3 ? int.Parse(parts[3]) : 0;
                        string subOptionCardId = parts.Length > 4 ? parts[4] : null;
                        return ExecuteOption(sourceId, targetId, position, subOptionCardId);
                    }
                case "TRADE":
                    return _coroutine.RunAndWait(MouseTradeCard(int.Parse(parts[1])));
                case "CONCEDE":
                    Concede();
                    return "OK:CONCEDE";
                case "HUMAN_TURN_START":
                    {
                        int turn = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                        var handEntityIdsCsv = parts.Length > 2 ? parts[2] : string.Empty;
                        int actionCount = parts.Length > 3 ? int.Parse(parts[3]) : 0;
                        return _coroutine.RunAndWait(HumanizedTurnStart(turn, handEntityIdsCsv, actionCount), 15000);
                    }

                // ── 战旗模式动作 ──
                case "BG_BUY":
                    {
                        int shopEntityId = int.Parse(parts[1]);
                        int position = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                        return _coroutine.RunAndWait(BgMouseBuy(shopEntityId, position));
                    }
                case "BG_SELL":
                    {
                        int boardEntityId = int.Parse(parts[1]);
                        return _coroutine.RunAndWait(BgMouseSell(boardEntityId));
                    }
                case "BG_MOVE":
                    {
                        int boardEntityId = int.Parse(parts[1]);
                        int newIndex = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                        return _coroutine.RunAndWait(BgMouseMove(boardEntityId, newIndex));
                    }
                case "BG_TAVERN_UP":
                    return _coroutine.RunAndWait(BgMouseTavernUp());
                case "BG_HERO_POWER":
                    {
                        int sourceHeroPowerEntityId = 0;
                        int targetEntityId = 0;
                        if (parts.Length > 2)
                        {
                            sourceHeroPowerEntityId = int.Parse(parts[1]);
                            targetEntityId = int.Parse(parts[2]);
                        }
                        else if (parts.Length > 1)
                        {
                            targetEntityId = int.Parse(parts[1]);
                        }

                        return _coroutine.RunAndWait(MouseHeroPower(sourceHeroPowerEntityId, targetEntityId));
                    }
                case "BG_PLAY":
                    {
                        int handEntityId = int.Parse(parts[1]);
                        int targetEntityId = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                        int position = parts.Length > 3 ? int.Parse(parts[3]) : 0;
                        int targetHeroSide = -1; // -1: 非英雄目标, 0: 我方英雄, 1: 敌方英雄
                        bool sourceUsesBoardDrop = false;
                        bool isMagneticPlay = false;

                        try
                        {
                            var s = reader?.ReadGameState();
                            if (targetEntityId > 0)
                            {
                                if (s?.HeroFriend != null && s.HeroFriend.EntityId == targetEntityId) targetHeroSide = 0;
                                else if (s?.HeroEnemy != null && s.HeroEnemy.EntityId == targetEntityId) targetHeroSide = 1;
                            }

                            var gs = GetGameState();
                            if (gs != null)
                            {
                                TryUsesBoardDropForPlay(gs, handEntityId, out sourceUsesBoardDrop);
                                isMagneticPlay = IsBattlegroundMagneticPlay(gs, handEntityId, targetEntityId);
                            }
                        }
                        catch { }

                        return _coroutine.RunAndWait(BgMousePlayFromHand(handEntityId, targetEntityId, position, targetHeroSide, sourceUsesBoardDrop, isMagneticPlay));
                    }
                case "BG_HERO_PICK":
                    {
                        int choiceIndex = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                        return _coroutine.RunAndWait(BgPickHero(choiceIndex));
                    }
                case "BG_REROLL":
                    return _coroutine.RunAndWait(BgMouseReroll());
                case "BG_HERO_REROLL":
                    {
                        int choiceIndex = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                        return _coroutine.RunAndWait(BgMouseHeroReroll(choiceIndex));
                    }
                case "BG_FREEZE":
                    return _coroutine.RunAndWait(BgMouseFreeze());
                case "BG_END_TURN":
                    return _coroutine.RunAndWait(MouseEndTurn());

                case "PROBE_CARD_COMPONENTS":
                    return ProbeHandCardComponents();

                default:
                    return "SKIP:unknown_type:" + type;
            }
        }

        #region 鼠标模拟协程

        /// <summary>
        /// 缓动曲线平滑移动（ease-in-out）
        /// </summary>
        private static IEnumerable<float> SmoothMove(int tx, int ty, int steps = 25, float stepDelay = 0.012f)
        {
            int sx = MouseSimulator.CurX, sy = MouseSimulator.CurY;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                // ease-in-out: 起步和收尾平缓，中间快
                float ease = t < 0.5f ? 2f * t * t : 1f - (-2f * t + 2f) * (-2f * t + 2f) / 2f;
                MouseSimulator.MoveTo(sx + (int)((tx - sx) * ease), sy + (int)((ty - sy) * ease));
                yield return stepDelay;
            }
        }

        private static IEnumerable<float> MoveCursorConstructed(int tx, int ty, int steps, float stepDelay, bool dragging)
        {
            // 非拖拽模式下对目标坐标施加高斯偏移，模拟真人点击不精确
            if (!dragging && IsConstructedHumanizerEnabled())
                ApplyGaussianOffset(ref tx, ref ty);

            if (IsConstructedHumanizerEnabled()
                && TryBuildCubicBezierMove(tx, ty, dragging, out var humanizedSteps))
            {
                foreach (var step in humanizedSteps)
                {
                    MouseSimulator.MoveTo(step.X, step.Y);
                    yield return step.DelaySeconds;
                }

                yield break;
            }

            foreach (var wait in SmoothMove(tx, ty, steps, stepDelay))
                yield return wait;
        }

        private static bool TryBuildCubicBezierMove(int tx, int ty, bool dragging, out List<HumanizedCursorStep> steps)
        {
            steps = null;

            try
            {
                int sx = MouseSimulator.CurX;
                int sy = MouseSimulator.CurY;
                double dx = tx - sx;
                double dy = ty - sy;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                var builtSteps = new List<HumanizedCursorStep>();
                if (distance < 2d)
                {
                    builtSteps.Add(new HumanizedCursorStep
                    {
                        X = tx,
                        Y = ty,
                        DelaySeconds = dragging ? 0.010f : 0.004f
                    });
                    steps = builtSteps;
                    return true;
                }

                double normalX = -dy / distance;
                double normalY = dx / distance;
                double offsetMagnitude = Math.Max(14d, distance * (NextHumanizeInt32(8, 18) / 100d));
                if (NextHumanizeInt32(0, 1) == 0)
                    offsetMagnitude = -offsetMagnitude;

                double c1Scale = NextHumanizeDouble(0.45d, 0.95d);
                double c2Scale = NextHumanizeDouble(0.25d, 0.80d);
                double c1x = sx + (dx * 0.28d) + (normalX * offsetMagnitude * c1Scale);
                double c1y = sy + (dy * 0.28d) + (normalY * offsetMagnitude * c1Scale);
                double c2x = sx + (dx * 0.72d) - (normalX * offsetMagnitude * c2Scale);
                double c2y = sy + (dy * 0.72d) - (normalY * offsetMagnitude * c2Scale);

                int stepCount = dragging ? NextHumanizeInt32(14, 24) : NextHumanizeInt32(6, 10);
                float stepDelay = dragging
                    ? (float)NextHumanizeDouble(0.008d, 0.014d)
                    : (float)NextHumanizeDouble(0.004d, 0.007d);

                for (int i = 1; i <= stepCount; i++)
                {
                    double t = (double)i / stepCount;
                    double omt = 1d - t;
                    double px =
                        (omt * omt * omt * sx)
                        + (3d * omt * omt * t * c1x)
                        + (3d * omt * t * t * c2x)
                        + (t * t * t * tx);
                    double py =
                        (omt * omt * omt * sy)
                        + (3d * omt * omt * t * c1y)
                        + (3d * omt * t * t * c2y)
                        + (t * t * t * ty);

                    builtSteps.Add(new HumanizedCursorStep
                    {
                        X = (int)Math.Round(px),
                        Y = (int)Math.Round(py),
                        DelaySeconds = stepDelay
                    });
                }

                steps = builtSteps;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static float GetHumanizePauseSeconds(int minMs, int maxMs)
        {
            return NextHumanizeInt32(minMs, maxMs) / 1000f;
        }

        /// <summary>
        /// 思考期间鼠标空闲微动，避免完全静止
        /// </summary>
        private static IEnumerable<float> IdleMicroMovement(int durationMs)
        {
            if (durationMs <= 0)
                yield break;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            double lastDirX = 0d;
            double lastDirY = 0d;

            while (sw.ElapsedMilliseconds < durationMs)
            {
                var intervalMs = NextHumanizeInt32(200, 400);
                yield return intervalMs / 1000f;

                if (sw.ElapsedMilliseconds >= durationMs)
                    break;

                var pixels = NextHumanizeInt32(3, 8);
                // 方向有连续性：上次方向 ×0.3 + 新随机方向 ×0.7
                double newDirX = NextHumanizeDouble(-1d, 1d);
                double newDirY = NextHumanizeDouble(-1d, 1d);
                double dirX = lastDirX * 0.3d + newDirX * 0.7d;
                double dirY = lastDirY * 0.3d + newDirY * 0.7d;
                double mag = Math.Sqrt(dirX * dirX + dirY * dirY);
                if (mag < 0.01d) continue;
                dirX /= mag;
                dirY /= mag;
                lastDirX = dirX;
                lastDirY = dirY;

                int targetX = MouseSimulator.CurX + (int)(dirX * pixels);
                int targetY = MouseSimulator.CurY + (int)(dirY * pixels);

                int screenWidth = MouseSimulator.GetScreenWidth();
                int screenHeight = MouseSimulator.GetScreenHeight();
                if (screenWidth > 0)
                    targetX = Math.Max(10, Math.Min(screenWidth - 10, targetX));
                if (screenHeight > 0)
                    targetY = Math.Max(10, Math.Min(screenHeight - 10, targetY));

                MouseSimulator.MoveTo(targetX, targetY);
            }
        }

        /// <summary>
        /// 对点击目标坐标施加高斯随机偏移，模拟真人点击不精确
        /// 使用 Box-Muller 变换生成高斯分布随机数
        /// </summary>
        private static void ApplyGaussianOffset(ref int x, ref int y)
        {
            int screenWidth = MouseSimulator.GetScreenWidth();
            if (screenWidth <= 0) return;

            double stdDev = screenWidth * 0.012d;
            double maxOffset = stdDev * 2.5d;

            double u1, u2;
            lock (_humanizeRandomSync)
            {
                u1 = 1d - _humanizeRandom.NextDouble();
                u2 = _humanizeRandom.NextDouble();
            }

            double z0 = Math.Sqrt(-2d * Math.Log(u1)) * Math.Cos(2d * Math.PI * u2);
            double z1 = Math.Sqrt(-2d * Math.Log(u1)) * Math.Sin(2d * Math.PI * u2);

            double offsetX = z0 * stdDev;
            double offsetY = z1 * stdDev;

            offsetX = Math.Max(-maxOffset, Math.Min(maxOffset, offsetX));
            offsetY = Math.Max(-maxOffset, Math.Min(maxOffset, offsetY));

            x += (int)Math.Round(offsetX);
            y += (int)Math.Round(offsetY);
        }

        /// <summary>
        /// 鼠标点击结束回合按钮
        /// </summary>
        private static IEnumerator<float> MouseEndTurn()
        {
            InputHook.Simulating = true;
            if (!GameObjectFinder.GetEndTurnButtonScreenPos(out var x, out var y))
            {
                // 坐标获取失败，直接用反射结束回合
                EndTurn();
                yield return 0.3f;
                _coroutine.SetResult("OK:END_TURN");
                yield break;
            }

            // 拟人化：结束回合前可能犹豫
            var humConfig = GetHumanizerConfigSnapshot();
            if (humConfig != null && humConfig.Enabled
                && ConstructedHumanizerPlanner.ShouldHesitateBeforeEndTurn(
                    humConfig.Intensity, NextHumanizeInt32(1, 100)))
            {
                // 先移动到按钮附近
                foreach (var w in MoveCursorConstructed(x, y, 10, 0.010f, false))
                    yield return w;

                // 停顿（犹豫）
                var hesitateMs = ConstructedHumanizerPlanner.GetEndTurnHesitationMs(
                    humConfig.Intensity, null);
                yield return hesitateMs / 1000f;

                // 小概率（5%）：移开再移回（"真的要结束吗"）
                if (NextHumanizeInt32(1, 100) <= 5)
                {
                    int screenWidth = MouseSimulator.GetScreenWidth();
                    int screenHeight = MouseSimulator.GetScreenHeight();
                    if (screenWidth > 0 && screenHeight > 0)
                    {
                        int driftX = x + NextHumanizeInt32(-80, 80);
                        int driftY = y + NextHumanizeInt32(30, 80);
                        driftX = Math.Max(0, Math.Min(screenWidth, driftX));
                        driftY = Math.Max(0, Math.Min(screenHeight, driftY));
                        foreach (var w in MoveCursorConstructed(driftX, driftY, 8, 0.010f, false))
                            yield return w;
                        yield return GetHumanizePauseSeconds(150, 350);
                    }

                    // 重新获取按钮位置并移回
                    if (GameObjectFinder.GetEndTurnButtonScreenPos(out x, out y))
                    {
                        foreach (var w in MoveCursorConstructed(x, y, 10, 0.010f, false))
                            yield return w;
                    }
                }

                // 点击
                MouseSimulator.LeftDown();
                yield return 0.05f;
                MouseSimulator.LeftUp();
                yield return 0.15f;
            }
            else
            {
                // 无犹豫：原始逻辑
                MouseSimulator.MoveTo(x, y);
                yield return 0.05f;
                MouseSimulator.LeftDown();
                yield return 0.05f;
                MouseSimulator.LeftUp();
                yield return 0.15f;
            }

            // 鼠标点击后追加反射调用，确保结束回合生效
            EndTurn();
            yield return 0.3f;
            _coroutine.SetResult("OK:END_TURN");
        }

        /// <summary>
        /// 地标激活：
        /// - 无目标地标：只单击一次地标
        /// - 有目标地标：鼠标按住地标拖到目标后松开
        /// </summary>
        private static IEnumerator<float> MouseUseLocation(int entityId, int targetEntityId, int targetHeroSide)
        {
            InputHook.Simulating = true;

            int sx = 0, sy = 0;
            bool gotSource = false;
            for (int retry = 0; retry < 4; retry++)
            {
                if (GameObjectFinder.GetEntityScreenPos(entityId, out sx, out sy))
                {
                    gotSource = true;
                    break;
                }
                yield return 0.12f;
            }
            if (!gotSource)
            {
                _coroutine.SetResult("FAIL:USE_LOCATION:pos:" + entityId);
                yield break;
            }

            // 无目标地标：单击一次，不做额外点击
            if (targetEntityId <= 0)
            {
                foreach (var w in MoveCursorConstructed(sx, sy, 10, 0.01f, false)) yield return w;
                MouseSimulator.LeftDown();
                yield return 0.06f;
                MouseSimulator.LeftUp();
                yield return 0.25f;
                _coroutine.SetResult("OK:USE_LOCATION:" + entityId + ":click");
                yield break;
            }

            // 有目标地标：必须拖动到目标后松手
            bool gotTarget = false;
            int tx = 0, ty = 0;
            for (int retry = 0; retry < 6 && !gotTarget; retry++)
            {
                if (targetHeroSide == 0)
                    gotTarget = GameObjectFinder.GetHeroScreenPos(true, out tx, out ty);
                else if (targetHeroSide == 1)
                    gotTarget = GameObjectFinder.GetHeroScreenPos(false, out tx, out ty);

                if (!gotTarget)
                    gotTarget = GameObjectFinder.GetEntityScreenPos(targetEntityId, out tx, out ty);

                if (!gotTarget && retry < 5)
                    yield return 0.10f;
            }
            if (!gotTarget)
            {
                _coroutine.SetResult("FAIL:USE_LOCATION:target_pos:" + targetEntityId);
                yield break;
            }

            foreach (var w in MoveCursorConstructed(sx, sy, 10, 0.01f, false)) yield return w;
            MouseSimulator.LeftDown();
            yield return 0.08f;
            foreach (var wait in MaybePreviewAlternateTarget(targetEntityId, targetHeroSide, true)) yield return wait;
            foreach (var w in MoveCursorConstructed(tx, ty, 16, 0.012f, true)) yield return w;
            MouseSimulator.LeftUp();
            yield return 0.28f;

            _coroutine.SetResult("OK:USE_LOCATION:" + entityId + ":drag");
        }

        /// <summary>
        /// 右键取消（通过移到屏幕边缘+右键模拟）
        /// </summary>
        private static IEnumerator<float> MouseCancel()
        {
            InputHook.Simulating = true;
            // 暂无右键hook，用移到屏幕外+释放代替
            MouseSimulator.LeftUp();
            yield return 0.2f;
            _coroutine.SetResult("OK:CANCEL");
        }

        /// <summary>
        /// 混合出牌：API抓取卡牌 + 鼠标模拟拖动/释放/选目标
        /// 阶段1: 通过API抓取卡牌（让游戏内部进入持有卡牌状态）
        /// 阶段2: 鼠标模拟拖动到目标位置
        /// 阶段3: 鼠标模拟释放（松手出牌）
        /// 阶段4: 如有战吼目标，鼠标模拟点击目标
        /// </summary>
        private static IEnumerator<float> MousePlayCard(int entityId, int targetEntityId, int position, int targetHeroSide, bool sourceIsMinionCard)
        {
            InputHook.Simulating = true;
            var gsBeforePlay = GetGameState();
            var hasBeforeHand = TryReadFriendlyHandEntityIds(gsBeforePlay, out var beforeHandIds);
            var sourceCardId = ResolveEntityCardId(gsBeforePlay, entityId);
            var sourceZonePosition = ResolveEntityZonePosition(gsBeforePlay, entityId);
            var guardRecentDrawCard = ShouldUseRecentDrawPlayGuard(entityId);

            // 等待手牌位置稳定（抽牌动画完成）
            int sourceX = 0, sourceY = 0;
            bool positionStable = false;
            var sourcePosRetryLimit = guardRecentDrawCard ? 12 : 8;
            for (int retry = 0; retry < sourcePosRetryLimit && !positionStable; retry++)
            {
                if (!GameObjectFinder.GetEntityScreenPos(entityId, out var cx, out var cy))
                {
                    yield return 0.05f;
                    continue;
                }

                var recentDrawInteractive = true;
                if (guardRecentDrawCard)
                {
                    var gsCurrent = retry == 0 ? gsBeforePlay : GetGameState();
                    if (TryReadRecentDrawCardInteractive(entityId, gsCurrent, out var interactive))
                        recentDrawInteractive = interactive;
                }

                if (retry > 0
                    && recentDrawInteractive
                    && Math.Abs(cx - sourceX) < 5
                    && Math.Abs(cy - sourceY) < 5)
                {
                    positionStable = true;
                }
                sourceX = cx;
                sourceY = cy;
                yield return 0.05f;
            }

            if (!positionStable)
            {
                if (guardRecentDrawCard
                    && TryReadRecentDrawCardInteractive(entityId, GetGameState(), out var interactive)
                    && !interactive)
                {
                    AppendActionTrace("PLAY(mouse) recent draw card still not interactive entity=" + entityId);
                }
                _coroutine.SetResult("FAIL:PLAY:source_pos:" + entityId);
                yield break;
            }

            // ========== 阶段1: 抓取卡牌 ==========
            // 优先通过 API 抓取（不依赖屏幕坐标，最可靠）
            bool grabbedViaAPI = false;
            string apiGrabMethod = "none";
            string apiGrabDetail = string.Empty;
            int apiHeldEntityId = 0;
            const int maxApiGrabAttempts = 3;

            for (int attempt = 1; attempt <= maxApiGrabAttempts; attempt++)
            {
                if (TryGrabCardViaAPI(
                        entityId,
                        sourceZonePosition,
                        out apiGrabMethod,
                        out apiHeldEntityId,
                        out apiGrabDetail))
                {
                    grabbedViaAPI = true;
                    break;
                }

                TryResetHeldCard();
                yield return 0.06f;
            }
            if (grabbedViaAPI)
            {
                AppendActionTrace(
                    "API grabbed card expected=" + entityId
                    + " held=" + apiHeldEntityId
                    + " zonePos=" + sourceZonePosition
                    + " cardId=" + sourceCardId
                    + " via=" + apiGrabMethod);
            }
            else
            {
                AppendActionTrace(
                    "API grab failed expected=" + entityId
                    + " zonePos=" + sourceZonePosition
                    + " cardId=" + sourceCardId
                    + " detail=" + apiGrabDetail);
            }

            if (!grabbedViaAPI)
            {
                TryResetHeldCard();
                _coroutine.SetResult("FAIL:PLAY:grab_api_failed:" + entityId + ":" + (string.IsNullOrWhiteSpace(apiGrabDetail) ? "unknown" : apiGrabDetail));
                yield break;
            }

            if (grabbedViaAPI)
            {
                // API 抓取成功，需要同步设置鼠标按下状态
                // 游戏每帧检查 Input.GetMouseButton(0) 来判断玩家是否还在持有卡牌
                // 如果不设置 LeftDown，游戏会认为玩家立即松手导致卡牌弹回
                foreach (var wait in MoveCursorConstructed(sourceX, sourceY, 10, 0.010f, false)) yield return wait;
                MouseSimulator.LeftDown();
                yield return 0.08f;
            }
            else
            {
                // API 失败，回退到鼠标点击手牌位置抓取
                _coroutine.SetResult("FAIL:PLAY:grab_api_failed:" + entityId + ":mouse_grab_disabled");
                yield break;
            }

            // ========== 阶段2+3: 拖动并释放 ==========
            if (targetEntityId > 0)
            {
                if (sourceIsMinionCard)
                {
                    // 指向性随从（战吼选目标）必须先落位，再点击目标。
                    int totalMinions = GetFriendlyMinionCount();
                    if (!GameObjectFinder.GetBoardDropZoneScreenPos(position, totalMinions, out var dx, out var dy))
                    {
                        MouseSimulator.LeftUp();
                        _coroutine.SetResult("FAIL:PLAY:drop_pos");
                        yield break;
                    }

                    foreach (var w in MoveCursorConstructed(dx, dy, 15, 0.012f, true)) yield return w;
                    MouseSimulator.LeftUp();
                    yield return 0.18f;

                    bool gotTarget = false;
                    int tx = 0, ty = 0;
                    for (int retry = 0; retry < 6 && !gotTarget; retry++)
                    {
                        if (targetHeroSide == 0)
                            gotTarget = GameObjectFinder.GetHeroScreenPos(true, out tx, out ty);
                        else if (targetHeroSide == 1)
                            gotTarget = GameObjectFinder.GetHeroScreenPos(false, out tx, out ty);

                        if (!gotTarget)
                            gotTarget = GameObjectFinder.GetEntityScreenPos(targetEntityId, out tx, out ty);

                        if (!gotTarget && retry < 5)
                            yield return 0.1f;
                    }

                    if (!gotTarget)
                    {
                        _coroutine.SetResult("FAIL:PLAY:target_pos:" + targetEntityId);
                        yield break;
                    }

                    foreach (var wait in MaybePreviewAlternateTarget(targetEntityId, targetHeroSide, false)) yield return wait;
                    foreach (var wait in MoveCursorConstructed(tx, ty, 10, 0.010f, false)) yield return wait;
                    yield return 0.05f;
                    MouseSimulator.LeftDown();
                    yield return 0.04f;
                    MouseSimulator.LeftUp();
                }
                else
                {
                    // 指向性法术：直接从手牌拖到目标身上松手释放。
                    // 炉石中法术是拖到目标后松手，不需要先放到场上再点目标。
                    bool gotTarget = false;
                    int tx = 0, ty = 0;
                    for (int retry = 0; retry < 6 && !gotTarget; retry++)
                    {
                        if (targetHeroSide == 0)
                            gotTarget = GameObjectFinder.GetHeroScreenPos(true, out tx, out ty);
                        else if (targetHeroSide == 1)
                            gotTarget = GameObjectFinder.GetHeroScreenPos(false, out tx, out ty);

                        if (!gotTarget)
                            gotTarget = GameObjectFinder.GetEntityScreenPos(targetEntityId, out tx, out ty);

                        if (!gotTarget && retry < 5)
                            yield return 0.1f;
                    }

                    if (!gotTarget)
                    {
                        MouseSimulator.LeftUp();
                        _coroutine.SetResult("FAIL:PLAY:target_pos:" + targetEntityId);
                        yield break;
                    }

                    // 保持按住，直接拖到目标位置后松手
                    foreach (var wait in MaybePreviewAlternateTarget(targetEntityId, targetHeroSide, true)) yield return wait;
                    foreach (var w in MoveCursorConstructed(tx, ty, 15, 0.012f, true)) yield return w;
                    MouseSimulator.LeftUp();
                }
            }
            else
            {
                // 无目标的牌（随从/非指向法术）：拖到场上放置位置
                int totalMinions = GetFriendlyMinionCount();
                if (!GameObjectFinder.GetBoardDropZoneScreenPos(position, totalMinions, out var dx, out var dy))
                {
                    _coroutine.SetResult("FAIL:PLAY:drop_pos");
                    yield break;
                }
                foreach (var w in MoveCursorConstructed(dx, dy, 15, 0.012f, true)) yield return w;
                MouseSimulator.LeftUp();
            }

            yield return 0.3f;

            // 防止“目标没点上但仍返回 OK”导致后续点击被当作补选目标。
            // 若牌还在手里，说明本次出牌并未真正提交。
            var gsAfterPlay = GetGameState();
            var hasAfterHand = TryReadFriendlyHandEntityIds(gsAfterPlay, out var afterHandIds);
            var resolvedStillInHand = false;
            var stillInHand = false;
            if (gsAfterPlay != null
                && TryIsEntityInFriendlyHand(gsAfterPlay, entityId, out stillInHand))
            {
                resolvedStillInHand = true;
            }

            if (resolvedStillInHand && stillInHand)
            {
                AppendActionTrace(
                    "PLAY still in hand after release entityId=" + entityId
                    + " targetId=" + targetEntityId
                    + " isMinionCard=" + sourceIsMinionCard
                    + " cardId=" + sourceCardId);
                _coroutine.SetResult("FAIL:PLAY:still_in_hand:" + entityId);
                yield break;
            }

            if (resolvedStillInHand
                && !stillInHand
                && hasBeforeHand
                && hasAfterHand
                && beforeHandIds.Contains(entityId))
            {
                var removed = beforeHandIds.Where(id => !afterHandIds.Contains(id)).ToList();
                if (removed.Count == 1 && removed[0] != entityId)
                {
                    _coroutine.SetResult("FAIL:PLAY:source_mismatch:" + entityId + ":" + removed[0]);
                    yield break;
                }

                if (removed.Count > 1 && !removed.Contains(entityId))
                {
                    _coroutine.SetResult("FAIL:PLAY:source_mismatch:" + entityId + ":" + removed[0]);
                    yield break;
                }
            }

            yield return 0.2f;
            _coroutine.SetResult("OK:PLAY:" + entityId + ":api_grab");
        }

        private static IEnumerator<float> MousePlayCardByMouseFlow(int entityId, int targetEntityId, int position, int targetHeroSide, bool sourceUsesBoardDrop)
        {
            InputHook.Simulating = true;
            var gsBeforePlay = GetGameState();
            var hasBeforeHand = TryReadFriendlyHandEntityIds(gsBeforePlay, out var beforeHandIds);
            var sourceCardId = ResolveEntityCardId(gsBeforePlay, entityId);
            var sourceZoneTagBeforePlay = ResolveEntityZoneTag(gsBeforePlay, entityId);
            var sourceZonePosition = ResolveEntityZonePosition(gsBeforePlay, entityId);
            var guardRecentDrawCard = ShouldUseRecentDrawPlayGuard(entityId);

            // 等待手牌位置稳定（抽牌动画完成），使用左边缘坐标
            int sourceX = 0, sourceY = 0;
            bool positionStable = false;
            var sourcePosRetryLimit = guardRecentDrawCard ? 12 : 8;
            for (int retry = 0; retry < sourcePosRetryLimit && !positionStable; retry++)
            {
                if (!GameObjectFinder.GetHandCardLeftEdgeScreenPos(entityId, out var cx, out var cy))
                {
                    yield return 0.05f;
                    continue;
                }

                var recentDrawInteractive = true;
                if (guardRecentDrawCard)
                {
                    var gsCurrent = retry == 0 ? gsBeforePlay : GetGameState();
                    if (TryReadRecentDrawCardInteractive(entityId, gsCurrent, out var interactive))
                        recentDrawInteractive = interactive;
                }

                if (retry > 0
                    && recentDrawInteractive
                    && Math.Abs(cx - sourceX) < 5
                    && Math.Abs(cy - sourceY) < 5)
                {
                    positionStable = true;
                }
                sourceX = cx;
                sourceY = cy;
                yield return 0.05f;
            }

            if (!positionStable)
            {
                if (guardRecentDrawCard
                    && TryReadRecentDrawCardInteractive(entityId, GetGameState(), out var interactive)
                    && !interactive)
                {
                    AppendActionTrace("PLAY(mouse-flow) recent draw card still not interactive entity=" + entityId);
                }
                _coroutine.SetResult("FAIL:PLAY:source_pos:" + entityId);
                yield break;
            }

            // 纯鼠标抓牌：移动到左边缘坐标，按下鼠标，等待卡牌被抬起
            bool grabbed = false;
            var inputMgr = GetSingleton(_inputMgrType);
            for (int attempt = 1; attempt <= 3 && !grabbed; attempt++)
            {
                foreach (var wait in MoveCursorConstructed(sourceX, sourceY, 10, 0.010f, false)) yield return wait;
                yield return 0.04f;
                MouseSimulator.LeftDown();
                yield return 0.12f;

                // 检测卡牌是否被游戏抬起
                for (int poll = 0; poll < 8; poll++)
                {
                    if (inputMgr != null && TryGetHeldCardEntityId(inputMgr, out var heldId) && heldId > 0)
                    {
                        if (heldId == entityId)
                        {
                            grabbed = true;
                            AppendActionTrace("PLAY(mouse) grabbed via left_edge attempt=" + attempt + " entity=" + entityId);
                        }
                        else
                        {
                            AppendActionTrace("PLAY(mouse) grabbed wrong card expected=" + entityId + " held=" + heldId + " attempt=" + attempt);
                            MouseSimulator.LeftUp();
                            yield return 0.06f;
                            TryResetHeldCard();
                            yield return 0.06f;
                            // 重新获取左边缘坐标（手牌可能位移了）
                            if (GameObjectFinder.GetHandCardLeftEdgeScreenPos(entityId, out var rx, out var ry))
                            {
                                sourceX = rx;
                                sourceY = ry;
                            }
                        }
                        break;
                    }
                    yield return 0.04f;
                }

                if (!grabbed)
                {
                    MouseSimulator.LeftUp();
                    yield return 0.06f;
                    TryResetHeldCard();
                    yield return 0.06f;
                    // 重新获取坐标
                    if (GameObjectFinder.GetHandCardLeftEdgeScreenPos(entityId, out var nx, out var ny))
                    {
                        sourceX = nx;
                        sourceY = ny;
                    }
                }
            }

            if (!grabbed)
            {
                AppendActionTrace("PLAY(mouse) grab failed after 3 attempts entity=" + entityId + " cardId=" + sourceCardId);
                TryResetHeldCard();
                _coroutine.SetResult("FAIL:PLAY:mouse_grab_failed:" + entityId);
                yield break;
            }

            bool targetConfirmationPending = false;
            bool targetConfirmationBusyObserved = false;
            PlayTargetConfirmationSnapshot lastTargetConfirmationSnapshot = null;
            var targetConfirmationState = PlayTargetConfirmationState.Pending;
            if (targetEntityId > 0)
            {
                if (sourceUsesBoardDrop)
                {
                    int totalMinions = GetFriendlyMinionCount();
                    if (!GameObjectFinder.GetBoardDropZoneScreenPos(position, totalMinions, out var dropX, out var dropY))
                    {
                        MouseSimulator.LeftUp();
                        TryResetHeldCard();
                        _coroutine.SetResult("FAIL:PLAY:drop_pos");
                        yield break;
                    }

                    foreach (var w in MoveCursorConstructed(dropX, dropY, 18, 0.012f, true)) yield return w;
                    MouseSimulator.LeftUp();
                    yield return 0.18f;

                    bool sourceLeftHand = false;
                    for (int retry = 0; retry < 12; retry++)
                    {
                        var gs = GetGameState();
                        if (gs != null && TryIsEntityInFriendlyHand(gs, entityId, out var inHandAfterDrop))
                        {
                            sourceLeftHand = !inHandAfterDrop;
                            if (sourceLeftHand)
                                break;
                        }

                        yield return 0.05f;
                    }

                    // 出牌后用运行时合法候选重解析目标，不能再盲信上游 target。
                    if (sourceLeftHand && targetHeroSide < 0)
                    {
                        var runtimeResolution = WaitForRuntimePlayTargetResolution(entityId, targetEntityId, 900);
                        if (runtimeResolution == null)
                        {
                            AppendActionTrace(
                                "PLAY(runtime-target) target_context_not_ready_timeout"
                                + " sourceEntityId=" + entityId
                                + " hintedTarget=" + targetEntityId);
                        }
                        else if (runtimeResolution.Mode == PlayRuntimeTargetMode.HandTarget)
                        {
                            if (!runtimeResolution.HasResolvedEntity)
                            {
                                AppendActionTrace(
                                    "PLAY(runtime-target) " + (runtimeResolution.FailureReason ?? "hand_target_detected_but_no_match")
                                    + " sourceEntityId=" + entityId
                                    + " hintedTarget=" + targetEntityId
                                    + " candidates=" + (runtimeResolution.CandidateSummary ?? string.Empty));
                                TryResetHeldCard();
                                _coroutine.SetResult("FAIL:PLAY:hand_target_unresolved:" + entityId + ":" + targetEntityId);
                                yield break;
                            }

                            AppendActionTrace(
                                "PLAY(runtime-target) mode=" + runtimeResolution.Mode
                                + " sourceEntityId=" + entityId
                                + " hintedTarget=" + targetEntityId
                                + " resolvedTarget=" + runtimeResolution.ResolvedEntityId
                                + " reason=" + (runtimeResolution.MatchReason ?? string.Empty)
                                + " candidates=" + (runtimeResolution.CandidateSummary ?? string.Empty));
                            if (runtimeResolution.ResolvedEntityId != targetEntityId)
                            {
                                targetEntityId = runtimeResolution.ResolvedEntityId;
                            }
                        }
                    }

                    bool confirmed = false;
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        bool gotTarget = false;
                        int targetX = 0, targetY = 0;
                        for (int retry = 0; retry < 6 && !gotTarget; retry++)
                        {
                            if (TryResolvePlayTargetScreenPos(targetEntityId, targetHeroSide, out targetX, out targetY))
                            {
                                gotTarget = true;
                                break;
                            }

                            if (retry < 5)
                                yield return 0.1f;
                        }

                        if (!gotTarget)
                        {
                            TryResetHeldCard();
                            _coroutine.SetResult("FAIL:PLAY:target_pos:" + targetEntityId);
                            yield break;
                        }

                        foreach (var wait in MaybePreviewAlternateTarget(targetEntityId, targetHeroSide, false)) yield return wait;
                        foreach (var wait in MoveCursorConstructed(targetX, targetY, 10, 0.010f, false)) yield return wait;
                        yield return 0.05f;
                        MouseSimulator.LeftDown();
                        yield return 0.04f;
                        MouseSimulator.LeftUp();

                        for (int retry = 0; retry < 6; retry++)
                        {
                            if (!IsPlayTargetConfirmationPending(entityId))
                            {
                                confirmed = true;
                                break;
                            }

                            yield return 0.06f;
                        }

                        if (confirmed)
                            break;
                    }

                    targetConfirmationPending = !confirmed && sourceLeftHand && IsPlayTargetConfirmationPending(entityId);
                }
                else
                {
                    bool gotTarget = false;
                    int targetX = 0, targetY = 0;
                    for (int retry = 0; retry < 6 && !gotTarget; retry++)
                    {
                        if (TryResolvePlayTargetScreenPos(targetEntityId, targetHeroSide, out targetX, out targetY))
                        {
                            gotTarget = true;
                            break;
                        }

                        if (retry < 5)
                            yield return 0.1f;
                    }

                    if (!gotTarget)
                    {
                        MouseSimulator.LeftUp();
                        TryResetHeldCard();
                        _coroutine.SetResult("FAIL:PLAY:target_pos:" + targetEntityId);
                        yield break;
                    }

                    foreach (var wait in MaybePreviewAlternateTarget(targetEntityId, targetHeroSide, true)) yield return wait;
                    foreach (var w in MoveCursorConstructed(targetX, targetY, 18, 0.012f, true)) yield return w;
                    MouseSimulator.LeftUp();
                    yield return 0.14f;

                    targetConfirmationState = CapturePlayTargetConfirmationState(
                        entityId,
                        sourceZoneTagBeforePlay,
                        ref targetConfirmationBusyObserved,
                        out lastTargetConfirmationSnapshot,
                        finalObservation: false);
                    targetConfirmationPending = targetConfirmationState == PlayTargetConfirmationState.Pending;
                    if (targetConfirmationPending)
                    {
                        for (int attempt = 0; attempt < 2 && targetConfirmationPending; attempt++)
                        {
                            if (attempt > 0)
                            {
                                bool refreshed = false;
                                for (int retry = 0; retry < 4 && !refreshed; retry++)
                                {
                                    refreshed = TryResolvePlayTargetScreenPos(targetEntityId, targetHeroSide, out targetX, out targetY);
                                    if (!refreshed && retry < 3)
                                        yield return 0.08f;
                                }
                            }

                            foreach (var wait in MoveCursorConstructed(targetX, targetY, 10, 0.010f, false)) yield return wait;
                            yield return 0.04f;
                            MouseSimulator.LeftDown();
                            yield return 0.04f;
                            MouseSimulator.LeftUp();

                            for (int retry = 0; retry < 5; retry++)
                            {
                                targetConfirmationState = CapturePlayTargetConfirmationState(
                                    entityId,
                                    sourceZoneTagBeforePlay,
                                    ref targetConfirmationBusyObserved,
                                    out lastTargetConfirmationSnapshot,
                                    finalObservation: false);
                                if (targetConfirmationState != PlayTargetConfirmationState.Pending)
                                {
                                    break;
                                }

                                yield return 0.06f;
                            }

                            targetConfirmationPending = targetConfirmationState == PlayTargetConfirmationState.Pending;
                        }
                    }
                }
            }
            else
            {
                int releaseX = 0;
                int releaseY = 0;
                if (sourceUsesBoardDrop)
                {
                    int totalMinions = GetFriendlyMinionCount();
                    if (!GameObjectFinder.GetBoardDropZoneScreenPos(position, totalMinions, out releaseX, out releaseY))
                    {
                        MouseSimulator.LeftUp();
                        TryResetHeldCard();
                        _coroutine.SetResult("FAIL:PLAY:drop_pos");
                        yield break;
                    }
                }
                else if (!GameObjectFinder.GetPlayReleaseScreenPos(out releaseX, out releaseY))
                {
                    MouseSimulator.LeftUp();
                    TryResetHeldCard();
                    _coroutine.SetResult("FAIL:PLAY:drop_pos");
                    yield break;
                }

                foreach (var w in MoveCursorConstructed(releaseX, releaseY, 16, 0.012f, true)) yield return w;
                MouseSimulator.LeftUp();
            }

            yield return 0.25f;

            var gsAfterPlay = GetGameState();
            var hasAfterHand = TryReadFriendlyHandEntityIds(gsAfterPlay, out var afterHandIds);
            var resolvedStillInHand = false;
            var stillInHand = false;
            if (gsAfterPlay != null && TryIsEntityInFriendlyHand(gsAfterPlay, entityId, out stillInHand))
                resolvedStillInHand = true;

            if (resolvedStillInHand && stillInHand)
            {
                AppendActionTrace(
                    "PLAY(mouse) still in hand entityId=" + entityId
                    + " targetId=" + targetEntityId
                    + " usesBoardDrop=" + sourceUsesBoardDrop
                    + " cardId=" + sourceCardId);
                _coroutine.SetResult("FAIL:PLAY:still_in_hand:" + entityId);
                yield break;
            }

            if (resolvedStillInHand
                && !stillInHand
                && hasBeforeHand
                && hasAfterHand
                && beforeHandIds.Contains(entityId))
            {
                var removed = beforeHandIds.Where(id => !afterHandIds.Contains(id)).ToList();
                if (removed.Count == 1 && removed[0] != entityId)
                {
                    _coroutine.SetResult("FAIL:PLAY:source_mismatch:" + entityId + ":" + removed[0]);
                    yield break;
                }

                if (removed.Count > 1 && !removed.Contains(entityId))
                {
                    _coroutine.SetResult("FAIL:PLAY:source_mismatch:" + entityId + ":" + removed[0]);
                    yield break;
                }
            }

            if (targetEntityId > 0 && resolvedStillInHand && !stillInHand)
            {
                if (!sourceUsesBoardDrop)
                {
                    for (int retry = 0; retry < 4; retry++)
                    {
                        targetConfirmationState = CapturePlayTargetConfirmationState(
                            entityId,
                            sourceZoneTagBeforePlay,
                            ref targetConfirmationBusyObserved,
                            out lastTargetConfirmationSnapshot,
                            finalObservation: retry == 3);
                        if (targetConfirmationState != PlayTargetConfirmationState.Pending)
                            break;

                        yield return 0.05f;
                    }

                    lastTargetConfirmationSnapshot = EnsurePlayTargetConfirmationSourceState(
                        lastTargetConfirmationSnapshot,
                        entityId,
                        sourceZoneTagBeforePlay,
                        resolvedStillInHand,
                        stillInHand,
                        targetConfirmationBusyObserved);
                    if (targetConfirmationState != PlayTargetConfirmationState.Confirmed)
                        targetConfirmationState = PlayTargetConfirmation.Evaluate(lastTargetConfirmationSnapshot, finalObservation: true);

                    if (targetConfirmationState == PlayTargetConfirmationState.Failed)
                    {
                        AppendActionTrace(
                            "PLAY(mouse) target not confirmed entityId=" + entityId
                            + " targetId=" + targetEntityId
                            + " responseMode=" + DescribePlayTargetConfirmationResponseMode(lastTargetConfirmationSnapshot)
                            + " heldEntityId=" + DescribePlayTargetConfirmationHeldEntityId(lastTargetConfirmationSnapshot)
                            + " stillInHand=" + DescribePlayTargetConfirmationStillInHand(lastTargetConfirmationSnapshot)
                            + " zoneTag=" + DescribePlayTargetConfirmationZoneTag(lastTargetConfirmationSnapshot)
                            + " busyObserved=" + DescribePlayTargetConfirmationBusyObserved(lastTargetConfirmationSnapshot, targetConfirmationBusyObserved)
                            + " sourceUsesBoardDrop=" + sourceUsesBoardDrop
                            + " cardId=" + sourceCardId);
                        TryResetHeldCard();
                        _coroutine.SetResult("FAIL:PLAY:target_not_confirmed:" + entityId + ":" + targetEntityId);
                        yield break;
                    }
                }
                else
                {
                    if (!targetConfirmationPending)
                    {
                        for (int retry = 0; retry < 4; retry++)
                        {
                            if (!IsPlayTargetConfirmationPending(entityId))
                                break;

                            targetConfirmationPending = true;
                            yield return 0.05f;
                        }
                    }

                    if (targetConfirmationPending)
                    {
                        AppendActionTrace(
                            "PLAY(mouse) target not confirmed entityId=" + entityId
                            + " targetId=" + targetEntityId
                            + " usesBoardDrop=" + sourceUsesBoardDrop
                            + " cardId=" + sourceCardId);
                        TryResetHeldCard();
                        _coroutine.SetResult("FAIL:PLAY:target_not_confirmed:" + entityId + ":" + targetEntityId);
                        yield break;
                    }
                }
            }

            yield return 0.2f;
            _coroutine.SetResult("OK:PLAY:" + entityId + ":mouse_left_edge");
        }

        /// <summary>
        /// 当游戏处于手牌目标选择模式时，从合法目标列表中找到正确的手牌实体。
        /// 利用 GameState.DoesSelectedOptionHaveHandTarget() 判断是否为手牌选择模式，
        /// 利用 GetSelectedNetworkOption().Main.Targets 获取合法目标列表。
        /// 若当前 targetEntityId 不在合法目标中，按 cardId 匹配返回正确的手牌实体。
        /// 返回修正后的实体 ID，或 0 表示无需/无法修正。
        /// </summary>
        private static int TryCorrectHandTargetEntityId(int targetEntityId)
        {
            if (targetEntityId <= 0) return 0;

            try
            {
                var gs = GetGameState();
                if (gs == null) return 0;

                // 1. 检查游戏是否处于手牌选择模式
                if (!TryInvokeMethod(gs, "DoesSelectedOptionHaveHandTarget", Array.Empty<object>(), out var result))
                    return 0;
                if (!(result is bool hasHandTarget && hasHandTarget))
                    return 0;

                // 2. 获取合法目标列表
                if (!TryInvokeMethod(gs, "GetSelectedNetworkOption", Array.Empty<object>(), out var optionObj) || optionObj == null)
                    return 0;
                var main = GetFieldOrProp(optionObj, "Main");
                if (main == null) return 0;
                var targets = GetFieldOrProp(main, "Targets") as IEnumerable;
                if (targets == null) return 0;

                var validTargetIds = new List<int>();
                foreach (var t in targets)
                {
                    var idObj = GetFieldOrProp(t, "ID");
                    if (idObj is int id && id > 0)
                        validTargetIds.Add(id);
                }

                if (validTargetIds.Count == 0)
                    return 0;

                // 3. 如果 targetEntityId 已在合法目标中 → 无需修正
                if (validTargetIds.Contains(targetEntityId))
                    return 0;

                // 4. targetEntityId 不在合法目标中 → 按 cardId 匹配找替代
                var targetEntity = GetEntity(gs, targetEntityId);
                if (targetEntity == null) return 0;
                var targetCardId = (Invoke(targetEntity, "GetCardId")
                    ?? GetFieldOrProp(targetEntity, "CardId")
                    ?? GetFieldOrProp(targetEntity, "m_cardId")
                    ?? GetFieldOrProp(targetEntity, "m_cardID"))?.ToString();
                if (string.IsNullOrEmpty(targetCardId)) return 0;

                foreach (var validId in validTargetIds)
                {
                    var validEntity = GetEntity(gs, validId);
                    if (validEntity == null) continue;
                    var validCardId = (Invoke(validEntity, "GetCardId")
                        ?? GetFieldOrProp(validEntity, "CardId")
                        ?? GetFieldOrProp(validEntity, "m_cardId")
                        ?? GetFieldOrProp(validEntity, "m_cardID"))?.ToString();
                    if (string.Equals(targetCardId, validCardId, StringComparison.OrdinalIgnoreCase))
                        return validId;
                }
            }
            catch { }

            return 0;
        }

        private static PlayRuntimeTargetResolution TryResolveRuntimePlayTarget(object gameState, int targetEntityId)
        {
            if (gameState == null || targetEntityId <= 0)
                return null;

            var explicitHandTarget = HasSelectedOptionHandTargetSignal(gameState);
            var candidates = CollectPlayRuntimeTargetCandidates(gameState, out var rawChoiceType);
            if ((candidates == null || candidates.Count == 0) && !explicitHandTarget)
                return null;

            var hint = BuildRuntimeTargetHint(gameState, targetEntityId);
            var resolution = PlayRuntimeTargetResolver.Resolve(hint, candidates, explicitHandTarget, rawChoiceType);
            if (resolution != null)
                resolution.CandidateSummary = FormatRuntimeTargetCandidates(candidates);
            return resolution;
        }

        private static PlayRuntimeTargetResolution WaitForRuntimePlayTargetResolution(int sourceEntityId, int hintedTargetEntityId, int timeoutMs)
        {
            if (hintedTargetEntityId <= 0)
                return null;

            var deadline = Environment.TickCount + Math.Max(120, timeoutMs);
            while (Environment.TickCount - deadline < 0)
            {
                var gameState = GetGameState();
                var resolution = TryResolveRuntimePlayTarget(gameState, hintedTargetEntityId);
                if (resolution != null && resolution.Mode != PlayRuntimeTargetMode.Unknown)
                    return resolution;

                Thread.Sleep(40);
            }

            return null;
        }

        private static bool HasSelectedOptionHandTargetSignal(object gameState)
        {
            if (gameState == null)
                return false;

            if (!TryInvokeMethod(gameState, "DoesSelectedOptionHaveHandTarget", Array.Empty<object>(), out var result))
                return false;

            return result is bool hasHandTarget && hasHandTarget;
        }

        private static List<PlayRuntimeTargetCandidate> CollectPlayRuntimeTargetCandidates(object gameState, out string rawChoiceType)
        {
            rawChoiceType = string.Empty;
            var candidates = new List<PlayRuntimeTargetCandidate>();
            if (gameState == null)
                return candidates;

            AppendSelectedOptionTargets(gameState, candidates);

            if (TryBuildChoiceSnapshot(gameState, out var snapshot) && snapshot != null)
            {
                rawChoiceType = snapshot.RawChoiceType ?? string.Empty;
                if (snapshot.ChoiceEntityIds != null)
                {
                    foreach (var entityId in snapshot.ChoiceEntityIds)
                        TryAppendRuntimeTargetCandidate(gameState, entityId, candidates);
                }
            }

            return candidates;
        }

        private static void AppendSelectedOptionTargets(object gameState, List<PlayRuntimeTargetCandidate> candidates)
        {
            if (gameState == null || candidates == null)
                return;

            if (!TryInvokeMethod(gameState, "GetSelectedNetworkOption", Array.Empty<object>(), out var optionObj) || optionObj == null)
                return;

            var main = GetFieldOrProp(optionObj, "Main")
                ?? GetFieldOrProp(optionObj, "m_main")
                ?? Invoke(optionObj, "GetMain");
            if (main == null)
                return;

            var targets = GetFieldOrProp(main, "Targets") as IEnumerable
                ?? GetFieldOrProp(main, "m_targets") as IEnumerable
                ?? Invoke(main, "GetTargets") as IEnumerable;
            if (targets == null)
                return;

            foreach (var target in targets)
            {
                var entityId = ResolveOptionEntityId(target);
                if (entityId <= 0)
                    entityId = GetIntFieldOrProp(target, "Target");
                if (entityId <= 0)
                    entityId = GetIntFieldOrProp(target, "m_target");
                if (entityId <= 0)
                    entityId = GetIntFieldOrProp(target, "EntityID");
                if (entityId <= 0)
                    entityId = GetIntFieldOrProp(target, "ID");

                TryAppendRuntimeTargetCandidate(gameState, entityId, candidates);
            }
        }

        private static void TryAppendRuntimeTargetCandidate(object gameState, int entityId, List<PlayRuntimeTargetCandidate> candidates)
        {
            if (gameState == null || entityId <= 0 || candidates == null)
                return;

            if (candidates.Any(candidate => candidate != null && candidate.EntityId == entityId))
                return;

            candidates.Add(new PlayRuntimeTargetCandidate
            {
                EntityId = entityId,
                Zone = ResolveEntityZoneName(gameState, entityId),
                ZonePosition = ResolveEntityZonePosition(gameState, entityId),
                CardId = ResolveEntityCardId(gameState, entityId) ?? string.Empty
            });
        }

        private static PlayRuntimeTargetHint BuildRuntimeTargetHint(object gameState, int targetEntityId)
        {
            return new PlayRuntimeTargetHint
            {
                OriginalTargetEntityId = targetEntityId,
                CardId = ResolveEntityCardId(gameState, targetEntityId) ?? string.Empty,
                ZonePosition = ResolveEntityZonePosition(gameState, targetEntityId)
            };
        }

        private static string ResolveEntityZoneName(object gameState, int entityId)
        {
            if (gameState == null || entityId <= 0)
                return string.Empty;

            var entity = GetEntity(gameState, entityId);
            if (entity == null)
                return string.Empty;

            var zoneObj = Invoke(entity, "GetZone")
                ?? GetFieldOrProp(entity, "Zone")
                ?? GetFieldOrProp(entity, "m_zone")
                ?? GetFieldOrProp(entity, "ZONE");
            var zoneText = zoneObj?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(zoneText) && zoneText.Any(char.IsLetter))
                return zoneText.Trim();

            return MapZoneTagToName(ResolveEntityZoneTag(gameState, entityId));
        }

        private static string FormatRuntimeTargetCandidates(IEnumerable<PlayRuntimeTargetCandidate> candidates)
        {
            if (candidates == null)
                return string.Empty;

            return string.Join(";", candidates
                .Where(candidate => candidate != null && candidate.EntityId > 0)
                .Select(candidate =>
                    candidate.EntityId
                    + ":" + (candidate.Zone ?? string.Empty)
                    + ":" + candidate.ZonePosition
                    + ":" + (candidate.CardId ?? string.Empty)));
        }

        private static string MapZoneTagToName(int zoneTag)
        {
            switch (zoneTag)
            {
                case 1:
                    return "DECK";
                case 2:
                    return "HAND";
                case 3:
                    return "PLAY";
                case 4:
                    return "GRAVEYARD";
                case 5:
                    return "REMOVEDFROMGAME";
                case 6:
                    return "SETASIDE";
                case 7:
                    return "SECRET";
                default:
                    return string.Empty;
            }
        }

        private static bool TryResolvePlayTargetScreenPos(int targetEntityId, int targetHeroSide, out int x, out int y)
        {
            x = y = 0;
            if (targetHeroSide == 0)
                return GameObjectFinder.GetHeroScreenPos(true, out x, out y);
            if (targetHeroSide == 1)
                return GameObjectFinder.GetHeroScreenPos(false, out x, out y);
            return GameObjectFinder.GetEntityScreenPos(targetEntityId, out x, out y);
        }

        private static PlayTargetConfirmationState CapturePlayTargetConfirmationState(
            int sourceEntityId,
            int sourceZoneTagBeforePlay,
            ref bool busyObserved,
            out PlayTargetConfirmationSnapshot snapshot,
            bool finalObservation)
        {
            snapshot = CapturePlayTargetConfirmationSnapshot(sourceEntityId, sourceZoneTagBeforePlay, busyObserved);
            if (snapshot != null)
                busyObserved = snapshot.BusyObserved;

            return PlayTargetConfirmation.Evaluate(snapshot, finalObservation);
        }

        private static PlayTargetConfirmationSnapshot CapturePlayTargetConfirmationSnapshot(
            int sourceEntityId,
            int sourceZoneTagBeforePlay,
            bool busyObserved)
        {
            var snapshot = new PlayTargetConfirmationSnapshot
            {
                SourceEntityId = sourceEntityId,
                ZoneBeforePlay = sourceZoneTagBeforePlay,
                BusyObserved = busyObserved
            };

            try
            {
                var gs = GetGameState();
                if (gs != null)
                {
                    snapshot.BusyObserved = snapshot.BusyObserved || HasBusyPlayResolutionSignal(gs);

                    if (TryIsEntityInFriendlyHand(gs, sourceEntityId, out var stillInHand))
                    {
                        snapshot.SourceInHandResolved = true;
                        snapshot.StillInHand = stillInHand;
                    }

                    snapshot.ZoneTag = ResolveEntityZoneTag(gs, sourceEntityId);

                    if (TryInvokeMethod(gs, "GetResponseMode", Array.Empty<object>(), out var modeObj) && modeObj != null)
                        snapshot.ResponseMode = modeObj.ToString() ?? string.Empty;
                }

                var inputMgr = GetSingleton(_inputMgrType);
                if (TryGetHeldCardEntityId(inputMgr, out var heldEntityId))
                    snapshot.HeldEntityId = heldEntityId;
            }
            catch
            {
            }

            return snapshot;
        }

        private static PlayTargetConfirmationSnapshot EnsurePlayTargetConfirmationSourceState(
            PlayTargetConfirmationSnapshot snapshot,
            int sourceEntityId,
            int sourceZoneTagBeforePlay,
            bool resolvedStillInHand,
            bool stillInHand,
            bool busyObserved)
        {
            if (snapshot == null)
            {
                snapshot = new PlayTargetConfirmationSnapshot
                {
                    SourceEntityId = sourceEntityId,
                    ZoneBeforePlay = sourceZoneTagBeforePlay
                };
            }

            if (snapshot.SourceEntityId <= 0)
                snapshot.SourceEntityId = sourceEntityId;
            if (snapshot.ZoneBeforePlay <= 0)
                snapshot.ZoneBeforePlay = sourceZoneTagBeforePlay;
            if (!snapshot.SourceInHandResolved && resolvedStillInHand)
            {
                snapshot.SourceInHandResolved = true;
                snapshot.StillInHand = stillInHand;
            }

            if (busyObserved)
                snapshot.BusyObserved = true;

            return snapshot;
        }

        private static bool HasBusyPlayResolutionSignal(object gameState)
        {
            if (gameState == null)
                return false;

            if (TryInvokeBoolMethod(gameState, "IsResponsePacketBlocked", out var blocked) && blocked)
                return true;
            if (TryInvokeBoolMethod(gameState, "IsBusy", out var busy) && busy)
                return true;
            if (TryInvokeBoolMethod(gameState, "IsBlockingPowerProcessor", out var blockingPowerProcessor) && blockingPowerProcessor)
                return true;

            var ppType = _asm?.GetType("PowerProcessor");
            if (ppType == null)
                return false;

            var pp = GetSingleton(ppType);
            return pp != null
                && TryInvokeBoolMethod(pp, "IsRunning", out var running)
                && running;
        }

        private static string DescribePlayTargetConfirmationResponseMode(PlayTargetConfirmationSnapshot snapshot)
        {
            return snapshot != null && !string.IsNullOrWhiteSpace(snapshot.ResponseMode)
                ? snapshot.ResponseMode
                : "unknown";
        }

        private static string DescribePlayTargetConfirmationHeldEntityId(PlayTargetConfirmationSnapshot snapshot)
        {
            return snapshot != null ? snapshot.HeldEntityId.ToString() : "unknown";
        }

        private static string DescribePlayTargetConfirmationStillInHand(PlayTargetConfirmationSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.SourceInHandResolved)
                return "unknown";

            return snapshot.StillInHand ? "true" : "false";
        }

        private static string DescribePlayTargetConfirmationZoneTag(PlayTargetConfirmationSnapshot snapshot)
        {
            return snapshot != null ? snapshot.ZoneTag.ToString() : "unknown";
        }

        private static string DescribePlayTargetConfirmationBusyObserved(PlayTargetConfirmationSnapshot snapshot, bool busyObserved)
        {
            var observed = busyObserved;
            if (snapshot != null && snapshot.BusyObserved)
                observed = true;

            return observed ? "true" : "false";
        }

        private static bool IsPlayTargetConfirmationPending(int sourceEntityId)
        {
            try
            {
                var gs = GetGameState();
                if (gs == null)
                    return false;

                var inputMgr = GetSingleton(_inputMgrType);
                if (TryGetHeldCardEntityId(inputMgr, out var heldEntityId) && heldEntityId == sourceEntityId)
                    return true;

                if (TryInvokeMethod(gs, "GetResponseMode", Array.Empty<object>(), out var modeObj) && modeObj != null)
                {
                    if (IsTargetSelectionResponseMode(modeObj))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryUsesBoardDropForPlay(object gameState, int entityId, out bool usesBoardDrop)
        {
            usesBoardDrop = false;
            if (gameState == null || entityId <= 0)
                return false;

            try
            {
                var entity = GetEntity(gameState, entityId);
                if (entity == null)
                    return false;

                var ctx = ReflectionContext.Instance;
                if (!ctx.Init())
                    return false;

                var cardTypeTag = ctx.GetTagValue(entity, "CARDTYPE");
                if (cardTypeTag <= 0)
                    return false;

                try
                {
                    var cardTypeEnum = ctx.AsmCSharp?.GetType("TAG_CARDTYPE");
                    if (cardTypeEnum != null)
                    {
                        var minionValue = Convert.ToInt32(Enum.Parse(cardTypeEnum, "MINION", true));
                        var locationValue = Convert.ToInt32(Enum.Parse(cardTypeEnum, "LOCATION", true));
                        usesBoardDrop = cardTypeTag == minionValue || cardTypeTag == locationValue;
                        return true;
                    }
                }
                catch
                {
                }

                usesBoardDrop = cardTypeTag == 4 || cardTypeTag == 39;
                return true;
            }
            catch
            {
            }

            return false;
        }

        private static bool TryIsMinionCardEntity(object gameState, int entityId, out bool isMinion)
        {
            isMinion = false;
            if (gameState == null || entityId <= 0)
                return false;

            try
            {
                var entity = GetEntity(gameState, entityId);
                if (entity == null)
                    return false;

                var ctx = ReflectionContext.Instance;
                if (!ctx.Init())
                    return false;

                var cardTypeTag = ctx.GetTagValue(entity, "CARDTYPE");
                if (cardTypeTag > 0)
                {
                    try
                    {
                        var cardTypeEnum = ctx.AsmCSharp?.GetType("TAG_CARDTYPE");
                        if (cardTypeEnum != null)
                        {
                            var minionValue = Convert.ToInt32(Enum.Parse(cardTypeEnum, "MINION", true));
                            isMinion = cardTypeTag == minionValue;
                            return true;
                        }
                    }
                    catch
                    {
                    }

                    // TAG_CARDTYPE 不可用时退回常量：MINION 通常为 4
                    isMinion = cardTypeTag == 4;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>
        /// 可交易：抓取手牌并拖拽到牌库位置后松手。
        /// </summary>
        private static IEnumerator<float> MouseTradeCard(int entityId)
        {
            InputHook.Simulating = true;

            var gsBeforeTrade = GetGameState();
            var sourceZonePosition = ResolveEntityZonePosition(gsBeforeTrade, entityId);
            bool grabbedViaAPI = TryGrabCardViaAPI(entityId, sourceZonePosition, out var apiGrabMethod, out var apiHeldEntityId, out var apiGrabDetail);
            if (grabbedViaAPI)
            {
                AppendActionTrace(
                    "API grabbed card expected=" + entityId
                    + " held=" + apiHeldEntityId
                    + " zonePos=" + sourceZonePosition
                    + " via=" + apiGrabMethod);
            }
            else
            {
                AppendActionTrace(
                    "API grab failed expected=" + entityId
                    + " zonePos=" + sourceZonePosition
                    + " detail=" + apiGrabDetail);
                TryResetHeldCard();
                _coroutine.SetResult("FAIL:TRADE:grab_api_failed:" + entityId + ":" + (string.IsNullOrWhiteSpace(apiGrabDetail) ? "unknown" : apiGrabDetail));
                yield break;
            }

            if (grabbedViaAPI)
            {
                int midX = MouseSimulator.GetScreenWidth() / 2;
                int midY = MouseSimulator.GetScreenHeight() / 2;
                MouseSimulator.MoveTo(midX, midY);
                MouseSimulator.LeftDown();
                yield return 0.12f;
            }
            else
            {
                _coroutine.SetResult("FAIL:TRADE:grab_api_failed:" + entityId + ":mouse_grab_disabled");
                yield break;
            }

            if (!GameObjectFinder.GetFriendlyDeckScreenPos(out var dx, out var dy))
            {
                MouseSimulator.LeftUp();
                _coroutine.SetResult("FAIL:TRADE:deck_pos");
                yield break;
            }

            foreach (var w in SmoothMove(dx, dy, 18)) yield return w;
            MouseSimulator.LeftUp();
            yield return 0.45f;

            _coroutine.SetResult("OK:TRADE:" + entityId + ":api_grab");
        }

        /// <summary>
        /// 通过 InputManager API 抓取手牌中的卡牌
        /// 调用游戏内部的卡牌点击处理，让游戏进入"持有卡牌"状态
        /// 这比屏幕坐标定位更可靠，因为不依赖视觉层的 Transform 位置
        /// </summary>
        private static bool TryGrabCardViaAPI(int entityId)
        {
            return TryGrabCardViaAPI(entityId, 0, out _, out _, out _);
        }

        private static bool TryGrabCardViaAPI(
            int entityId,
            int sourceZonePosition,
            out string methodUsed,
            out int heldEntityId,
            out string detail)
        {
            methodUsed = string.Empty;
            heldEntityId = 0;
            detail = string.Empty;
            try
            {
                if (!EnsureTypes()) return false;

                var gs = GetGameState();
                if (gs == null)
                {
                    detail = "no_gamestate";
                    return false;
                }
                if (!TryIsEntityInFriendlyHand(gs, entityId, out var inHand) || !inHand)
                {
                    detail = "source_not_in_hand";
                    return false;
                }

                var entity = GetEntity(gs, entityId);
                if (entity == null)
                {
                    detail = "source_entity_missing";
                    return false;
                }

                // 获取 Card 对象（InputManager 的方法需要 Card 或 Entity）
                var handCard = ResolveFriendlyHandCardObject(gs, entityId);
                var entityCard = Invoke(entity, "GetCard");
                var card = handCard ?? entityCard;
                var cardObjectCandidates = new List<object>();
                AddDistinctObjectCandidate(cardObjectCandidates, handCard);
                AddDistinctObjectCandidate(cardObjectCandidates, entityCard);
                AddDistinctObjectCandidate(cardObjectCandidates, card);

                var inputMgr = GetSingleton(_inputMgrType);
                if (inputMgr == null)
                {
                    detail = "input_manager_missing";
                    return false;
                }

                AppendActionTrace(
                    "API grab begin entityId=" + entityId
                    + " zonePos=" + sourceZonePosition
                    + " handCardType=" + DescribeObjectType(handCard)
                    + " entityCardType=" + DescribeObjectType(entityCard)
                    + " cardCandidates=" + string.Join(",", cardObjectCandidates.Select(DescribeObjectType).ToArray()));

                if (TryGrabCardViaCurrentClientFlow(
                        card,
                        inputMgr,
                        entityId,
                        sourceZonePosition,
                        out var currentMethodUsed,
                        out heldEntityId,
                        out var currentClientGrabDetail))
                {
                    methodUsed = currentMethodUsed;
                    detail = currentClientGrabDetail;
                    return true;
                }

                var cardGrabDetail = cardObjectCandidates.Count == 0 ? "card_object_missing" : string.Empty;
                var cardGrabFailures = new List<string>();
                foreach (var cardCandidate in cardObjectCandidates)
                {
                    if (TryGrabCardViaCardObject(
                            cardCandidate,
                            inputMgr,
                            entityId,
                            sourceZonePosition,
                            out var cardMethodUsed,
                            out heldEntityId,
                            out var candidateGrabDetail))
                    {
                        methodUsed = cardMethodUsed;
                        detail = candidateGrabDetail;
                        return true;
                    }

                    cardGrabFailures.Add(DescribeObjectType(cardCandidate) + ":" + candidateGrabDetail);
                }

                if (!string.IsNullOrWhiteSpace(currentClientGrabDetail))
                {
                    cardGrabFailures.Insert(0, "current_client:" + currentClientGrabDetail);
                }

                if (cardGrabFailures.Count > 0)
                {
                    cardGrabDetail = "card_pickup_failed:" + string.Join(" | ", cardGrabFailures.ToArray());
                    AppendActionTrace(
                        "API card-object grab failed entityId=" + entityId
                        + " zonePos=" + sourceZonePosition
                        + " detail=" + cardGrabDetail);
                }

                // 尝试多种 InputManager 方法来"抓取"手牌
                // 炉石不同版本可能使用不同的方法名
                var grabMethods = new[]
                {
                    "HandleClickOnCardInHand",
                    "GrabCard",
                    "PickUpCard",
                    "HandleCardClicked",
                    "OnCardClicked"
                };

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                string lastFailure = string.IsNullOrWhiteSpace(cardGrabDetail) ? "no_grab_method" : cardGrabDetail;
                var gameObject = ResolveGrabGameObject(entity, entityCard ?? handCard ?? card);
                var firstArgCandidates = new List<object>();
                AddDistinctObjectCandidate(firstArgCandidates, handCard);
                AddDistinctObjectCandidate(firstArgCandidates, entityCard);
                AddDistinctObjectCandidate(firstArgCandidates, card);
                AddDistinctObjectCandidate(firstArgCandidates, entity);
                AddDistinctObjectCandidate(firstArgCandidates, gameObject);
                var inputMgrFailures = new List<string>();

                foreach (var methodName in grabMethods)
                {
                    var methods = _inputMgrType.GetMethods(flags)
                        .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    if (methods.Length == 0)
                    {
                        inputMgrFailures.Add(methodName + ":method_not_found");
                        continue;
                    }

                    foreach (var mi in methods)
                    {
                        var parameters = mi.GetParameters();
                        var signature = DescribeParameters(parameters);
                        if (parameters.Length == 0)
                        {
                            lastFailure = "no_parameters:" + methodName;
                            inputMgrFailures.Add(methodName + "[" + signature + "]:" + lastFailure);
                            continue;
                        }

                        var invoked = false;
                        foreach (var firstArg in firstArgCandidates)
                        {
                            if (!TryBuildGrabArgs(parameters, firstArg, sourceZonePosition, out var args))
                                continue;

                            invoked = true;
                            var attemptKey = methodName + "[" + signature + "]<" + DescribeObjectType(firstArg) + ">";
                            try
                            {
                                mi.Invoke(inputMgr, args);

                                var holdStatus = WaitForHeldCardAfterGrab(inputMgr, entityId, out heldEntityId);
                                if (holdStatus == HeldCardWaitStatus.Expected)
                                {
                                    methodUsed = methodName;
                                    detail = "ok";
                                    AppendActionTrace(
                                        "API grabbed card entityId=" + entityId
                                        + " via " + methodName
                                        + " zonePos=" + sourceZonePosition);
                                    return true;
                                }

                                if (holdStatus == HeldCardWaitStatus.Mismatch)
                                {
                                    lastFailure = "held_mismatch:" + methodName + ":" + heldEntityId;
                                    inputMgrFailures.Add(attemptKey + ":" + lastFailure);
                                    AppendActionTrace(
                                        "API grab mismatch expected=" + entityId
                                        + " actual=" + heldEntityId
                                        + " via " + methodName
                                        + " zonePos=" + sourceZonePosition);
                                    TryResetHeldCard();
                                    break;
                                }

                                lastFailure = "held_card_not_detected:" + methodName;
                                inputMgrFailures.Add(attemptKey + ":" + lastFailure);
                            }
                            catch (Exception ex)
                            {
                                lastFailure = "invoke_failed:" + methodName + ":" + SimplifyException(ex);
                                inputMgrFailures.Add(attemptKey + ":" + lastFailure);
                            }
                        }

                        if (!invoked)
                        {
                            lastFailure = "no_compatible_overload:" + methodName;
                            inputMgrFailures.Add(methodName + "[" + signature + "]:" + lastFailure);
                        }

                        if (Environment.TickCount == int.MinValue)
                        try
                        {
                            // 构造参数：优先传 Card，然后试 Entity
                            var args = BuildArgs(parameters, card ?? entity, 0, 0, sourceZonePosition);
                            mi.Invoke(inputMgr, args);

                            // 验证是否成功进入持有状态
                            if (TryGetHeldCardEntityId(inputMgr, out heldEntityId))
                            {
                                if (heldEntityId == entityId)
                                {
                                    methodUsed = methodName;
                                    detail = "ok";
                                    AppendActionTrace(
                                        "API grabbed card entityId=" + entityId
                                        + " via " + methodName
                                        + " zonePos=" + sourceZonePosition);
                                    return true;
                                }

                                lastFailure = "held_mismatch:" + methodName + ":" + heldEntityId;
                                AppendActionTrace(
                                    "API grab mismatch expected=" + entityId
                                    + " actual=" + heldEntityId
                                    + " via " + methodName
                                    + " zonePos=" + sourceZonePosition);
                                if (!TryResetHeldCard())
                                    continue;
                                continue;
                            }

                            lastFailure = "held_card_not_detected:" + methodName;
                        }
                        catch (Exception ex)
                        {
                            // 该方法签名不兼容，尝试下一个
                            lastFailure = "invoke_failed:" + methodName + ":" + SimplifyException(ex);
                        }
                    }
                }

                detail = string.IsNullOrWhiteSpace(cardGrabDetail)
                    ? lastFailure
                    : cardGrabDetail + ";input_mgr_failed:" + lastFailure;
                AppendActionTrace(
                    "API grab not confirmed entityId=" + entityId
                    + ", detail=" + detail
                    + ", inputAttempts=" + string.Join(" || ", inputMgrFailures.Take(12).ToArray()));
                return false;
            }
            catch (Exception ex)
            {
                detail = "exception:" + SimplifyException(ex);
                AppendActionTrace("TryGrabCardViaAPI exception: " + SimplifyException(ex));
                return false;
            }
        }

        /// <summary>
        /// 攻击流程（点击-拖动-点击）：
        /// 1) 点击攻击者（随从/英雄）进入选中态
        /// 2) 鼠标移动到目标
        /// 3) 再次点击目标确认攻击
        /// </summary>
        private static IEnumerator<float> MouseAttack(
            int attackerEntityId,
            int targetEntityId,
            bool sourceIsFriendlyHero,
            bool targetIsEnemyHero)
        {
            InputHook.Simulating = true;
            int sx = 0, sy = 0;
            bool gotAttacker = false;
            var isFriendlyHeroAttacker = sourceIsFriendlyHero || IsFriendlyHeroEntityId(attackerEntityId);
            for (int retry = 0; retry < 5; retry++)
            {
                // 英雄攻击者：优先使用 GetHeroScreenPos（通过 Player→Hero→Card→Transform，
                // 比 GetEntityScreenPos 对英雄实体更可靠）。
                if (isFriendlyHeroAttacker
                    && GameObjectFinder.GetHeroScreenPos(true, out sx, out sy))
                {
                    gotAttacker = true;
                    break;
                }
                if (GameObjectFinder.GetEntityScreenPos(attackerEntityId, out sx, out sy))
                {
                    gotAttacker = true;
                    break;
                }
                yield return 0.5f;
            }
            if (!gotAttacker)
            {
                _coroutine.SetResult("FAIL:ATTACK:attacker_pos:" + attackerEntityId);
                yield break;
            }

            // 第一次点击：选中攻击者
            foreach (var wait in MoveCursorConstructed(sx, sy, 10, 0.010f, false)) yield return wait;
            yield return 0.015f;
            MouseSimulator.LeftDown();
            yield return 0.015f;
            MouseSimulator.LeftUp();
            // 给客户端一点时间进入攻击选中态
            yield return 0.03f;

            // 拾取攻击者后再定位目标，避免“前一击击杀后目标重排”导致坐标过期。
            bool gotTarget = false;
            int tx = 0, ty = 0;
            bool hasPrev = false;
            int prevX = 0, prevY = 0;
            for (int retry = 0; retry < 10 && !gotTarget; retry++)
            {
                int cx = 0, cy = 0;
                var found = targetIsEnemyHero
                    ? GameObjectFinder.GetHeroScreenPos(false, out cx, out cy)
                    : GameObjectFinder.GetEntityScreenPos(targetEntityId, out cx, out cy);
                if (found)
                {
                    // 对随从目标要求至少连续两次位置基本稳定，降低动画位移导致的空挥。
                    if (targetIsEnemyHero)
                    {
                        tx = cx;
                        ty = cy;
                        gotTarget = true;
                        break;
                    }

                    if (!hasPrev)
                    {
                        hasPrev = true;
                        prevX = cx;
                        prevY = cy;
                    }
                    else
                    {
                        var dx = Math.Abs(cx - prevX);
                        var dy = Math.Abs(cy - prevY);
                        if (dx <= 12 && dy <= 12)
                        {
                            tx = cx;
                            ty = cy;
                            gotTarget = true;
                            break;
                        }

                        prevX = cx;
                        prevY = cy;
                    }
                }

                yield return 0.02f;
            }
            if (!gotTarget)
            {
                _coroutine.SetResult("FAIL:ATTACK:target_pos:" + targetEntityId);
                yield break;
            }

            // 拖动到目标后第二次点击确认
            if (!targetIsEnemyHero && GameObjectFinder.GetEntityScreenPos(targetEntityId, out var txLatest, out var tyLatest))
            {
                tx = txLatest;
                ty = tyLatest;
            }
            // 跳过 MaybePreviewAlternateTarget（攻击速度优先）
            foreach (var w in MoveCursorConstructed(tx, ty, 12, 0.012f, false)) yield return w;

            // 点击前最后一刻修正：重新读取目标坐标，如果棋盘重排导致偏移则快���修正
            {
                int corrX = 0, corrY = 0;
                bool corrFound = targetIsEnemyHero
                    ? GameObjectFinder.GetHeroScreenPos(false, out corrX, out corrY)
                    : GameObjectFinder.GetEntityScreenPos(targetEntityId, out corrX, out corrY);
                if (corrFound)
                {
                    var cdx = Math.Abs(corrX - tx);
                    var cdy = Math.Abs(corrY - ty);
                    if (cdx > 12 || cdy > 12)
                    {
                        foreach (var w in SmoothMove(corrX, corrY, 3, 0.005f)) yield return w;
                    }
                }
            }

            MouseSimulator.LeftDown();
            yield return 0.015f;
            MouseSimulator.LeftUp();
            yield return 0.04f;
            _coroutine.SetResult("OK:ATTACK:" + attackerEntityId + ":click_drag_click");
        }

        private struct AttackStateSnapshot
        {
            public bool AttackerExists;
            public int AttackerAttackCount;
            public bool AttackerExhausted;
            public bool HasWeaponDurability;
            public int WeaponDurability;
            public bool TargetExists;
            public int TargetHealth;
            public int TargetArmor;
            public bool TargetDivineShield;
        }

        private static bool TryCaptureAttackState(GameStateData state, int attackerEntityId, int targetEntityId, out AttackStateSnapshot snapshot)
        {
            snapshot = default;
            if (state == null || attackerEntityId <= 0)
                return false;

            var attacker = FindEntityById(state, attackerEntityId);
            if (attacker == null)
                return false;

            snapshot.AttackerExists = true;
            snapshot.AttackerAttackCount = Math.Max(0, attacker.AttackCount);
            snapshot.AttackerExhausted = attacker.Exhausted;
            if (state.HeroFriend != null && state.HeroFriend.EntityId == attackerEntityId && state.WeaponFriend != null)
            {
                snapshot.HasWeaponDurability = true;
                snapshot.WeaponDurability = state.WeaponFriend.Durability;
            }
            else if (state.HeroEnemy != null && state.HeroEnemy.EntityId == attackerEntityId && state.WeaponEnemy != null)
            {
                snapshot.HasWeaponDurability = true;
                snapshot.WeaponDurability = state.WeaponEnemy.Durability;
            }

            var target = FindEntityById(state, targetEntityId);
            if (target != null)
            {
                snapshot.TargetExists = true;
                snapshot.TargetHealth = target.Health;
                snapshot.TargetArmor = target.Armor;
                snapshot.TargetDivineShield = target.DivineShield;
            }

            return true;
        }

        private readonly struct AttackApplyObservation
        {
            public AttackApplyObservation(bool applied, string reason)
            {
                Applied = applied;
                Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            }

            public bool Applied { get; }
            public string Reason { get; }
        }

        private static AttackApplyObservation GetAttackApplyObservation(AttackStateSnapshot before, AttackStateSnapshot after)
        {
            if (after.AttackerAttackCount > before.AttackerAttackCount)
                return new AttackApplyObservation(true, "attack_count_changed");

            if (!before.AttackerExhausted && after.AttackerExhausted)
                return new AttackApplyObservation(true, "attacker_exhausted");

            if (before.HasWeaponDurability && after.HasWeaponDurability
                && after.WeaponDurability != before.WeaponDurability)
                return new AttackApplyObservation(true, "weapon_durability_changed");

            if (before.TargetExists && !after.TargetExists)
                return new AttackApplyObservation(true, "target_removed");

            if (before.TargetExists && after.TargetExists)
            {
                if (after.TargetHealth != before.TargetHealth)
                    return new AttackApplyObservation(true, "target_health_changed");
                if (after.TargetArmor != before.TargetArmor)
                    return new AttackApplyObservation(true, "target_armor_changed");
                if (after.TargetDivineShield != before.TargetDivineShield)
                    return new AttackApplyObservation(true, "target_divine_shield_changed");
            }

            return new AttackApplyObservation(false, "unchanged");
        }

        private static string AppendAttackTimingToResult(
            string baseResult,
            int attempt,
            long mouseMs,
            long confirmMs,
            int confirmPolls,
            string applyReason)
        {
            var normalizedResult = string.IsNullOrWhiteSpace(baseResult) ? "NO_RESPONSE" : baseResult;
            var normalizedReason = string.IsNullOrWhiteSpace(applyReason) ? "unknown" : applyReason;
            return normalizedResult
                + ":attempt=" + System.Math.Max(1, attempt)
                + ":mouseMs=" + System.Math.Max(0L, mouseMs)
                + ":confirmMs=" + System.Math.Max(0L, confirmMs)
                + ":confirmPolls=" + System.Math.Max(0, confirmPolls)
                + ":apply=" + normalizedReason;
        }

        private static bool CanEntityAttackNow(GameStateData state, int attackerEntityId, int targetEntityId, out string reason)
        {
            reason = "unknown";
            if (state == null)
            {
                reason = "no_state";
                return false;
            }

            var attacker = FindEntityById(state, attackerEntityId);
            if (attacker == null)
            {
                reason = "attacker_not_found";
                return false;
            }

            if (attacker.Atk <= 0)
            {
                reason = "atk_le_0";
                return false;
            }

            if (attacker.Frozen || attacker.Freeze)
            {
                reason = "frozen";
                return false;
            }

            var maxAttack = GetMaxAttackCountThisTurn(state, attackerEntityId, attacker);
            if (attacker.AttackCount >= maxAttack)
            {
                reason = "attack_count_limit";
                return false;
            }

            if (attacker.Exhausted && attacker.AttackCount <= 0)
            {
                if (attacker.Charge)
                {
                    reason = "ok_charge";
                    return true;
                }

                if (attacker.Rush)
                {
                    if (IsEnemyMinionEntity(state, targetEntityId))
                    {
                        reason = "ok_rush_vs_minion";
                        return true;
                    }

                    if (state.HeroEnemy != null && state.HeroEnemy.EntityId == targetEntityId)
                    {
                        reason = "rush_cannot_attack_hero";
                        return false;
                    }
                }

                reason = "exhausted";
                return false;
            }

            reason = "ok";
            return true;
        }

        private static int GetMaxAttackCountThisTurn(GameStateData state, int attackerEntityId, EntityData attacker)
        {
            if (attacker == null)
                return 1;

            if (attacker.Windfury)
                return 2;

            if (state?.HeroFriend != null
                && state.HeroFriend.EntityId == attackerEntityId
                && state.WeaponFriend != null
                && state.WeaponFriend.Windfury)
                return 2;

            if (state?.HeroEnemy != null
                && state.HeroEnemy.EntityId == attackerEntityId
                && state.WeaponEnemy != null
                && state.WeaponEnemy.Windfury)
                return 2;

            return 1;
        }

        private static EntityData FindEntityById(GameStateData state, int entityId)
        {
            if (state == null || entityId <= 0)
                return null;

            if (state.HeroFriend != null && state.HeroFriend.EntityId == entityId) return state.HeroFriend;
            if (state.HeroEnemy != null && state.HeroEnemy.EntityId == entityId) return state.HeroEnemy;
            if (state.AbilityFriend != null && state.AbilityFriend.EntityId == entityId) return state.AbilityFriend;
            if (state.AbilityEnemy != null && state.AbilityEnemy.EntityId == entityId) return state.AbilityEnemy;
            if (state.WeaponFriend != null && state.WeaponFriend.EntityId == entityId) return state.WeaponFriend;
            if (state.WeaponEnemy != null && state.WeaponEnemy.EntityId == entityId) return state.WeaponEnemy;

            var inFriendMinions = state.MinionFriend?.FirstOrDefault(e => e != null && e.EntityId == entityId);
            if (inFriendMinions != null) return inFriendMinions;

            var inEnemyMinions = state.MinionEnemy?.FirstOrDefault(e => e != null && e.EntityId == entityId);
            if (inEnemyMinions != null) return inEnemyMinions;

            return state.Hand?.FirstOrDefault(e => e != null && e.EntityId == entityId);
        }

        private static bool IsEnemyMinionEntity(GameStateData state, int entityId)
        {
            return state?.MinionEnemy != null
                && entityId > 0
                && state.MinionEnemy.Any(entity => entity != null && entity.EntityId == entityId);
        }

        /// <summary>
        /// 鼠标点击英雄技能
        /// </summary>
        private static bool IsFriendlyHeroEntityId(int entityId)
        {
            if (entityId <= 0) return false;
            try
            {
                var gs = GetGameState();
                if (gs == null) return false;

                var friendly = Invoke(gs, "GetFriendlySidePlayer") ?? Invoke(gs, "GetFriendlyPlayer");
                if (friendly == null) return false;

                var hero = Invoke(friendly, "GetHero");
                if (hero == null) return false;

                // 兼容不同客户端版本：优先从 Hero->Entity 解析实体，再回退到 Hero 本体字段。
                var heroEntity = Invoke(hero, "GetEntity")
                    ?? GetFieldOrProp(hero, "Entity")
                    ?? GetFieldOrProp(hero, "m_entity")
                    ?? hero;

                var id = ResolveEntityId(heroEntity);
                if (id <= 0) id = ResolveEntityId(hero);
                if (id <= 0) id = GetIntFieldOrProp(hero, "EntityID");
                if (id <= 0) id = GetIntFieldOrProp(hero, "EntityId");
                if (id <= 0) id = GetIntFieldOrProp(hero, "ID");
                if (id <= 0) id = GetIntFieldOrProp(hero, "Id");
                return id == entityId;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsEnemyHeroEntityId(int entityId)
        {
            if (entityId <= 0) return false;
            try
            {
                var gs = GetGameState();
                if (gs == null) return false;

                var enemy = Invoke(gs, "GetOpposingSidePlayer") ?? Invoke(gs, "GetOpposingPlayer");
                if (enemy == null) return false;

                var hero = Invoke(enemy, "GetHero");
                if (hero == null) return false;

                var heroEntity = Invoke(hero, "GetEntity")
                    ?? GetFieldOrProp(hero, "Entity")
                    ?? GetFieldOrProp(hero, "m_entity")
                    ?? hero;

                var id = ResolveEntityId(heroEntity);
                if (id <= 0) id = ResolveEntityId(hero);
                if (id <= 0) id = GetIntFieldOrProp(hero, "EntityID");
                if (id <= 0) id = GetIntFieldOrProp(hero, "EntityId");
                if (id <= 0) id = GetIntFieldOrProp(hero, "ID");
                if (id <= 0) id = GetIntFieldOrProp(hero, "Id");
                return id == entityId;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerator<float> MouseHeroPower(int sourceHeroPowerEntityId, int targetEntityId)
        {
            InputHook.Simulating = true;
            if (!GameObjectFinder.GetHeroPowerScreenPos(sourceHeroPowerEntityId, out var hx, out var hy))
            {
                _coroutine.SetResult(sourceHeroPowerEntityId > 0
                    ? "FAIL:HP:pos_not_found:" + sourceHeroPowerEntityId
                    : "FAIL:HP:pos_not_found");
                yield break;
            }

            // 瞬移并点击英雄技能
            foreach (var wait in MoveCursorConstructed(hx, hy, 10, 0.010f, false)) yield return wait;
            yield return 0.05f;
            MouseSimulator.LeftDown();
            yield return 0.05f;
            MouseSimulator.LeftUp();
            yield return 0.3f;

            // 如果有目标
            if (targetEntityId > 0)
            {
                var targetHeroSide = -1;
                var currentState = SafeReadGameState();
                if (currentState != null)
                {
                    if (currentState.HeroFriend != null && currentState.HeroFriend.EntityId == targetEntityId)
                        targetHeroSide = 0;
                    else if (currentState.HeroEnemy != null && currentState.HeroEnemy.EntityId == targetEntityId)
                        targetHeroSide = 1;
                }

                bool gotTarget = TryResolvePlayTargetScreenPos(targetEntityId, targetHeroSide, out var tx, out var ty);
                if (!gotTarget)
                {
                    _coroutine.SetResult("FAIL:HP:target_pos:" + targetEntityId);
                    yield break;
                }
                foreach (var wait in MaybePreviewAlternateTarget(targetEntityId, targetHeroSide, false)) yield return wait;
                foreach (var wait in MoveCursorConstructed(tx, ty, 10, 0.010f, false)) yield return wait;
                yield return 0.05f;
                MouseSimulator.LeftDown();
                yield return 0.05f;
                MouseSimulator.LeftUp();
                yield return 0.3f;
            }

            _coroutine.SetResult("OK:HP");
        }

        /// <summary>
        /// 获取我方场上随从数量
        /// </summary>
        private static int GetFriendlyMinionCount()
        {
            try
            {
                var gs = GetGameState();
                if (gs == null) return 0;
                var friendly = Invoke(gs, "GetFriendlySidePlayer") ?? Invoke(gs, "GetFriendlyPlayer");
                if (friendly == null) return 0;
                var zone = Invoke(friendly, "GetBattlefieldZone") ?? Invoke(friendly, "GetPlayZone");
                if (zone == null) return 0;
                var cards = Invoke(zone, "GetCards") as IEnumerable
                    ?? GetFieldOrProp(zone, "m_cards") as IEnumerable;
                if (cards == null) return 0;
                int count = 0;
                foreach (var _ in cards) count++;
                return count;
            }
            catch { return 0; }
        }

        /// <summary>
        /// 鼠标点击留牌（点击要替换的卡牌，然后点击确认按钮）
        /// </summary>
        private static IEnumerator<float> MouseMulligan(int[] replaceIndices, int totalCards)
        {
            InputHook.Simulating = true;
            yield return 0.5f;

            var mulliganMgr = TryGetMulliganManager();

            // 点击要替换的卡牌
            foreach (var idx in replaceIndices)
            {
                if (!GameObjectFinder.GetMulliganCardScreenPos(idx, totalCards, out var cx, out var cy))
                    continue;
                foreach (var w in SmoothMove(cx, cy)) yield return w;
                MouseSimulator.LeftDown();
                yield return 0.05f;
                MouseSimulator.LeftUp();
                yield return 0.6f;

                // 验证点击是否生效
                if (mulliganMgr != null && TryReadMulliganMarkedState(mulliganMgr, idx, out var marked, out _) && !marked)
                {
                    // 点击未生效，重试一次
                    yield return 0.2f;
                    MouseSimulator.LeftDown();
                    yield return 0.05f;
                    MouseSimulator.LeftUp();
                    yield return 0.6f;
                }
            }

            // 通过 API 确认留牌（虚拟鼠标点击确认按钮对 PegUI 无效）
            if (mulliganMgr == null)
                mulliganMgr = TryGetMulliganManager();
            if (mulliganMgr != null)
            {
                var confirmed = TryInvokeMethod(mulliganMgr, "OnMulliganButtonReleased", new object[] { null }, out _, out _)
                    || TryInvokeMethod(mulliganMgr, "AutomaticContinueMulligan", new object[] { false }, out _, out _);
                if (confirmed)
                {
                    _coroutine.SetResult("OK:mouse_mulligan:toggled=" + replaceIndices.Length);
                    yield break;
                }
            }

            // API 失败时回退到鼠标点击确认
            if (!GameObjectFinder.GetMulliganConfirmScreenPos(out var bx, out var by))
            {
                _coroutine.SetResult("FAIL:MULLIGAN:confirm_pos");
                yield break;
            }
            foreach (var w in SmoothMove(bx, by)) yield return w;
            MouseSimulator.LeftDown();
            yield return 0.05f;
            MouseSimulator.LeftUp();
            yield return 0.5f;

            _coroutine.SetResult("OK:mouse_mulligan:toggled=" + replaceIndices.Length);
        }

        #endregion

        /// <summary>
        /// 鼠标模拟留牌（从管道线程调用，通过协程执行）
        /// </summary>
        public static string MouseApplyMulligan(string replaceEntityIdsCsv, List<int> handEntityIds)
        {
            if (_coroutine == null) return "ERROR:no_coroutine";
            if (handEntityIds == null || handEntityIds.Count == 0) return "ERROR:no_hand";

            var replaceEntityIds = ParseEntityIds(replaceEntityIdsCsv);
            var replaceSet = new HashSet<int>(replaceEntityIds.Where(id => id > 0));

            // 映射要替换的entityId到卡牌索引
            var indices = new List<int>();
            for (int i = 0; i < handEntityIds.Count; i++)
            {
                if (replaceSet.Contains(handEntityIds[i]))
                    indices.Add(i);
            }

            return _coroutine.RunAndWait(MouseMulligan(indices.ToArray(), handEntityIds.Count));
        }

        /// <summary>
        /// 应用留牌替换列表并确认选择。
        /// </summary>
        public static string ApplyMulligan(string replaceEntityIdsCsv)
        {
            if (!EnsureTypes()) return "ERROR:not_initialized";
            if (_coroutine == null) return "ERROR:no_coroutine";

            var replaceEntityIds = ParseEntityIds(replaceEntityIdsCsv);
            MulliganReadySnapshot snapshot;
            string readyDetail;
            if (!TryGetMulliganReadySnapshot(out snapshot, out readyDetail))
                return "FAIL:wait:mulligan_ready:" + (readyDetail ?? "unknown");

            // 始终走鼠标模拟点击卡牌，避免 API 直接操作在动画尾帧导致界面卡住
            return _coroutine.RunAndWait(
                ApplyMulliganByMouseWithVerification(
                    replaceEntityIds,
                    snapshot.CardIndexByEntityId.Keys.OrderBy(id => id).ToArray()),
                20000);
        }

        /// <summary>
        /// 简化的留牌逻辑：直接点击卡牌，不验证状态，失败由外层重试。
        /// </summary>
        private static IEnumerator<float> ApplyMulliganByMouseWithVerification(int[] replaceEntityIds, int[] expectedEntityIds)
        {
            InputHook.Simulating = true;
            yield return 0.06f;

            MulliganReadySnapshot snapshot;
            string readyDetail;
            if (!TryGetMulliganReadySnapshot(out snapshot, out readyDetail))
            {
                _coroutine.SetResult("FAIL:wait:mouse_fallback:" + (readyDetail ?? "unknown"));
                yield break;
            }

            if (!DoEntitySetsMatch(expectedEntityIds, snapshot.CardIndexByEntityId.Keys))
            {
                _coroutine.SetResult("FAIL:wait:mouse_fallback:entity_set_changed");
                yield break;
            }

            var replaceSet = new HashSet<int>(replaceEntityIds.Where(id => id > 0));
            var clickedCount = 0;

            foreach (var entityId in replaceSet)
            {
                int cardIndex;
                if (!snapshot.CardIndexByEntityId.TryGetValue(entityId, out cardIndex))
                {
                    _coroutine.SetResult("FAIL:wait:mouse_fallback:entity_not_found:" + entityId);
                    yield break;
                }

                var success = false;
                for (int attempt = 0; attempt < 2 && !success; attempt++)
                {
                    if (!TryGetMulliganCardClickPos(entityId, cardIndex, snapshot.StartingCards.Count, out var cx, out var cy, out _))
                        break;

                    foreach (var w in SmoothMove(cx, cy, 8, 0.012f)) yield return w;
                    MouseSimulator.LeftDown();
                    yield return 0.05f;
                    MouseSimulator.LeftUp();
                    yield return 0.3f;

                    if (TryReadMulliganMarkedState(snapshot.MulliganManager, cardIndex, out var marked, out _) && marked)
                    {
                        success = true;
                        clickedCount++;
                    }
                }
            }

            yield return 0.3f;

            if (TryInvokeMethod(snapshot.MulliganManager, "OnMulliganButtonReleased", new object[] { null }, out _, out _))
            {
                _coroutine.SetResult("OK:mouse_fallback:api_confirm:clicked=" + clickedCount);
                yield break;
            }

            if (TryInvokeMethod(snapshot.MulliganManager, "AutomaticContinueMulligan", new object[] { false }, out _, out _))
            {
                _coroutine.SetResult("OK:mouse_fallback:api_confirm_alt:clicked=" + clickedCount);
                yield break;
            }

            if (GameObjectFinder.GetMulliganConfirmScreenPos(out var bx, out var by))
            {
                foreach (var w in SmoothMove(bx, by, 8, 0.012f)) yield return w;
                MouseSimulator.LeftDown();
                yield return 0.05f;
                MouseSimulator.LeftUp();
                _coroutine.SetResult("OK:mouse_fallback:mouse_confirm:clicked=" + clickedCount);
                yield break;
            }

            _coroutine.SetResult("FAIL:mouse_fallback:confirm_failed:clicked=" + clickedCount);
        }

        private static bool TryGetMulliganCardClickPos(int entityId, int cardIndex, int totalCards, out int x, out int y, out string detail)
        {
            x = y = 0;
            detail = null;

            if (entityId > 0 && GameObjectFinder.GetEntityScreenPos(entityId, out x, out y))
            {
                detail = "entity_pos";
                return true;
            }

            if (cardIndex >= 0 && GameObjectFinder.GetMulliganCardScreenPos(cardIndex, totalCards, out x, out y))
            {
                detail = "index_pos";
                return true;
            }

            // 诊断日志：定位到底哪一步失败
            var diag = new System.Text.StringBuilder();
            diag.Append("DIAG:eid=").Append(entityId).Append(",idx=").Append(cardIndex).Append(",total=").Append(totalCards);
            try
            {
                diag.Append("|entityWorldPos=").Append(GameObjectFinder.GetEntityWorldPos(entityId) != null ? "ok" : "null");
                diag.Append("|scrW=").Append(MouseSimulator.GetScreenWidth());
                diag.Append("|scrH=").Append(MouseSimulator.GetScreenHeight());
                diag.Append("|mulliganDiag=").Append(GameObjectFinder.DiagMulliganCardPos(cardIndex, totalCards));
            }
            catch (Exception ex)
            {
                diag.Append("|diagErr=").Append(ex.Message);
            }

            detail = "entity_and_index_pos_not_found:" + diag;
            return false;
        }

        /// <summary>
        /// 仅返回可替换的留牌选择（来自 MulliganManager.m_startingCards）。
        /// 格式：cardId1,entityId1;cardId2,entityId2;...
        /// 留牌 UI 存在但卡牌未就绪时返回空字符串。
        /// 不在留牌上下文时返回 null。
        /// </summary>
        public static string GetMulliganChoiceCards()
        {
            if (!EnsureTypes()) return null;

            var gs = GetGameState();
            if (gs == null) return null;

            var mulliganMgr = TryGetMulliganManager();
            if (mulliganMgr == null) return null;

            if (TryInvokeBoolMethod(mulliganMgr, "IsMulliganActive", out var isMulliganActive) && !isMulliganActive)
                return null;

            MulliganReadySnapshot snapshot;
            string detail;
            if (!TryGetMulliganReadySnapshot(out snapshot, out detail))
                return string.Empty;

            return string.Join(";", snapshot.ChoiceParts);
        }

        public static bool GetMulliganHasCoinFlag()
        {
            if (!EnsureTypes())
                return false;

            var gs = GetGameState();
            if (gs == null)
                return false;

            if (!TryGetMulliganReadySnapshot(out var snapshot, out _))
                return false;

            return snapshot.StartingCards.Count >= 4;
        }

        private static List<int> GetMulliganStartingHandEntityIds()
        {
            var result = new List<int>();
            try
            {
                var mulliganMgr = TryGetMulliganManager();
                if (mulliganMgr == null)
                    return result;

                var cards = GetFieldOrProp(mulliganMgr, "m_startingCards") as IEnumerable;
                if (cards == null)
                    return result;

                foreach (var card in cards)
                {
                    if (card == null) continue;
                    var entity = Invoke(card, "GetEntity")
                        ?? GetFieldOrProp(card, "Entity")
                        ?? GetFieldOrProp(card, "m_entity");
                    var entityId = ResolveEntityId(entity);
                    if (entityId > 0)
                        result.Add(entityId);
                }
            }
            catch
            {
            }

            return result;
        }

        private static List<int> GetFriendlyHandEntityIds()
        {
            var result = new List<int>();
            try
            {
                var gameState = GetGameState();
                if (gameState == null)
                    return result;

                if (!TryGetFriendlyHandCards(gameState, out var cards) || cards == null)
                    return result;

                foreach (var card in cards)
                {
                    if (card == null) continue;
                    var entity = Invoke(card, "GetEntity")
                        ?? GetFieldOrProp(card, "Entity")
                        ?? GetFieldOrProp(card, "m_entity")
                        ?? card;
                    var entityId = ResolveEntityId(entity);
                    if (entityId > 0)
                        result.Add(entityId);
                }
            }
            catch
            {
            }

            return result;
        }

        private sealed class MulliganReadySnapshot
        {
            public object GameState { get; set; }
            public object MulliganManager { get; set; }
            public object FriendlyChoices { get; set; }
            public List<object> StartingCards { get; } = new List<object>();
            public List<string> ChoiceParts { get; } = new List<string>();
            public Dictionary<int, object> CardByEntityId { get; } = new Dictionary<int, object>();
            public Dictionary<int, int> CardIndexByEntityId { get; } = new Dictionary<int, int>();
            public HashSet<int> ChoiceEntityIds { get; } = new HashSet<int>();
        }

        private static bool TryGetMulliganReadySnapshot(out MulliganReadySnapshot snapshot, out string detail, bool allowLooseChoicePacket = false)
        {
            snapshot = null;
            detail = null;

            var gameState = GetGameState();
            if (gameState == null)
            {
                detail = "game_state_not_available";
                return false;
            }

            var mulliganMgr = TryGetMulliganManager();
            if (mulliganMgr == null)
            {
                detail = "mulligan_manager_not_found";
                return false;
            }

            if (TryInvokeBoolMethod(mulliganMgr, "IsMulliganActive", out var isMulliganActive) && !isMulliganActive)
            {
                detail = "mulligan_not_active";
                return false;
            }

            var introObj = GetFieldOrProp(mulliganMgr, "introComplete");
            if (introObj is bool introComplete && !introComplete)
            {
                detail = "intro_not_complete";
                return false;
            }

            var waitingObj = GetFieldOrProp(mulliganMgr, "m_waitingForUserInput");
            if (!(waitingObj is bool waitingForUserInput) || !waitingForUserInput)
            {
                detail = "waiting_for_user_input";
                return false;
            }

            var mulliganButton = Invoke(mulliganMgr, "GetMulliganButton") ?? GetFieldOrProp(mulliganMgr, "mulliganButton");
            if (mulliganButton == null)
            {
                detail = "mulligan_button_not_ready";
                return false;
            }

            if (TryInvokeBoolMethod(gameState, "IsResponsePacketBlocked", out var responseBlocked) && responseBlocked)
            {
                detail = "response_packet_blocked";
                return false;
            }

            var responseMode = Invoke(gameState, "GetResponseMode");
            if (!allowLooseChoicePacket && !IsChoiceResponseMode(responseMode))
            {
                detail = "response_mode_not_choice:" + (responseMode?.ToString() ?? "null");
                return false;
            }

            var friendlyChoices = Invoke(gameState, "GetFriendlyEntityChoices");
            if (!allowLooseChoicePacket && friendlyChoices == null)
            {
                detail = "friendly_choices_not_ready";
                return false;
            }

            var inputMgr = GetSingleton(_inputMgrType);
            if (inputMgr != null && (!TryInvokeBoolMethod(inputMgr, "PermitDecisionMakingInput", out var permitInput) || !permitInput))
            {
                detail = "input_not_ready";
                return false;
            }

            var startingCardsRaw = GetFieldOrProp(mulliganMgr, "m_startingCards") as IEnumerable;
            if (startingCardsRaw == null)
            {
                detail = "starting_cards_not_ready";
                return false;
            }

            var startingCards = startingCardsRaw.Cast<object>().Where(card => card != null).ToList();
            if (startingCards.Count == 0)
            {
                detail = "starting_cards_empty";
                return false;
            }

            if ((!TryGetCollectionCount(GetFieldOrProp(mulliganMgr, "m_handCardsMarkedForReplace"), out var markedCount)
                || markedCount < startingCards.Count) && !allowLooseChoicePacket)
            {
                detail = "marked_state_not_ready";
                return false;
            }

            var builtSnapshot = new MulliganReadySnapshot
            {
                GameState = gameState,
                MulliganManager = mulliganMgr,
                FriendlyChoices = friendlyChoices
            };

            AppendChoiceEntityIds(friendlyChoices, builtSnapshot.ChoiceEntityIds);

            for (int cardIndex = 0; cardIndex < startingCards.Count; cardIndex++)
            {
                var card = startingCards[cardIndex];
                int entityId;
                string cardId;
                if (!TryGetMulliganCardDescriptor(card, out entityId, out cardId))
                {
                    detail = entityId > 0 ? "starting_cards_cardid_not_ready" : "starting_cards_entity_not_ready";
                    return false;
                }

                if (builtSnapshot.CardByEntityId.ContainsKey(entityId))
                {
                    detail = "starting_cards_entity_duplicate:" + entityId;
                    return false;
                }

                builtSnapshot.StartingCards.Add(card);
                builtSnapshot.CardByEntityId.Add(entityId, card);
                builtSnapshot.CardIndexByEntityId.Add(entityId, cardIndex);
                builtSnapshot.ChoiceParts.Add(cardId + "," + entityId);
            }

            if (!allowLooseChoicePacket
                && builtSnapshot.ChoiceEntityIds.Count > 0
                && builtSnapshot.CardIndexByEntityId.Keys.Any(entityId => !builtSnapshot.ChoiceEntityIds.Contains(entityId)))
            {
                detail = "choice_entities_not_ready";
                return false;
            }

            snapshot = builtSnapshot;
            return true;
        }

        private static bool TryGetMulliganCardDescriptor(object card, out int entityId, out string cardId)
        {
            entityId = 0;
            cardId = null;
            if (card == null)
                return false;

            var entity = Invoke(card, "GetEntity")
                ?? GetFieldOrProp(card, "Entity")
                ?? GetFieldOrProp(card, "m_entity");
            if (entity == null)
                return false;

            entityId = ResolveEntityId(entity);
            if (entityId <= 0)
                return false;

            var cardIdObj = Invoke(entity, "GetCardId")
                ?? GetFieldOrProp(entity, "CardId")
                ?? Invoke(card, "GetCardId")
                ?? GetFieldOrProp(card, "CardId");
            cardId = cardIdObj?.ToString();
            return !string.IsNullOrWhiteSpace(cardId);
        }

        private static void AppendChoiceEntityIds(object friendlyChoices, ISet<int> entityIds)
        {
            if (friendlyChoices == null || entityIds == null)
                return;

            var entities = GetFieldOrProp(friendlyChoices, "Entities") as IEnumerable
                ?? Invoke(friendlyChoices, "GetEntities") as IEnumerable;
            if (entities == null)
                return;

            foreach (var entity in entities)
            {
                var entityId = 0;
                try
                {
                    entityId = Convert.ToInt32(entity);
                }
                catch
                {
                }

                if (entityId <= 0)
                    entityId = ResolveEntityId(entity);

                if (entityId > 0)
                    entityIds.Add(entityId);
            }
        }

        private static bool DoEntitySetsMatch(IEnumerable<int> expectedEntityIds, IEnumerable<int> actualEntityIds)
        {
            var expected = new HashSet<int>((expectedEntityIds ?? Array.Empty<int>()).Where(id => id > 0));
            var actual = new HashSet<int>((actualEntityIds ?? Array.Empty<int>()).Where(id => id > 0));
            return expected.SetEquals(actual);
        }

        private static bool TryApplyMulliganViaManager(int[] replaceEntityIds, MulliganReadySnapshot snapshot, out string detail)
        {
            detail = null;
            replaceEntityIds = replaceEntityIds ?? Array.Empty<int>();
            if (snapshot == null)
            {
                detail = "mulligan_snapshot_not_ready";
                return false;
            }

            var requestIdSet = new HashSet<int>(replaceEntityIds
                .Where(id => id > 0)
                .Distinct());

            foreach (var entityId in requestIdSet)
            {
                if (!snapshot.CardByEntityId.ContainsKey(entityId))
                {
                    detail = "entity_not_found:" + entityId;
                    return false;
                }
            }

            var toggledCount = 0;
            foreach (var pair in snapshot.CardIndexByEntityId.OrderBy(p => p.Value))
            {
                var entityId = pair.Key;
                var cardIndex = pair.Value;
                var shouldReplace = requestIdSet.Contains(entityId);

                if (!TryReadMulliganMarkedState(snapshot.MulliganManager, cardIndex, out var currentMarked, out var markedDetail))
                {
                    detail = "marked_state_read_failed:" + markedDetail;
                    return false;
                }

                if (currentMarked == shouldReplace)
                    continue;

                object card;
                if (!snapshot.CardByEntityId.TryGetValue(entityId, out card) || card == null)
                {
                    detail = "card_not_found:" + entityId;
                    return false;
                }

                if (!TryToggleMulliganCardViaManager(snapshot.MulliganManager, card, cardIndex, out var toggleDetail))
                {
                    detail = "toggle_failed:" + entityId + ":" + toggleDetail;
                    return false;
                }

                if (!TryReadMulliganMarkedState(snapshot.MulliganManager, cardIndex, out var markedAfter, out markedDetail))
                {
                    detail = "marked_state_verify_failed:" + markedDetail;
                    return false;
                }

                if (markedAfter != shouldReplace)
                {
                    detail = "toggle_state_mismatch:" + entityId
                        + ":expected=" + (shouldReplace ? "1" : "0")
                        + ":actual=" + (markedAfter ? "1" : "0");
                    return false;
                }

                toggledCount++;
            }

            string continueMethodUsed = null;
            if (TryInvokeMethod(snapshot.MulliganManager, "OnMulliganButtonReleased", new object[] { null }, out _, out var continueError))
            {
                continueMethodUsed = "OnMulliganButtonReleased";
            }
            else if (TryInvokeMethod(snapshot.MulliganManager, "AutomaticContinueMulligan", new object[] { false }, out _, out continueError))
            {
                continueMethodUsed = "AutomaticContinueMulligan";
            }
            else if (snapshot.GameState != null && TryInvokeMethod(snapshot.GameState, "SendChoices", Array.Empty<object>(), out _, out continueError))
            {
                continueMethodUsed = "GameState.SendChoices";
            }

            if (continueMethodUsed == null)
            {
                detail = "continue_failed:" + (continueError ?? "unknown");
                return false;
            }

            detail = "toggle=" + toggledCount + ";request=" + requestIdSet.Count + ";continue=" + continueMethodUsed;
            return true;
        }

        /// <summary>
        /// End current turn by pressing the end-turn button.
        /// </summary>
        private static void EndTurn()
        {
            try
            {
                var inst = GetSingleton(_inputMgrType);
                if (inst != null)
                {
                    Invoke(inst, "DoEndTurnButton");
                    return;
                }
                // fallback: 直接通过 GameState 发送结束回合选项
                var gs = GetGameState();
                if (gs != null)
                {
                    var optionsPacket = Invoke(gs, "GetOptionsPacket");
                    if (optionsPacket != null)
                    {
                        var list = GetFieldOrProp(optionsPacket, "List") as IList;
                        if (list != null)
                        {
                            for (int i = 0; i < list.Count; i++)
                            {
                                var option = list[i];
                                var optionType = GetFieldOrProp(option, "Type");
                                if (optionType != null)
                                {
                                    var typeName = optionType.ToString();
                                    if (typeName.Contains("END_TURN") || typeName.Contains("PASS"))
                                    {
                                        TryInvokeMethod(gs, "SetSelectedOption", new object[] { i }, out _, out _);
                                        Invoke(gs, "SendOption");
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 投降
        /// </summary>
        private static void Concede()
        {
            try
            {
                var gs = GetGameState();
                if (gs != null)
                {
                    Invoke(gs, "Concede");
                    return;
                }
                // fallback
                var network = GetSingleton(_networkType);
                if (network != null)
                    Invoke(network, "Concede");
            }
            catch { }
        }

        private static int[] ParseEntityIds(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return Array.Empty<int>();

            return raw
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s =>
                {
                    if (int.TryParse(s, out var value)) return value;
                    return 0;
                })
                .Where(value => value > 0)
                .ToArray();
        }

        private static bool IsMulliganManagerWaitingDetail(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
                return true;

            var normalized = detail.ToLowerInvariant();
            return normalized.Contains("waiting_for_user_input")
                || normalized.Contains("intro_not_complete")
                || normalized.Contains("mulligan_button_not_ready")
                || normalized.Contains("game_state_not_available")
                || normalized.Contains("response_packet_blocked")
                || normalized.Contains("response_mode_not_choice")
                || normalized.Contains("friendly_choices_not_ready")
                || normalized.Contains("choice_entities_not_ready")
                || normalized.Contains("choice_id_not_ready")
                || normalized.Contains("input_not_ready")
                || normalized.Contains("starting_cards_not_ready")
                || normalized.Contains("starting_cards_empty")
                || normalized.Contains("starting_cards_entity_not_ready")
                || normalized.Contains("starting_cards_cardid_not_ready")
                || normalized.Contains("marked_state_not_ready")
                || normalized.Contains("mulligan_not_active")
                || normalized.Contains("mulligan_manager_not_found")
                || normalized.Contains("entity_not_found")
                || normalized.Contains("entity_set_changed");
        }

        private static bool TrySendMulliganChoices(int[] replaceEntityIds, MulliganReadySnapshot snapshot, out string detail)
        {
            detail = null;
            replaceEntityIds = replaceEntityIds ?? Array.Empty<int>();
            if (snapshot == null)
            {
                detail = "mulligan_snapshot_not_ready";
                return false;
            }

            if (TrySendMulliganChoicesByChoicePacket(replaceEntityIds, snapshot, out detail))
                return true;

            var methodNames = new[]
            {
                "SendMulligan",
                "SubmitMulligan",
                "ConfirmMulligan",
                "DoMulligan"
            };

            var gameState = snapshot.GameState;
            var entityChoices = new object[0];
            if (gameState != null && replaceEntityIds.Length > 0)
            {
                var list = new List<object>();
                foreach (var entityId in replaceEntityIds)
                {
                    var entity = GetEntity(gameState, entityId);
                    if (entity != null) list.Add(entity);
                }
                entityChoices = list.ToArray();
            }

            if (gameState != null && TryInvokeMulliganSender(gameState, methodNames, replaceEntityIds, entityChoices, out detail))
                return true;

            var network = GetSingleton(_networkType);
            if (network != null && TryInvokeMulliganSender(network, methodNames, replaceEntityIds, entityChoices, out detail))
                return true;

            if (snapshot.MulliganManager != null && TryInvokeMulliganSender(snapshot.MulliganManager, methodNames, replaceEntityIds, entityChoices, out detail))
                return true;

            detail = detail ?? "choice_sender_not_found";
            return false;
        }

        private static bool TrySendMulliganChoicesByChoicePacket(int[] replaceEntityIds, MulliganReadySnapshot snapshot, out string detail)
        {
            detail = null;
            if (snapshot == null)
            {
                detail = "mulligan_snapshot_not_ready";
                return false;
            }

            var network = GetSingleton(_networkType);
            if (network == null) return false;

            var gameState = snapshot.GameState;
            var friendlyChoices = snapshot.FriendlyChoices;
            if (friendlyChoices == null)
                friendlyChoices = Invoke(network, "GetEntityChoices");
            if (friendlyChoices == null)
            {
                detail = "friendly_choices_not_ready";
                return false;
            }

            var choiceId = GetIntFieldOrProp(friendlyChoices, "ID");
            if (choiceId <= 0)
                choiceId = GetIntFieldOrProp(friendlyChoices, "Id");
            if (choiceId <= 0)
            {
                detail = "choice_id_not_ready";
                return false;
            }

            var allowedEntityIds = new HashSet<int>();
            AppendChoiceEntityIds(friendlyChoices, allowedEntityIds);

            var missingEntityId = replaceEntityIds
                .Where(id => id > 0)
                .FirstOrDefault(id => allowedEntityIds.Count > 0 && !allowedEntityIds.Contains(id));
            if (missingEntityId > 0)
            {
                detail = "entity_not_found:" + missingEntityId;
                return false;
            }

            var picks = replaceEntityIds
                .Where(id => id > 0)
                .Where(id => allowedEntityIds.Count == 0 || allowedEntityIds.Contains(id))
                .Distinct()
                .ToList();

            var methods = _networkType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, "SendChoices", StringComparison.OrdinalIgnoreCase))
                .Where(m => m.GetParameters().Length == 2)
                .ToArray();

            foreach (var method in methods)
            {
                var pars = method.GetParameters();
                if (pars[0].ParameterType != typeof(int))
                    continue;
                if (!TryBuildIntCollectionArg(pars[1].ParameterType, picks.ToArray(), out var pickArg))
                    continue;

                try
                {
                    method.Invoke(network, new[] { (object)choiceId, pickArg });
                    detail = "Network." + method.Name
                        + ":id=" + choiceId
                        + ":picks=" + (picks.Count == 0 ? "none" : string.Join(",", picks));
                    return true;
                }
                catch
                {
                }
            }

            detail = "choice_packet_sender_not_found";
            return false;
        }

        private static int ResolveEntityId(object entity)
        {
            if (entity == null) return 0;

            try
            {
                var idObj = Invoke(entity, "GetEntityId");
                if (idObj != null) return Convert.ToInt32(idObj);
            }
            catch
            {
            }

            var id = GetIntFieldOrProp(entity, "EntityId");
            if (id <= 0) id = GetIntFieldOrProp(entity, "EntityID");
            if (id <= 0) id = GetIntFieldOrProp(entity, "Id");
            if (id <= 0) id = GetIntFieldOrProp(entity, "ID");
            if (id <= 0) id = GetIntFieldOrProp(entity, "m_EntityId");
            if (id <= 0) id = GetIntFieldOrProp(entity, "m_EntityID");
            if (id <= 0) id = GetIntFieldOrProp(entity, "m_entityId");
            if (id <= 0) id = GetIntFieldOrProp(entity, "m_id");
            return id;
        }

        private static bool IsChoiceResponseMode(object responseMode)
        {
            if (responseMode == null) return false;

            var modeName = responseMode.ToString();
            if (string.Equals(modeName, "CHOICE", StringComparison.OrdinalIgnoreCase))
                return true;
            if (IsTargetSelectionResponseMode(responseMode))
                return true;

            try
            {
                return Convert.ToInt32(responseMode) == 13;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsChoiceModeActive(object gameState)
        {
            var choicePacket = TryGetFriendlyChoicePacket(gameState);
            var choiceCardMgr = TryGetChoiceCardMgr();
            var rawChoiceState = GetRawFriendlyChoiceState(choiceCardMgr);
            if (IsMulliganActiveForChoiceSuppression(gameState, out var mulliganDetail))
            {
                if (choicePacket != null || GetChoiceUiCardCount(choiceCardMgr, rawChoiceState) > 0)
                {
                    AppendChoiceDecisionTrace(
                        "choice_suppressed_during_mulligan",
                        gameState,
                        choicePacket,
                        rawChoiceState,
                        choiceCardMgr,
                        mulliganDetail);
                }

                return false;
            }

            if (TryGetChoicePacketEntityIds(choicePacket, out var packetEntityIds)
                && packetEntityIds.Count > 0)
            {
                return true;
            }

            if (TryGetCurrentFriendlyChoiceState(choiceCardMgr) != null)
                return true;

            if (gameState == null)
                return false;

            if (TryInvokeBoolMethod(gameState, "IsInChoiceMode", out var inChoiceMode))
                return inChoiceMode;

            var responseMode = Invoke(gameState, "GetResponseMode");
            return IsChoiceResponseMode(responseMode);
        }

        private static string GetChoiceModeBySourceTags(object sourceEntity, ChoiceSnapshot snapshot)
        {
            if (snapshot != null && snapshot.ChoiceCardIds != null && snapshot.ChoiceCardIds.Count > 0)
            {
                var hasMaintain = snapshot.ChoiceCardIds.Any(cardId => string.Equals(cardId, "TIME_000ta", StringComparison.OrdinalIgnoreCase));
                var hasRewind = snapshot.ChoiceCardIds.Any(cardId => string.Equals(cardId, "TIME_000tb", StringComparison.OrdinalIgnoreCase));
                if (hasMaintain && hasRewind)
                    return "TIMELINE";
            }

            if (snapshot != null)
            {
                if (snapshot.IsSubOptionChoice && snapshot.IsTitanAbility)
                    return "TITAN_ABILITY";
                if (snapshot.IsLaunchpadAbility)
                    return "STARSHIP_LAUNCH";
                if (snapshot.IsMagicItemDiscover)
                    return "TRINKET_DISCOVER";
                if (snapshot.IsShopChoice)
                    return "SHOP_CHOICE";
            }

            var choiceType = GetChoiceTypeToken(snapshot?.RawChoiceType);
            if (string.Equals(choiceType, "DISCOVER", StringComparison.OrdinalIgnoreCase))
                return "DISCOVER";
            if (string.Equals(choiceType, "DREDGE", StringComparison.OrdinalIgnoreCase))
                return "DREDGE";
            if (string.Equals(choiceType, "ADAPT", StringComparison.OrdinalIgnoreCase))
                return "ADAPT";
            if (!string.IsNullOrWhiteSpace(choiceType)
                && choiceType.IndexOf("TARGET", StringComparison.OrdinalIgnoreCase) >= 0)
                return "TARGET";

            // 与 HB1.1.8 的判定一致：优先用来源实体标签区分具体选择模式。
            if (HasSourceEntityTag(sourceEntity, "DISCOVER")) return "DISCOVER";
            if (HasSourceEntityTag(sourceEntity, "ADAPT")) return "ADAPT";
            if (HasSourceEntityTag(sourceEntity, "DREDGE")) return "DREDGE";

            if (string.Equals(choiceType, "CHOOSE_ONE", StringComparison.OrdinalIgnoreCase))
                return "CHOOSE_ONE";

            if (sourceEntity != null
                && (HasSourceEntityTag(sourceEntity, "CHOOSE_ONE")
                    || SourceHasMechanic(sourceEntity, "CHOOSE_ONE")
                    || SourceTextContainsAny(sourceEntity, "抉择", "choose one")))
            {
                return "CHOOSE_ONE";
            }

            return "GENERAL";
        }

        private static string GetChoiceModeBySourceTags(object sourceEntity)
        {
            return GetChoiceModeBySourceTags(sourceEntity, null);
        }

        private static ChoiceExecutionMechanismKind ResolveChoiceExecutionMechanism(ChoiceSnapshot snapshot)
        {
            if (snapshot == null)
                return ChoiceExecutionMechanismKind.Unknown;

            var normalizedMode = (snapshot.Mode ?? string.Empty).Trim().ToUpperInvariant();
            switch (normalizedMode)
            {
                case "CHOOSE_ONE":
                case "TITAN_ABILITY":
                case "STARSHIP_LAUNCH":
                    return ChoiceExecutionMechanismKind.SubOptionChoice;
                case "DISCOVER":
                case "DREDGE":
                case "ADAPT":
                case "TIMELINE":
                case "TRINKET_DISCOVER":
                case "SHOP_CHOICE":
                case "GENERAL":
                case "TARGET":
                default:
                    return snapshot.IsSubOptionChoice
                        ? ChoiceExecutionMechanismKind.SubOptionChoice
                        : ChoiceExecutionMechanismKind.EntityChoice;
            }
        }

        private static bool TryGetCurrentChoiceSnapshotForEntity(
            int entityId,
            out ChoiceSnapshot snapshot,
            out ChoiceExecutionMechanismKind mechanism)
        {
            snapshot = null;
            mechanism = ChoiceExecutionMechanismKind.Unknown;
            if (entityId <= 0)
                return false;

            var gs = GetGameState();
            if (gs == null || !TryBuildChoiceSnapshot(gs, out var currentSnapshot) || currentSnapshot == null)
                return false;

            if (currentSnapshot.ChoiceEntityIds == null || !currentSnapshot.ChoiceEntityIds.Contains(entityId))
                return false;

            snapshot = currentSnapshot;
            mechanism = ResolveChoiceExecutionMechanism(currentSnapshot);
            return mechanism != ChoiceExecutionMechanismKind.Unknown;
        }

        private static bool IsEntityChoiceUiReady()
        {
            var gs = GetGameState();
            if (gs == null)
                return false;

            var choicePacket = TryGetFriendlyChoicePacket(gs);
            if (TryGetChoicePacketEntityIds(choicePacket, out var entityIds) && entityIds.Count > 0)
                return true;

            if (!TryBuildChoiceSnapshot(gs, out var snapshot) || snapshot == null)
                return false;

            return ResolveChoiceExecutionMechanism(snapshot) == ChoiceExecutionMechanismKind.EntityChoice
                && snapshot.ChoiceEntityIds != null
                && snapshot.ChoiceEntityIds.Count > 0
                && (snapshot.PacketReady || snapshot.ChoiceStateActive || snapshot.UiShown);
        }

        private static bool IsSubOptionUiReady()
        {
            var gs = GetGameState();
            if (gs == null)
                return false;

            var choiceCardMgr = TryGetChoiceCardMgr();
            if (choiceCardMgr != null)
            {
                if (TryInvokeBoolMethod(choiceCardMgr, "HasSubOption", out var hasSubOption) && hasSubOption)
                    return true;

                if (TryInvokeBoolMethod(choiceCardMgr, "IsFriendlyShown", out var isFriendlyShown) && isFriendlyShown)
                {
                    if (!TryBuildChoiceSnapshot(gs, out var shownSnapshot) || shownSnapshot == null)
                        return true;

                    if (ResolveChoiceExecutionMechanism(shownSnapshot) == ChoiceExecutionMechanismKind.SubOptionChoice)
                        return true;
                }
            }

            if (TryInvokeBoolMethod(gs, "IsInSubOptionMode", out var inSubOptionMode) && inSubOptionMode)
                return true;

            if (!TryBuildChoiceSnapshot(gs, out var snapshot) || snapshot == null)
                return false;

            return ResolveChoiceExecutionMechanism(snapshot) == ChoiceExecutionMechanismKind.SubOptionChoice
                && snapshot.ChoiceEntityIds != null
                && snapshot.ChoiceEntityIds.Count > 0
                && (snapshot.UiShown || snapshot.ChoiceStateActive);
        }

        private static object TryGetFriendlyChoicePacket(object gameState)
        {
            var choicePacket = gameState != null ? Invoke(gameState, "GetFriendlyEntityChoices") : null;
            if (choicePacket != null)
                return choicePacket;

            var network = GetSingleton(_networkType);
            return network == null ? null : Invoke(network, "GetEntityChoices");
        }

        private static bool TryGetChoicePacketEntityIds(object choicePacket, out List<int> entityIds)
        {
            entityIds = new List<int>();
            if (choicePacket == null)
                return false;

            var entities = GetFieldOrProp(choicePacket, "Entities") as IEnumerable
                ?? GetFieldOrProp(choicePacket, "m_entities") as IEnumerable
                ?? GetFieldOrProp(choicePacket, "EntityIds") as IEnumerable;
            if (entities == null)
                return false;

            foreach (var rawEntityId in entities)
            {
                try
                {
                    var entityId = Convert.ToInt32(rawEntityId);
                    if (entityId > 0 && !entityIds.Contains(entityId))
                        entityIds.Add(entityId);
                }
                catch
                {
                }
            }

            return entityIds.Count > 0;
        }

        private static string ReadChoiceTypeName(object source)
        {
            if (source == null)
                return string.Empty;

            var raw = GetFieldOrProp(source, "ChoiceType")
                ?? GetFieldOrProp(source, "m_choiceType")
                ?? GetFieldOrProp(source, "Type")
                ?? GetFieldOrProp(source, "m_type")
                ?? GetFieldOrProp(source, "ResponseType")
                ?? GetFieldOrProp(source, "m_responseType");
            if (raw == null)
                return string.Empty;

            try
            {
                return raw.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetChoiceTypeToken(string rawChoiceType)
        {
            if (string.IsNullOrWhiteSpace(rawChoiceType))
                return string.Empty;

            var normalized = rawChoiceType.ToUpperInvariant();
            if (normalized.Contains("CHOOSE_ONE") || normalized.Contains("CHOOSEONE"))
                return "CHOOSE_ONE";
            if (normalized.Contains("DISCOVER"))
                return "DISCOVER";
            if (normalized.Contains("TITAN"))
                return "TITAN";
            if (normalized.Contains("DREDGE"))
                return "DREDGE";
            if (normalized.Contains("ADAPT"))
                return "ADAPT";
            if (normalized.Contains("GENERAL"))
                return "GENERAL";
            return normalized;
        }

        private static bool SourceHasMechanic(object sourceEntity, string mechanicName)
        {
            if (sourceEntity == null || string.IsNullOrWhiteSpace(mechanicName))
                return false;

            foreach (var candidate in CollectChoiceSourceCandidates(sourceEntity))
            {
                if (candidate == null)
                    continue;

                if (ReferenceEquals(candidate, sourceEntity) && HasSourceEntityTag(candidate, mechanicName))
                    return true;

                var mechanicSources = new[]
                {
                    GetFieldOrProp(candidate, "Mechanics"),
                    GetFieldOrProp(candidate, "m_mechanics"),
                    GetFieldOrProp(candidate, "CardTags"),
                    GetFieldOrProp(candidate, "Tags"),
                    Invoke(candidate, "GetMechanics")
                };

                foreach (var mechanicSource in mechanicSources)
                {
                    if (MechanicCollectionContains(mechanicSource, mechanicName))
                        return true;
                }
            }

            return false;
        }

        private static bool MechanicCollectionContains(object source, string mechanicName)
        {
            if (source == null || string.IsNullOrWhiteSpace(mechanicName))
                return false;

            if (source is string directText)
                return directText.IndexOf(mechanicName, StringComparison.OrdinalIgnoreCase) >= 0;

            if (source is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item == null)
                        continue;

                    var text = item.ToString();
                    if (!string.IsNullOrWhiteSpace(text)
                        && text.IndexOf(mechanicName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            var fallback = source.ToString();
            return !string.IsNullOrWhiteSpace(fallback)
                && fallback.IndexOf(mechanicName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool SourceTextContainsAny(object sourceEntity, params string[] keywords)
        {
            if (sourceEntity == null || keywords == null || keywords.Length == 0)
                return false;

            foreach (var candidate in CollectChoiceSourceCandidates(sourceEntity))
            {
                if (candidate == null)
                    continue;

                foreach (var memberName in ChoiceTextMemberNames)
                {
                    var text = ReadObjectMemberAsString(candidate, memberName);
                    if (!string.IsNullOrWhiteSpace(text) && TextContainsAny(text, keywords))
                        return true;
                }
            }

            return false;
        }

        private static List<object> CollectChoiceSourceCandidates(object sourceEntity)
        {
            var candidates = new List<object>();
            AddDistinctObjectCandidate(candidates, sourceEntity);

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (candidate == null)
                    continue;

                AddDistinctObjectCandidate(candidates, Invoke(candidate, "GetCard"));
                AddDistinctObjectCandidate(candidates, GetFieldOrProp(candidate, "Card"));
                AddDistinctObjectCandidate(candidates, GetFieldOrProp(candidate, "m_card"));
                AddDistinctObjectCandidate(candidates, GetFieldOrProp(candidate, "Template"));
                AddDistinctObjectCandidate(candidates, GetFieldOrProp(candidate, "m_template"));
                AddDistinctObjectCandidate(candidates, GetFieldOrProp(candidate, "CardDef"));
                AddDistinctObjectCandidate(candidates, GetFieldOrProp(candidate, "m_cardDef"));
                AddDistinctObjectCandidate(candidates, GetFieldOrProp(candidate, "EntityDef"));
                AddDistinctObjectCandidate(candidates, GetFieldOrProp(candidate, "m_entityDef"));
            }

            return candidates;
        }

        private static string ReadObjectMemberAsString(object source, string memberName)
        {
            if (source == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            var type = source.GetType();

            try
            {
                var prop = type.GetProperty(memberName, flags);
                if (prop != null)
                {
                    var value = prop.GetValue(source, null);
                    if (value != null)
                        return value.ToString();
                }
            }
            catch
            {
            }

            try
            {
                var field = type.GetField(memberName, flags);
                if (field != null)
                {
                    var value = field.GetValue(source);
                    if (value != null)
                        return value.ToString();
                }
            }
            catch
            {
            }

            try
            {
                var method = type.GetMethod(memberName, flags, null, Type.EmptyTypes, null);
                if (method != null)
                {
                    var value = method.Invoke(source, null);
                    if (value != null)
                        return value.ToString();
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool TextContainsAny(string text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length == 0)
                return false;

            foreach (var keyword in keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword)
                    && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static object TryGetChoiceCardMgr()
        {
            if (!EnsureTypes())
                return null;

            if (_choiceCardMgrType == null)
                _choiceCardMgrType = _asm?.GetType("ChoiceCardMgr");
            if (_choiceCardMgrType == null)
                return null;

            try
            {
                return GetSingleton(_choiceCardMgrType);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 检查 choice packet 的 ChoiceType 是否为 MULLIGAN。
        /// CHOICE_TYPE 枚举: INVALID=0, MULLIGAN=1, GENERAL=2, TARGET=3
        /// 留牌包的 ChoiceType 为 MULLIGAN，不应被当作发现选择处理。
        /// </summary>
        private static bool IsChoicePacketMulligan(object choicePacket)
        {
            if (choicePacket == null) return false;
            var rawType = ReadChoiceTypeName(choicePacket);
            return rawType.IndexOf("MULLIGAN", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsChoicePacketGeneral(object choicePacket)
        {
            if (choicePacket == null)
                return false;

            var rawType = ReadChoiceTypeName(choicePacket);
            return rawType.IndexOf("GENERAL", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsChoicePacketTarget(object choicePacket)
        {
            if (choicePacket == null)
                return false;

            var rawType = ReadChoiceTypeName(choicePacket);
            return rawType.IndexOf("TARGET", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSupportedChoicePacket(object choicePacket)
        {
            return IsChoicePacketGeneral(choicePacket) || IsChoicePacketTarget(choicePacket);
        }

        private static bool TryGetChoiceCardMgrFriendlyCards(out List<object> cards)
        {
            cards = null;

            var choiceCardMgr = TryGetChoiceCardMgr();
            if (choiceCardMgr == null)
                return false;

            var friendlyCards = Invoke(choiceCardMgr, "GetFriendlyCards") as IEnumerable;
            if (friendlyCards == null)
            {
                var choiceState = TryGetCurrentFriendlyChoiceState(choiceCardMgr);
                friendlyCards = GetFieldOrProp(choiceState, "m_cards") as IEnumerable
                    ?? GetFieldOrProp(choiceState, "Cards") as IEnumerable;
            }

            if (friendlyCards == null)
                return false;

            cards = new List<object>();
            foreach (var card in friendlyCards)
            {
                if (card != null)
                    cards.Add(card);
            }

            return true;
        }

        private static object TryGetCurrentFriendlyChoiceState(object choiceCardMgr)
        {
            if (choiceCardMgr == null)
                return null;

            var gameState = GetGameState();
            var choicePacket = TryGetFriendlyChoicePacket(gameState);
            var hasGeneralPacket = IsChoicePacketGeneral(choicePacket);
            var currentChoiceState = GetRawFriendlyChoiceState(choiceCardMgr);
            var hasUsableCurrentChoiceUi = HasFriendlyChoiceUi(choiceCardMgr, currentChoiceState);

            if (IsMulliganActiveForChoiceSuppression(gameState, out var mulliganDetail))
            {
                if (currentChoiceState != null || GetChoiceUiCardCount(choiceCardMgr, currentChoiceState) > 0)
                {
                    AppendChoiceDecisionTrace(
                        "choice_ui_fallback_rejected_mulligan_active",
                        gameState,
                        choicePacket,
                        currentChoiceState,
                        choiceCardMgr,
                        mulliganDetail);
                }

                return null;
            }

            if (IsFriendlyChoiceStateActive(currentChoiceState))
            {
                if (!hasGeneralPacket)
                {
                    AppendChoiceDecisionTrace(
                        "choice_ui_fallback_rejected_no_general_packet",
                        gameState,
                        choicePacket,
                        currentChoiceState,
                        choiceCardMgr,
                        "current_choice_state_active");
                    return null;
                }

                return currentChoiceState;
            }

            if (!hasGeneralPacket)
            {
                if (hasUsableCurrentChoiceUi)
                {
                    AppendChoiceDecisionTrace(
                        "choice_ui_fallback_rejected_no_general_packet",
                        gameState,
                        choicePacket,
                        currentChoiceState,
                        choiceCardMgr,
                        "friendly_choice_ui_visible");
                }

                return null;
            }

            var choiceState = GetFieldOrProp(choiceCardMgr, "m_lastShownChoiceState")
                ?? GetFieldOrProp(choiceCardMgr, "lastShownChoiceState")
                ?? GetFieldOrProp(choiceCardMgr, "LastShownChoiceState");
            if (!HasFriendlyChoiceUi(choiceCardMgr, choiceState))
                return null;

            return IsFriendlyChoiceStateActive(choiceState) ? choiceState : null;
        }

        private static object GetRawFriendlyChoiceState(object choiceCardMgr)
        {
            if (choiceCardMgr == null)
                return null;

            if (TryInvokeMethod(choiceCardMgr, "GetFriendlyChoiceState", Array.Empty<object>(), out var currentChoiceState))
                return currentChoiceState;

            return GetFieldOrProp(choiceCardMgr, "m_lastShownChoiceState")
                ?? GetFieldOrProp(choiceCardMgr, "lastShownChoiceState")
                ?? GetFieldOrProp(choiceCardMgr, "LastShownChoiceState");
        }

        private static bool IsFriendlyChoiceState(object choiceState)
        {
            if (choiceState == null)
                return false;

            var flag = GetFieldOrProp(choiceState, "m_isFriendly")
                ?? GetFieldOrProp(choiceState, "IsFriendly")
                ?? GetFieldOrProp(choiceState, "isFriendly");

            if (flag is bool boolFlag)
                return boolFlag;

            try
            {
                return flag != null && Convert.ToBoolean(flag);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFriendlyChoiceStateActive(object choiceState)
        {
            return IsFriendlyChoiceState(choiceState) && !IsChoiceStateConcealed(choiceState);
        }

        private static bool IsChoiceStateConcealed(object choiceState)
        {
            if (choiceState == null)
                return false;

            var concealed = GetFieldOrProp(choiceState, "m_hasBeenConcealed")
                ?? GetFieldOrProp(choiceState, "HasBeenConcealed")
                ?? GetFieldOrProp(choiceState, "hasBeenConcealed");

            if (concealed is bool boolFlag)
                return boolFlag;

            try
            {
                return concealed != null && Convert.ToBoolean(concealed);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsChoiceStateWaitingToStart(object choiceState)
        {
            if (choiceState == null)
                return false;

            var waiting = GetFieldOrProp(choiceState, "m_waitingToStart")
                ?? GetFieldOrProp(choiceState, "WaitingToStart")
                ?? GetFieldOrProp(choiceState, "waitingToStart");

            if (waiting is bool boolFlag)
                return boolFlag;

            try
            {
                return waiting != null && Convert.ToBoolean(waiting);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsChoiceStateRevealed(object choiceState)
        {
            if (choiceState == null)
                return false;

            var revealed = GetFieldOrProp(choiceState, "m_hasBeenRevealed")
                ?? GetFieldOrProp(choiceState, "HasBeenRevealed")
                ?? GetFieldOrProp(choiceState, "hasBeenRevealed");

            if (revealed is bool boolFlag)
                return boolFlag;

            try
            {
                return revealed != null && Convert.ToBoolean(revealed);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMulliganActiveForChoiceSuppression(object gameState, out string detail)
        {
            detail = "not_active";

            var mulliganMgr = TryGetMulliganManager();
            if (mulliganMgr != null
                && TryInvokeBoolMethod(mulliganMgr, "IsMulliganActive", out var mulliganManagerActive)
                && mulliganManagerActive)
            {
                detail = "mulligan_manager_active";
                return true;
            }

            if (gameState != null
                && TryInvokeBoolMethod(gameState, "IsMulliganManagerActive", out var gameStateMulliganActive)
                && gameStateMulliganActive)
            {
                detail = "game_state_mulligan_active";
                return true;
            }

            var gameEntity = gameState != null ? Invoke(gameState, "GetGameEntity") : null;
            if (gameEntity != null
                && TryInvokeBoolMethod(gameEntity, "IsMulliganActiveRealTime", out var realTimeMulliganActive)
                && realTimeMulliganActive)
            {
                detail = "game_entity_mulligan_realtime";
                return true;
            }

            return false;
        }

        private static int GetChoiceUiCardCount(object choiceCardMgr, object choiceState)
        {
            IEnumerable friendlyCards = null;
            if (choiceCardMgr != null)
                friendlyCards = Invoke(choiceCardMgr, "GetFriendlyCards") as IEnumerable;

            if (friendlyCards == null)
            {
                friendlyCards = GetFieldOrProp(choiceState, "m_cards") as IEnumerable
                    ?? GetFieldOrProp(choiceState, "Cards") as IEnumerable;
            }

            if (friendlyCards == null)
                return 0;

            var count = 0;
            foreach (var card in friendlyCards)
            {
                if (card != null)
                    count++;
            }

            return count;
        }

        private static int GetChoiceSourceEntityId(object choicePacket, object choiceState)
        {
            var sourceEntityId = GetIntFieldOrProp(choicePacket, "Source");
            if (sourceEntityId <= 0)
                sourceEntityId = GetIntFieldOrProp(choicePacket, "m_source");
            if (sourceEntityId <= 0)
                sourceEntityId = GetIntFieldOrProp(choicePacket, "SourceEntityId");
            if (sourceEntityId <= 0)
                sourceEntityId = GetIntFieldOrProp(choiceState, "m_sourceEntityId");
            if (sourceEntityId <= 0)
                sourceEntityId = GetIntFieldOrProp(choiceState, "SourceEntityId");
            if (sourceEntityId <= 0)
                sourceEntityId = GetIntFieldOrProp(choiceState, "Source");
            return sourceEntityId;
        }

        private static void AppendChoiceDecisionTrace(
            string label,
            object gameState,
            object choicePacket,
            object choiceState,
            object choiceCardMgr,
            string detail)
        {
            var responseMode = Invoke(gameState, "GetResponseMode")?.ToString() ?? "null";
            var choicePacketType = choicePacket == null ? "null" : ReadChoiceTypeName(choicePacket);
            if (string.IsNullOrWhiteSpace(choicePacketType))
                choicePacketType = "unknown";

            var uiCardCount = GetChoiceUiCardCount(choiceCardMgr, choiceState);
            var sourceEntityId = GetChoiceSourceEntityId(choicePacket, choiceState);
            var message =
                label
                + " responseMode=" + responseMode
                + " choicePacketType=" + choicePacketType
                + " uiCardCount=" + uiCardCount
                + " sourceEntityId=" + sourceEntityId
                + " detail=" + (detail ?? "null");
            if (!ShouldAppendChoiceDecisionTrace(message))
                return;

            AppendActionTrace(message);
        }

        private static bool HasFriendlyChoiceUi(object choiceCardMgr)
        {
            return HasFriendlyChoiceUi(choiceCardMgr, null);
        }

        private static bool HasFriendlyChoiceUi(object choiceCardMgr, object choiceState)
        {
            if (choiceCardMgr == null)
                return false;

            if (GetChoiceUiCardCount(choiceCardMgr, choiceState) > 0)
                return true;

            if (IsFriendlyChoiceStateActive(choiceState))
                return true;

            return IsFriendlyChoiceState(choiceState)
                && (IsChoiceStateWaitingToStart(choiceState) || IsChoiceStateRevealed(choiceState))
                && !IsChoiceStateConcealed(choiceState);
        }

        private static bool ShouldAppendChoiceDecisionTrace(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            lock (_choiceDecisionTraceSync)
            {
                var nowTick = Environment.TickCount;
                if (string.Equals(_lastChoiceDecisionTrace, message, StringComparison.Ordinal)
                    && unchecked(nowTick - _lastChoiceDecisionTraceTick) < ChoiceDecisionTraceDedupWindowMs)
                {
                    return false;
                }

                _lastChoiceDecisionTrace = message;
                _lastChoiceDecisionTraceTick = nowTick;
                return true;
            }
        }

        private static bool TryAppendEntityIdsFromEnumerable(object source, List<int> entityIds)
        {
            if (!(source is IEnumerable enumerable) || entityIds == null)
                return false;

            var added = false;
            foreach (var item in enumerable)
            {
                if (item == null)
                    continue;

                var entityId = 0;
                try
                {
                    entityId = Convert.ToInt32(item);
                }
                catch
                {
                    entityId = ResolveEntityId(item);
                }

                if (entityId > 0 && !entityIds.Contains(entityId))
                {
                    entityIds.Add(entityId);
                    added = true;
                }
            }

            return added;
        }

        private static List<int> GetChosenEntityIds(object gameState, object choiceState, object choiceCardMgr)
        {
            var entityIds = new List<int>();

            if (gameState != null)
            {
                if (TryInvokeMethod(gameState, "GetChosenEntities", Array.Empty<object>(), out var chosenEntities))
                    TryAppendEntityIdsFromEnumerable(chosenEntities, entityIds);

                if (entityIds.Count == 0)
                {
                    TryAppendEntityIdsFromEnumerable(
                        GetFieldOrProp(gameState, "m_chosenEntities")
                        ?? GetFieldOrProp(gameState, "ChosenEntities"),
                        entityIds);
                }
            }

            if (entityIds.Count == 0 && choiceState != null)
            {
                TryAppendEntityIdsFromEnumerable(
                    GetFieldOrProp(choiceState, "m_chosenEntities")
                    ?? GetFieldOrProp(choiceState, "ChosenEntities"),
                    entityIds);
            }

            if (entityIds.Count == 0 && choiceCardMgr != null)
            {
                TryAppendEntityIdsFromEnumerable(
                    GetFieldOrProp(choiceCardMgr, "m_lastChosenEntityIds")
                    ?? GetFieldOrProp(choiceCardMgr, "LastChosenEntityIds"),
                    entityIds);
            }

            return entityIds;
        }

        private static bool TryResolveChoiceSourceEntity(object gameState, object choiceCardMgr, object choicePacket, out int sourceEntityId, out object sourceEntity)
        {
            sourceEntityId = 0;
            sourceEntity = null;

            if (choicePacket != null)
            {
                sourceEntityId = GetIntFieldOrProp(choicePacket, "Source");
                if (sourceEntityId <= 0) sourceEntityId = GetIntFieldOrProp(choicePacket, "m_source");
                if (sourceEntityId <= 0) sourceEntityId = GetIntFieldOrProp(choicePacket, "SourceEntityId");
            }

            if (sourceEntityId <= 0)
            {
                var choiceState = TryGetCurrentFriendlyChoiceState(choiceCardMgr);
                if (choiceState != null)
                {
                    sourceEntityId = GetIntFieldOrProp(choiceState, "m_sourceEntityId");
                    if (sourceEntityId <= 0) sourceEntityId = GetIntFieldOrProp(choiceState, "SourceEntityId");
                    if (sourceEntityId <= 0) sourceEntityId = GetIntFieldOrProp(choiceState, "Source");
                }
            }

            if (sourceEntityId > 0 && gameState != null)
                sourceEntity = GetEntity(gameState, sourceEntityId);

            return sourceEntityId > 0 || sourceEntity != null;
        }

        private static bool TryBuildChoiceSnapshot(object gameState, out ChoiceSnapshot snapshot)
        {
            snapshot = null;
            if (gameState == null)
                return false;

            var choicePacket = TryGetFriendlyChoicePacket(gameState);
            var choiceCardMgr = TryGetChoiceCardMgr();
            var rawChoiceState = GetRawFriendlyChoiceState(choiceCardMgr);

            if (IsMulliganActiveForChoiceSuppression(gameState, out var mulliganDetail))
            {
                if (choicePacket != null || GetChoiceUiCardCount(choiceCardMgr, rawChoiceState) > 0)
                {
                    AppendChoiceDecisionTrace(
                        "choice_suppressed_during_mulligan",
                        gameState,
                        choicePacket,
                        rawChoiceState,
                        choiceCardMgr,
                        mulliganDetail);
                }

                return false;
            }

            var mulliganMgr = TryGetMulliganManager();
            if (mulliganMgr != null
                && TryInvokeBoolMethod(mulliganMgr, "IsMulliganActive", out var mulliganActive)
                && mulliganActive)
            {
                return false;
            }

            var choiceState = TryGetCurrentFriendlyChoiceState(choiceCardMgr);

            // 留牌包的 ChoiceType 为 MULLIGAN，不应被当作发现选择处理。
            // 这是最精准的过滤：ChoiceType 来自服务器网络包，不依赖 UI 单例初始化。
            if (IsChoicePacketMulligan(choicePacket))
                return false;

            if (choicePacket != null && !IsSupportedChoicePacket(choicePacket))
                return false;

            var built = new ChoiceSnapshot();
            built.InChoiceMode = IsChoiceModeActive(gameState);
            built.PacketReady = TryGetChoicePacketEntityIds(choicePacket, out _);
            built.ChoiceStateActive = choiceState != null;
            built.ChoiceStateWaitingToStart = IsChoiceStateWaitingToStart(choiceState);
            built.ChoiceStateRevealed = IsChoiceStateRevealed(choiceState);
            built.ChoiceStateConcealed = IsChoiceStateConcealed(choiceState);
            built.IsSubOptionChoice = ReadBoolValue(GetFieldOrProp(choiceState, "m_isSubOptionChoice"));
            built.IsTitanAbility = ReadBoolValue(GetFieldOrProp(choiceState, "m_isTitanAbility"));
            built.IsRewindChoice = ReadBoolValue(GetFieldOrProp(choiceState, "m_isRewindChoice"));
            built.IsMagicItemDiscover = ReadBoolValue(GetFieldOrProp(choiceState, "m_isMagicItemDiscover"));
            built.IsShopChoice = ReadBoolValue(GetFieldOrProp(choiceState, "m_isShopChoice"));
            built.IsLaunchpadAbility = ReadBoolValue(GetFieldOrProp(choiceState, "m_isLaunchpadAbility"));
            built.UiShown = TryGetChoiceCardMgrFriendlyCards(out var friendlyCards)
                && friendlyCards != null
                && friendlyCards.Count > 0;

            if (!built.InChoiceMode && !built.PacketReady && !built.ChoiceStateActive)
                return false;

            if (choicePacket != null)
            {
                built.ChoiceId = GetIntFieldOrProp(choicePacket, "ID");
                if (built.ChoiceId <= 0) built.ChoiceId = GetIntFieldOrProp(choicePacket, "Id");
                built.CountMin = GetIntFieldOrProp(choicePacket, "CountMin");
                built.CountMax = GetIntFieldOrProp(choicePacket, "CountMax");
                built.RawChoiceType = ReadChoiceTypeName(choicePacket);
            }

            if (string.IsNullOrWhiteSpace(built.RawChoiceType))
                built.RawChoiceType = ReadChoiceTypeName(choiceState);

            TryResolveChoiceSourceEntity(gameState, choiceCardMgr, choicePacket, out var sourceEntityId, out var sourceEntity);
            built.SourceEntityId = sourceEntityId;
            built.SourceCardId = ResolveCardIdFromObject(sourceEntity);
            if (string.IsNullOrWhiteSpace(built.SourceCardId) && built.SourceEntityId > 0)
                built.SourceCardId = ResolveEntityCardId(gameState, built.SourceEntityId);

            if (TryBuildChoicePartsFromPacket(gameState, choicePacket, out var packetParts))
                MergeChoicePartsIntoSnapshot(built, packetParts);
            if (TryBuildChoicePartsFromChoiceCardMgr(gameState, out var uiParts))
                MergeChoicePartsIntoSnapshot(built, uiParts);

            if (built.ChoiceEntityIds.Count == 0)
                return false;

            built.ChosenEntityIds = GetChosenEntityIds(gameState, choiceState, choiceCardMgr)
                .Where(id => built.ChoiceEntityIds.Contains(id))
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            built.Mode = GetChoiceModeBySourceTags(sourceEntity, built);
            if (built.CountMax <= 0)
                built.CountMax = 1;
            if (built.CountMin < 0)
                built.CountMin = 0;
            if (built.CountMin > built.CountMax)
                built.CountMax = built.CountMin;
            built.Signature = BuildChoiceSignature(built);
            built.SnapshotId = BuildChoiceSnapshotId(built.Signature);
            snapshot = built;
            return true;
        }

        private static void MergeChoicePartsIntoSnapshot(ChoiceSnapshot snapshot, IEnumerable<string> parts)
        {
            if (snapshot == null || parts == null)
                return;

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                var commaIndex = part.LastIndexOf(',');
                if (commaIndex < 0 || commaIndex >= part.Length - 1)
                    continue;

                if (!int.TryParse(part.Substring(commaIndex + 1), out var entityId) || entityId <= 0)
                    continue;

                var cardId = commaIndex > 0 ? part.Substring(0, commaIndex) : string.Empty;
                UpsertChoicePart(snapshot, cardId, entityId);
            }
        }

        private static void UpsertChoicePart(ChoiceSnapshot snapshot, string cardId, int entityId)
        {
            if (snapshot == null || entityId <= 0)
                return;

            cardId = cardId ?? string.Empty;
            var index = snapshot.ChoiceEntityIds.IndexOf(entityId);
            var serialized = cardId + "," + entityId;

            if (index >= 0)
            {
                if (string.IsNullOrWhiteSpace(snapshot.ChoiceCardIds[index]) && !string.IsNullOrWhiteSpace(cardId))
                {
                    snapshot.ChoiceCardIds[index] = cardId;
                    snapshot.ChoiceParts[index] = serialized;
                }

                return;
            }

            snapshot.ChoiceEntityIds.Add(entityId);
            snapshot.ChoiceCardIds.Add(cardId);
            snapshot.ChoiceParts.Add(serialized);
        }

        private static string BuildChoiceSignature(ChoiceSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            return snapshot.ChoiceId
                + ":"
                + snapshot.SourceEntityId
                + ":"
                + snapshot.Mode
                + ":"
                + string.Join(";", snapshot.ChoiceParts ?? new List<string>())
                + ":chosen="
                + string.Join(",", snapshot.ChosenEntityIds ?? new List<int>());
        }

        private static string BuildChoiceSnapshotId(string signature)
        {
            signature = signature ?? string.Empty;
            using (var sha1 = SHA1.Create())
            {
                var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(signature));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes)
                    builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }

        private static string SerializeChoiceSnapshot(ChoiceSnapshot snapshot)
        {
            if (snapshot == null || snapshot.ChoiceParts == null || snapshot.ChoiceParts.Count == 0)
                return null;

            var payload = (snapshot.SourceCardId ?? string.Empty)
                + "|"
                + string.Join(";", snapshot.ChoiceParts)
                + "|"
                + (string.IsNullOrWhiteSpace(snapshot.Mode) ? "GENERAL" : snapshot.Mode);

            if (snapshot.ChosenEntityIds != null && snapshot.ChosenEntityIds.Count > 0)
                payload += "|chosen=" + string.Join(",", snapshot.ChosenEntityIds);

            return payload;
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length + 16);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (ch < 32)
                            builder.Append("\\u").Append(((int)ch).ToString("x4"));
                        else
                            builder.Append(ch);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string SerializeChoiceSnapshotJson(ChoiceSnapshot snapshot)
        {
            if (snapshot == null || snapshot.ChoiceEntityIds == null || snapshot.ChoiceEntityIds.Count == 0)
                return null;

            var isReady = IsChoiceSnapshotReady(snapshot, out var readyReason);
            var builder = new StringBuilder(512);
            builder.Append('{');
            AppendJsonProperty(builder, "snapshotId", snapshot.SnapshotId, isFirst: true);
            AppendJsonProperty(builder, "choiceId", snapshot.ChoiceId);
            AppendJsonProperty(builder, "mode", snapshot.Mode ?? "GENERAL");
            AppendJsonProperty(builder, "rawChoiceType", snapshot.RawChoiceType ?? string.Empty);
            AppendJsonProperty(builder, "sourceEntityId", snapshot.SourceEntityId);
            AppendJsonProperty(builder, "sourceCardId", snapshot.SourceCardId ?? string.Empty);
            AppendJsonProperty(builder, "countMin", snapshot.CountMin);
            AppendJsonProperty(builder, "countMax", snapshot.CountMax);
            AppendJsonProperty(builder, "isReady", isReady);
            AppendJsonProperty(builder, "readyReason", readyReason ?? string.Empty);
            AppendJsonProperty(builder, "isSubOption", snapshot.IsSubOptionChoice);
            AppendJsonProperty(builder, "isTitanAbility", snapshot.IsTitanAbility);
            AppendJsonProperty(builder, "isRewindChoice", snapshot.IsRewindChoice);
            AppendJsonProperty(builder, "isMagicItemDiscover", snapshot.IsMagicItemDiscover);
            AppendJsonProperty(builder, "isShopChoice", snapshot.IsShopChoice);
            AppendJsonProperty(builder, "isLaunchpadAbility", snapshot.IsLaunchpadAbility);
            AppendJsonProperty(builder, "uiShown", snapshot.UiShown);
            AppendJsonIntArray(builder, "selectedEntityIds", snapshot.ChosenEntityIds);
            builder.Append(",\"options\":[");
            for (var i = 0; i < snapshot.ChoiceEntityIds.Count; i++)
            {
                if (i > 0)
                    builder.Append(',');

                var entityId = snapshot.ChoiceEntityIds[i];
                var cardId = i < snapshot.ChoiceCardIds.Count ? snapshot.ChoiceCardIds[i] : string.Empty;
                var selected = snapshot.ChosenEntityIds != null && snapshot.ChosenEntityIds.Contains(entityId);
                builder.Append('{');
                AppendJsonProperty(builder, "entityId", entityId, isFirst: true);
                AppendJsonProperty(builder, "cardId", cardId ?? string.Empty);
                AppendJsonProperty(builder, "selected", selected);
                builder.Append('}');
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, string value, bool isFirst = false)
        {
            if (!isFirst)
                builder.Append(',');

            builder.Append('"').Append(name).Append("\":\"").Append(EscapeJson(value)).Append('"');
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, int value, bool isFirst = false)
        {
            if (!isFirst)
                builder.Append(',');

            builder.Append('"').Append(name).Append("\":").Append(value);
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, bool value, bool isFirst = false)
        {
            if (!isFirst)
                builder.Append(',');

            builder.Append('"').Append(name).Append("\":").Append(value ? "true" : "false");
        }

        private static void AppendJsonIntArray(StringBuilder builder, string name, IEnumerable<int> values)
        {
            builder.Append(",\"").Append(name).Append("\":[");
            var first = true;
            foreach (var value in values ?? Enumerable.Empty<int>())
            {
                if (!first)
                    builder.Append(',');
                builder.Append(value);
                first = false;
            }

            builder.Append(']');
        }

        private static string EscapeDiscoverStateValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("|", "/").Replace(";", ",");
        }

        private static bool IsChoiceSnapshotReady(ChoiceSnapshot snapshot, out string detail)
        {
            detail = "ready";
            if (snapshot == null)
            {
                detail = "choice_state_unavailable";
                return false;
            }

            if (IsTargetChoiceSnapshot(snapshot))
            {
                var handBlocking = GetHandZoneBlockingReason(GetGameState());
                if (!string.IsNullOrWhiteSpace(handBlocking))
                {
                    detail = handBlocking;
                    return false;
                }

                foreach (var entityId in snapshot.ChoiceEntityIds)
                {
                    if (!TryGetChoiceEntityScreenPos(entityId, out _, out _))
                    {
                        detail = "target_pos_not_found:" + entityId;
                        return false;
                    }
                }

                return true;
            }

            if (snapshot.ChoiceStateWaitingToStart)
            {
                detail = "waiting_to_start";
                return false;
            }

            if (!snapshot.ChoiceStateRevealed)
            {
                detail = "not_revealed";
                return false;
            }

            if (snapshot.ChoiceStateConcealed)
            {
                detail = "concealed";
                return false;
            }

            foreach (var entityId in snapshot.ChoiceEntityIds)
            {
                if (!TryGetFriendlyChoiceCardObject(entityId, out var card) || card == null)
                {
                    detail = "card_missing:" + entityId;
                    return false;
                }

                if (!TryReadCardActorReady(card, out var actorReady) || !actorReady)
                {
                    detail = "actor_not_ready:" + entityId;
                    return false;
                }

                if (TryReadCardHasActiveTweens(card, out var hasActiveTweens) && hasActiveTweens)
                {
                    detail = "card_tween_active:" + entityId;
                    return false;
                }

                if (!TryGetFriendlyChoiceCardScreenPos(entityId, out _, out _))
                {
                    detail = "pos_not_found:" + entityId;
                    return false;
                }
            }

            return true;
        }

        private static bool IsDiscoverSnapshotReady(ChoiceSnapshot snapshot, out string detail)
        {
            return IsChoiceSnapshotReady(snapshot, out detail);
        }

        private static bool TryBuildChoicePartsFromChoiceCardMgr(object gameState, out List<string> parts)
        {
            parts = null;

            if (!IsChoicePacketGeneral(TryGetFriendlyChoicePacket(gameState)))
                return false;

            if (!TryGetChoiceCardMgrFriendlyCards(out var friendlyCards)
                || friendlyCards == null
                || friendlyCards.Count == 0)
            {
                return false;
            }

            var result = new List<string>();
            foreach (var card in friendlyCards)
            {
                var entity = ResolveChoiceCardEntity(card);
                if (entity == null)
                    continue;

                var entityId = ResolveEntityId(entity);
                if (entityId <= 0)
                    continue;

                var cardId = ResolveCardIdFromObject(entity);
                if (string.IsNullOrWhiteSpace(cardId))
                    cardId = ResolveCardIdFromObject(card);
                if (string.IsNullOrWhiteSpace(cardId) && gameState != null)
                    cardId = ResolveEntityCardId(gameState, entityId);

                result.Add((cardId ?? string.Empty) + "," + entityId);
            }

            if (result.Count == 0)
                return false;

            parts = result;
            return true;
        }

        private static bool TryBuildChoicePartsFromPacket(object gameState, object choicePacket, out List<string> parts)
        {
            parts = null;
            if (choicePacket == null)
                return false;

            if (!TryGetChoicePacketEntityIds(choicePacket, out var entities) || entities.Count == 0)
                return false;

            var result = new List<string>();
            foreach (var id in entities)
            {
                var entity = GetEntity(gameState, id);
                var cardId = entity != null ? ResolveCardIdFromObject(entity) : string.Empty;
                if (string.IsNullOrWhiteSpace(cardId) && gameState != null)
                    cardId = ResolveEntityCardId(gameState, id);
                result.Add((cardId ?? string.Empty) + "," + id);
            }

            if (result.Count == 0)
                return false;

            parts = result;
            return true;
        }

        private static object ResolveChoiceCardEntity(object card)
        {
            if (card == null)
                return null;

            return Invoke(card, "GetEntity")
                ?? GetFieldOrProp(card, "Entity")
                ?? GetFieldOrProp(card, "m_entity")
                ?? card;
        }

        private static bool TryGetFriendlyChoiceCardObject(int entityId, out object card)
        {
            card = null;
            if (entityId <= 0)
                return false;

            if (!TryGetChoiceCardMgrFriendlyCards(out var friendlyCards)
                || friendlyCards == null
                || friendlyCards.Count == 0)
            {
                return false;
            }

            foreach (var candidate in friendlyCards)
            {
                var entity = ResolveChoiceCardEntity(candidate);
                if (ResolveEntityId(entity) != entityId)
                    continue;

                card = candidate;
                return true;
            }

            return false;
        }

        private static bool TryGetFriendlyChoiceCardScreenPos(int entityId, out int x, out int y)
        {
            x = y = 0;
            if (!TryGetFriendlyChoiceCardObject(entityId, out var card) || card == null)
                return false;

            return GameObjectFinder.GetObjectScreenPos(card, out x, out y);
        }

        private static bool TryGetChoiceEntityScreenPos(int entityId, out int x, out int y)
        {
            x = y = 0;
            if (entityId <= 0)
                return false;

            if (TryGetFriendlyChoiceCardScreenPos(entityId, out x, out y))
                return true;

            if (IsFriendlyHeroEntityId(entityId))
                return GameObjectFinder.GetHeroScreenPos(true, out x, out y);

            if (IsEnemyHeroEntityId(entityId))
                return GameObjectFinder.GetHeroScreenPos(false, out x, out y);

            return GameObjectFinder.GetEntityScreenPos(entityId, out x, out y);
        }

        private static bool IsTargetChoiceSnapshot(ChoiceSnapshot snapshot)
        {
            if (snapshot == null)
                return false;

            if (!string.IsNullOrWhiteSpace(snapshot.Mode)
                && snapshot.Mode.IndexOf("TARGET", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return !string.IsNullOrWhiteSpace(snapshot.RawChoiceType)
                && snapshot.RawChoiceType.IndexOf("TARGET", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryReadCardHasActiveTweens(object card, out bool hasActiveTweens)
        {
            hasActiveTweens = false;
            if (card == null)
                return false;

            if (_iTweenType == null)
            {
                _iTweenType = _asm?.GetType("iTween")
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("iTween"))
                        .FirstOrDefault(t => t != null);
            }

            if (_iTweenType == null)
                return false;

            var cardGameObject = GetFieldOrProp(card, "gameObject");
            if (cardGameObject == null)
                return false;

            try
            {
                var countMethod = _iTweenType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (!string.Equals(m.Name, "Count", StringComparison.OrdinalIgnoreCase))
                            return false;

                        var parameters = m.GetParameters();
                        return parameters.Length == 1
                            && parameters[0].ParameterType.IsInstanceOfType(cardGameObject);
                    });

                if (countMethod != null)
                {
                    var countValue = countMethod.Invoke(null, new[] { cardGameObject });
                    hasActiveTweens = Convert.ToInt32(countValue) > 0;
                    return true;
                }

                var hasTweenMethod = _iTweenType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (!string.Equals(m.Name, "HasTween", StringComparison.OrdinalIgnoreCase))
                            return false;

                        var parameters = m.GetParameters();
                        return parameters.Length == 1
                            && parameters[0].ParameterType.IsInstanceOfType(cardGameObject);
                    });

                if (hasTweenMethod != null)
                {
                    hasActiveTweens = Convert.ToBoolean(hasTweenMethod.Invoke(null, new[] { cardGameObject }));
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsMouseOnlyChoiceSnapshot(ChoiceSnapshot snapshot, ChoiceExecutionMechanismKind mechanism)
        {
            return snapshot != null
                && mechanism == ChoiceExecutionMechanismKind.EntityChoice
                && string.Equals(snapshot.Mode, "DISCOVER", StringComparison.OrdinalIgnoreCase);
        }

        private static bool WaitForChoiceEntityReady(
            int entityId,
            ChoiceExecutionMechanismKind mechanism,
            out string detail)
        {
            switch (mechanism)
            {
                case ChoiceExecutionMechanismKind.SubOptionChoice:
                    return WaitForSubOptionChoiceReady(entityId, out detail);
                case ChoiceExecutionMechanismKind.EntityChoice:
                default:
                    return WaitForEntityChoiceReady(entityId, out detail);
            }
        }

        private static bool WaitForEntityChoiceReady(int entityId, out string detail)
        {
            detail = "entity_choice_wait_timeout";
            if (entityId <= 0)
            {
                detail = "entity_invalid";
                return false;
            }

            var deadline = Environment.TickCount + DiscoverChoiceReadyTimeoutMs;
            var lastDetail = "choice_state_unavailable";
            while (Environment.TickCount - deadline < 0)
            {
                var gs = GetGameState();
                if (gs == null)
                {
                    lastDetail = "game_state_null";
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                if (!TryBuildChoiceSnapshot(gs, out var snapshot))
                {
                    lastDetail = "choice_state_unavailable";
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                if (ResolveChoiceExecutionMechanism(snapshot) != ChoiceExecutionMechanismKind.EntityChoice)
                {
                    lastDetail = "mechanism_changed:" + ResolveChoiceExecutionMechanism(snapshot);
                    break;
                }

                if (!snapshot.ChoiceEntityIds.Contains(entityId))
                {
                    lastDetail = "entity_missing";
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                if (!snapshot.PacketReady && !snapshot.ChoiceStateActive && !snapshot.UiShown)
                {
                    lastDetail = "choice_state_inactive";
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                if (!IsChoiceSnapshotReady(snapshot, out lastDetail))
                {
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                if (!TryGetChoiceEntityScreenPos(entityId, out _, out _))
                {
                    lastDetail = "pos_not_found";
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                detail = "ready";
                return true;
            }

            detail = lastDetail;
            return false;
        }

        private static bool WaitForSubOptionChoiceReady(int entityId, out string detail)
        {
            detail = "suboption_choice_wait_timeout";
            if (entityId <= 0)
            {
                detail = "entity_invalid";
                return false;
            }

            var deadline = Environment.TickCount + DiscoverChoiceReadyTimeoutMs;
            var lastDetail = "choice_state_unavailable";
            while (Environment.TickCount - deadline < 0)
            {
                var gs = GetGameState();
                if (gs == null)
                {
                    lastDetail = "game_state_null";
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                if (!TryBuildChoiceSnapshot(gs, out var snapshot))
                {
                    lastDetail = "choice_state_unavailable";
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                if (ResolveChoiceExecutionMechanism(snapshot) != ChoiceExecutionMechanismKind.SubOptionChoice)
                {
                    lastDetail = "mechanism_changed:" + ResolveChoiceExecutionMechanism(snapshot);
                    break;
                }

                if (!snapshot.ChoiceEntityIds.Contains(entityId))
                {
                    lastDetail = "entity_missing";
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                if (!snapshot.UiShown && !snapshot.ChoiceStateActive && !IsSubOptionUiReady())
                {
                    lastDetail = "suboption_ui_inactive";
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                if (!IsChoiceSnapshotReady(snapshot, out lastDetail))
                {
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                if (!TryGetChoiceEntityScreenPos(entityId, out _, out _))
                {
                    lastDetail = "pos_not_found";
                    Thread.Sleep(DiscoverChoiceReadyPollMs);
                    continue;
                }

                detail = "ready";
                return true;
            }

            detail = lastDetail;
            return false;
        }

        private static string ResolveCardIdFromObject(object source)
        {
            if (source == null)
                return string.Empty;

            var cardIdObj = Invoke(source, "GetCardId")
                ?? GetFieldOrProp(source, "CardId")
                ?? GetFieldOrProp(source, "m_cardId")
                ?? GetFieldOrProp(source, "m_cardID");
            return cardIdObj?.ToString() ?? string.Empty;
        }

        private static bool HasSourceEntityTag(object sourceEntity, string tagName)
        {
            if (sourceEntity == null || string.IsNullOrWhiteSpace(tagName))
                return false;

            try
            {
                var ctx = ReflectionContext.Instance;
                if (!ctx.Init())
                    return false;

                return ctx.GetTagValue(sourceEntity, tagName) > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetCollectionCount(object collection, out int count)
        {
            count = 0;
            if (collection == null) return false;

            if (collection is Array array)
            {
                count = array.Length;
                return true;
            }

            if (collection is ICollection genericCollection)
            {
                count = genericCollection.Count;
                return true;
            }

            if (collection is IEnumerable enumerable)
            {
                var index = 0;
                foreach (var _ in enumerable)
                    index++;
                count = index;
                return true;
            }

            return false;
        }

        private static bool TryReadMulliganMarkedState(object mulliganMgr, int cardIndex, out bool marked, out string detail)
        {
            marked = false;
            detail = null;

            if (mulliganMgr == null)
            {
                detail = "manager_null";
                return false;
            }

            if (cardIndex < 0)
            {
                detail = "invalid_index:" + cardIndex;
                return false;
            }

            var raw = GetFieldOrProp(mulliganMgr, "m_handCardsMarkedForReplace");
            if (raw == null)
            {
                detail = "field_null";
                return false;
            }

            if (raw is bool[] boolArray)
            {
                if (cardIndex >= boolArray.Length)
                {
                    detail = "index_out_of_range:" + cardIndex + "/" + boolArray.Length;
                    return false;
                }

                marked = boolArray[cardIndex];
                return true;
            }

            if (raw is IList list)
            {
                if (cardIndex >= list.Count)
                {
                    detail = "index_out_of_range:" + cardIndex + "/" + list.Count;
                    return false;
                }

                try
                {
                    marked = Convert.ToBoolean(list[cardIndex]);
                    return true;
                }
                catch (Exception ex)
                {
                    detail = "convert_failed:" + SimplifyException(ex);
                    return false;
                }
            }

            if (raw is IEnumerable enumerable)
            {
                var index = 0;
                foreach (var item in enumerable)
                {
                    if (index == cardIndex)
                    {
                        try
                        {
                            marked = Convert.ToBoolean(item);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            detail = "convert_failed:" + SimplifyException(ex);
                            return false;
                        }
                    }

                    index++;
                }

                detail = "index_out_of_range:" + cardIndex + "/" + index;
                return false;
            }

            detail = "unsupported_type:" + raw.GetType().FullName;
            return false;
        }

        private static bool TryToggleMulliganCardViaManager(object mulliganMgr, object card, int cardIndex, out string detail)
        {
            detail = null;
            if (mulliganMgr == null || card == null)
            {
                detail = "invalid_args";
                return false;
            }

            var methods = mulliganMgr.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, "ToggleHoldState", StringComparison.OrdinalIgnoreCase))
                .Where(m => m.GetParameters().Length == 1)
                .ToArray();

            var cardType = card.GetType();
            var cardMethod = methods.FirstOrDefault(m => m.GetParameters()[0].ParameterType == cardType)
                ?? methods.FirstOrDefault(m =>
                {
                    var paramType = m.GetParameters()[0].ParameterType;
                    return string.Equals(paramType.Name, "Card", StringComparison.OrdinalIgnoreCase)
                        && paramType.IsInstanceOfType(card);
                })
                ?? methods.FirstOrDefault(m => m.GetParameters()[0].ParameterType.IsInstanceOfType(card));

            if (cardMethod != null)
            {
                try
                {
                    cardMethod.Invoke(mulliganMgr, new[] { card });
                    detail = "card_method:" + cardMethod.GetParameters()[0].ParameterType.Name;
                    return true;
                }
                catch (Exception ex)
                {
                    detail = "card_method_failed:" + SimplifyException(ex);
                }
            }

            if (TryInvokeMethod(mulliganMgr, "ToggleHoldState", new object[] { cardIndex, false, false }, out _, out var indexError))
            {
                detail = "index_method";
                return true;
            }

            detail = detail == null ? "index_method_failed:" + indexError : detail + ";index_method_failed:" + indexError;
            return false;
        }

        private static bool TryInvokeMulliganSender(
            object target,
            string[] methodNames,
            int[] replaceEntityIds,
            object[] entityChoices,
            out string detail)
        {
            detail = null;
            if (target == null || methodNames == null || methodNames.Length == 0)
                return false;

            var methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => methodNames.Any(name => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            foreach (var method in methods)
            {
                if (!TryBuildMulliganArgs(method.GetParameters(), replaceEntityIds, entityChoices, out var args))
                    continue;

                try
                {
                    var result = method.Invoke(target, args);
                    if (method.ReturnType == typeof(bool) && result is bool ok && !ok)
                        continue;

                    detail = target.GetType().Name + "." + method.Name;
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool TryBuildMulliganArgs(
            ParameterInfo[] parameters,
            int[] replaceEntityIds,
            object[] entityChoices,
            out object[] args)
        {
            args = null;
            if (parameters == null || parameters.Length == 0)
                return false;

            var built = new object[parameters.Length];
            var hasChoiceArg = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;

                if (TryBuildIntCollectionArg(paramType, replaceEntityIds, out var intArg))
                {
                    built[i] = intArg;
                    hasChoiceArg = true;
                    continue;
                }

                if (TryBuildEntityCollectionArg(paramType, entityChoices, out var entityArg))
                {
                    built[i] = entityArg;
                    hasChoiceArg = true;
                    continue;
                }

                if (paramType == typeof(int))
                {
                    built[i] = replaceEntityIds.Length;
                    continue;
                }

                if (paramType == typeof(bool))
                {
                    built[i] = false;
                    continue;
                }

                if (paramType == typeof(string))
                {
                    built[i] = string.Join(",", replaceEntityIds);
                    hasChoiceArg = true;
                    continue;
                }

                built[i] = paramType.IsValueType ? Activator.CreateInstance(paramType) : null;
            }

            if (!hasChoiceArg)
                return false;

            args = built;
            return true;
        }

        private static bool TryBuildIntCollectionArg(Type paramType, int[] values, out object arg)
        {
            arg = null;
            values = values ?? Array.Empty<int>();

            if (paramType == typeof(int[]))
            {
                arg = values;
                return true;
            }

            var intList = values.ToList();
            if (paramType.IsAssignableFrom(intList.GetType()))
            {
                arg = intList;
                return true;
            }

            if (paramType.IsGenericType && paramType.GetGenericArguments().Length == 1
                && paramType.GetGenericArguments()[0] == typeof(int))
            {
                try
                {
                    var collection = Activator.CreateInstance(paramType);
                    var add = paramType.GetMethod("Add", new[] { typeof(int) });
                    if (collection != null && add != null)
                    {
                        foreach (var value in values)
                            add.Invoke(collection, new object[] { value });
                        arg = collection;
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool TryBuildEntityCollectionArg(Type paramType, object[] entities, out object arg)
        {
            arg = null;
            entities = entities ?? new object[0];
            if (_entityType == null) return false;

            if (paramType == _entityType)
            {
                if (entities.Length == 0) return false;
                arg = entities[0];
                return true;
            }

            if (paramType.IsArray)
            {
                var elementType = paramType.GetElementType();
                if (elementType != null && elementType.IsAssignableFrom(_entityType))
                {
                    var arr = Array.CreateInstance(elementType, entities.Length);
                    for (int i = 0; i < entities.Length; i++)
                        arr.SetValue(entities[i], i);
                    arg = arr;
                    return true;
                }
            }

            if (paramType.IsGenericType && paramType.GetGenericArguments().Length == 1)
            {
                var genericType = paramType.GetGenericArguments()[0];
                if (!genericType.IsAssignableFrom(_entityType)) return false;

                var listType = typeof(List<>).MakeGenericType(genericType);
                var list = (IList)Activator.CreateInstance(listType);
                foreach (var entity in entities)
                    list.Add(entity);

                if (paramType.IsAssignableFrom(listType))
                {
                    arg = list;
                    return true;
                }

                try
                {
                    var collection = Activator.CreateInstance(paramType);
                    var add = paramType.GetMethod("Add", new[] { genericType });
                    if (collection != null && add != null)
                    {
                        foreach (var entity in entities)
                            add.Invoke(collection, new[] { entity });
                        arg = collection;
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool ToggleMulliganCard(int entityId)
        {
            try
            {
                if (SendOptionForEntity(entityId, 0, -1, null, false) == null)
                    return true;

                var gameState = GetGameState();
                if (gameState == null) return false;
                var entity = GetEntity(gameState, entityId);
                if (entity == null) return false;

                var inputMgr = GetSingleton(_inputMgrType);
                if (inputMgr == null) return false;

                var names = new[]
                {
                    "HandleClickOnCardInHand",
                    "DoNetworkPlay",
                    "HandleCardClicked",
                    "OnCardClicked"
                };

                foreach (var name in names)
                {
                    var methods = _inputMgrType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    foreach (var method in methods)
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 0) continue;

                        var args = BuildArgs(parameters, entity, 0, 0);
                        method.Invoke(inputMgr, args);
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryConfirmMulligan(out string detail, bool allowEndTurnFallback = true)
        {
            detail = null;

            var inputMgr = GetSingleton(_inputMgrType);
            if (inputMgr != null && TryInvokeParameterlessMethods(inputMgr, new[]
            {
                "DoMulliganButton",
                "HandleMulliganButton",
                "ConfirmMulligan",
                "DoAcceptMulligan"
            }, out detail))
            {
                detail = inputMgr.GetType().Name + "." + detail;
                return true;
            }

            var mulliganMgr = TryGetMulliganManager();
            if (mulliganMgr != null && TryInvokeParameterlessMethods(mulliganMgr, new[]
            {
                "ConfirmMulligan",
                "Confirm",
                "Submit",
                "Done",
                "Accept"
            }, out detail))
            {
                detail = mulliganMgr.GetType().Name + "." + detail;
                return true;
            }

            if (mulliganMgr != null
                && TryInvokeMethod(mulliganMgr, "GetMulliganButton", Array.Empty<object>(), out var mulliganButton, out _)
                && mulliganButton != null
                && TryTriggerUiElement(mulliganButton, out detail))
            {
                detail = "MulliganButton." + detail;
                return true;
            }

            if (!allowEndTurnFallback)
                return false;

            var endTurnButtonType = _asm.GetType("EndTurnButton");
            var endTurnButton = endTurnButtonType != null ? InvokeStatic(endTurnButtonType, "Get") : null;
            if (endTurnButton != null && TryInvokeParameterlessMethods(endTurnButton, new[]
            {
                "OnEndTurnRequested",
                "TriggerRelease"
            }, out detail))
            {
                detail = endTurnButton.GetType().Name + "." + detail;
                return true;
            }

            if (inputMgr != null && TryInvokeParameterlessMethods(inputMgr, new[]
            {
                "DoEndTurnButton"
            }, out detail))
            {
                detail = inputMgr.GetType().Name + "." + detail;
                return true;
            }

            try
            {
                EndTurn();
                detail = "InputManager.DoEndTurnButton";
                return true;
            }
            catch
            {
            }

            return false;
        }

        private static bool TryInvokeParameterlessMethods(object target, string[] methodNames, out string methodUsed)
        {
            methodUsed = null;
            if (target == null || methodNames == null || methodNames.Length == 0)
                return false;

            var methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.GetParameters().Length == 0)
                .Where(m => methodNames.Any(name => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            foreach (var method in methods)
            {
                try
                {
                    method.Invoke(target, null);
                    methodUsed = method.Name;
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static object TryGetMulliganManager()
        {
            if (_asm == null) return null;

            var type = _asm.GetType("MulliganManager");
            if (type == null) return null;

            var mgr = GetSingleton(type);
            if (mgr != null) return mgr;

            return InvokeStatic(type, "Get");
        }

        #region 发现/选择

        /// <summary>
        /// 检测当前是否处于发现/选择状态，返回选项信息
        /// 格式: originCardId|cardId1,entityId1;cardId2,entityId2;cardId3,entityId3|choiceMode
        /// </summary>
        public static string GetChoiceState()
        {
            if (!EnsureTypes()) return null;
            var gs = GetGameState();
            if (gs == null) return null;

            if (TryBuildChoiceSnapshot(gs, out var snapshot))
                return SerializeChoiceSnapshotJson(snapshot);

            return null;
        }

        public static string GetDiscoverState()
        {
            if (!EnsureTypes())
                return null;

            var gs = GetGameState();
            if (gs == null || !TryBuildChoiceSnapshot(gs, out var snapshot))
                return null;

            if (!string.Equals(snapshot.Mode, "DISCOVER", StringComparison.OrdinalIgnoreCase)
                || snapshot.ChoiceId <= 0
                || snapshot.ChoiceEntityIds == null
                || snapshot.ChoiceEntityIds.Count == 0)
            {
                return null;
            }

            var ready = IsDiscoverSnapshotReady(snapshot, out var detail);
            return string.Join(
                "|",
                snapshot.ChoiceId.ToString(),
                snapshot.SourceEntityId.ToString(),
                EscapeDiscoverStateValue(snapshot.SourceCardId),
                ready ? "READY" : "WAITING",
                string.Join(";", snapshot.ChoiceEntityIds.Select((entityId, index) =>
                    entityId + "," + EscapeDiscoverStateValue(index < snapshot.ChoiceCardIds.Count ? snapshot.ChoiceCardIds[index] : string.Empty))),
                EscapeDiscoverStateValue(detail));
        }

        /// <summary>
        /// 按屏幕比例坐标点击（用于 Rewind 等 UI 按钮）
        /// ratioX/ratioY 取值 0.0~1.0
        /// </summary>
        public static string ClickScreen(float ratioX, float ratioY)
        {
            if (_coroutine == null) return "ERROR:no_coroutine";
            return _coroutine.RunAndWait(MouseClickScreenCoroutine(ratioX, ratioY), ClickScreenTimeoutMs);
        }

        private static IEnumerator<float> MouseClickScreenCoroutine(float ratioX, float ratioY)
        {
            InputHook.Simulating = true;
            int w = MouseSimulator.GetScreenWidth();
            int h = MouseSimulator.GetScreenHeight();
            int x = (int)(w * ratioX);
            int y = (int)(h * ratioY);
            MouseSimulator.MoveTo(x, y);
            yield return 0.1f;
            MouseSimulator.LeftDown();
            yield return 0.05f;
            MouseSimulator.LeftUp();
            yield return 0.3f;
            _coroutine.SetResult("OK:CLICK_SCREEN:" + x + "," + y);
        }

        /// <summary>
        /// 通过鼠标点击执行发现选择
        /// </summary>
        public static string ApplyChoice(string snapshotId, int[] entityIds)
        {
            if (!EnsureTypes()) return "ERROR:not_initialized";
            if (_coroutine == null) return "ERROR:no_coroutine";

            var gs = GetGameState();
            if (gs == null) return "FAIL:no_game_state";
            if (!TryBuildChoiceSnapshot(gs, out var snapshot) || snapshot == null)
                return "FAIL:no_choice";

            if (!string.Equals(snapshot.SnapshotId, snapshotId ?? string.Empty, StringComparison.Ordinal))
                return "FAIL:snapshot_mismatch:" + snapshot.SnapshotId;

            var picks = NormalizeChoiceEntityIds(snapshot, entityIds, out var normalizeDetail);
            if (picks == null)
                return "FAIL:" + normalizeDetail;

            switch (ResolveChoiceExecutionMechanism(snapshot))
            {
                case ChoiceExecutionMechanismKind.SubOptionChoice:
                    return TryApplySubOptionChoice(snapshot, picks);
                case ChoiceExecutionMechanismKind.EntityChoice:
                default:
                    return TryApplyEntityChoice(snapshot, picks);
            }
        }

        private static string TryApplyEntityChoice(ChoiceSnapshot snapshot, List<int> picks)
        {
            if (ShouldUseMouseForChoice(snapshot, picks) && picks.Count == 1)
                return _coroutine.RunAndWait(MouseClickChoice(picks[0]));

            var sendResult = TrySendChoiceViaNetwork(picks.ToArray());
            if (string.IsNullOrWhiteSpace(sendResult) || !sendResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(sendResult) ? "FAIL:choice_sender_not_found" : sendResult;

            return ConfirmChoiceSubmission(snapshot, picks);
        }

        private static string TryApplySubOptionChoice(ChoiceSnapshot snapshot, List<int> picks)
        {
            if (picks == null || picks.Count == 0)
                return "FAIL:choice_entities_empty";

            if (picks.Count == 1)
                return _coroutine.RunAndWait(MouseClickChoice(picks[0]));

            var sendResult = TrySendChoiceViaNetwork(picks.ToArray());
            if (string.IsNullOrWhiteSpace(sendResult) || !sendResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(sendResult) ? "FAIL:choice_sender_not_found" : sendResult;

            return ConfirmChoiceSubmission(snapshot, picks);
        }

        private static List<int> NormalizeChoiceEntityIds(ChoiceSnapshot snapshot, int[] entityIds, out string detail)
        {
            detail = "choice_entities_invalid";
            if (snapshot == null)
            {
                detail = "choice_snapshot_null";
                return null;
            }

            var allowed = new HashSet<int>((snapshot.ChoiceEntityIds ?? new List<int>()).Where(id => id > 0));
            var picks = (entityIds ?? Array.Empty<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (picks.Any(id => allowed.Count > 0 && !allowed.Contains(id)))
            {
                detail = "entity_not_found";
                return null;
            }

            if (snapshot.CountMax > 0 && picks.Count > snapshot.CountMax)
            {
                detail = "count_exceeds_max";
                return null;
            }

            if (picks.Count < snapshot.CountMin)
            {
                detail = "count_below_min";
                return null;
            }

            if (picks.Count == 0 && snapshot.CountMin > 0)
            {
                detail = "choice_entities_empty";
                return null;
            }

            detail = "ok";
            return picks;
        }

        private static bool ShouldUseMouseForChoice(ChoiceSnapshot snapshot, List<int> entityIds)
        {
            if (snapshot == null || entityIds == null || entityIds.Count != 1)
                return false;

            if (snapshot.CountMax > 1
                || snapshot.IsRewindChoice
                || snapshot.IsMagicItemDiscover
                || snapshot.IsShopChoice
                || snapshot.IsLaunchpadAbility)
            {
                return false;
            }

            switch ((snapshot.Mode ?? string.Empty).ToUpperInvariant())
            {
                case "DISCOVER":
                case "DREDGE":
                case "ADAPT":
                case "GENERAL":
                case "CHOOSE_ONE":
                case "TARGET":
                case "OPTION_TARGET":
                    return true;
                default:
                    return false;
            }
        }

        private static string ConfirmChoiceSubmission(ChoiceSnapshot previousSnapshot, List<int> picks)
        {
            var previousSnapshotId = previousSnapshot?.SnapshotId ?? string.Empty;
            var previousSignature = previousSnapshot?.Signature ?? string.Empty;
            var picksJoined = picks == null || picks.Count == 0 ? "none" : string.Join(",", picks);

            for (var i = 0; i < 25; i++)
            {
                Thread.Sleep(80);
                var gs = GetGameState();
                if (gs == null || !TryBuildChoiceSnapshot(gs, out var current) || current == null)
                    return "OK:CLOSED:" + picksJoined;

                if (!string.Equals(current.SnapshotId, previousSnapshotId, StringComparison.Ordinal)
                    || !string.Equals(current.Signature, previousSignature, StringComparison.Ordinal))
                {
                    return "OK:CHANGED:" + current.SnapshotId;
                }
            }

            return "FAIL:choice_not_confirmed:" + picksJoined;
        }

        public static string ApplyChoice(int entityId)
        {
            if (!EnsureTypes()) return "ERROR:not_initialized";
            if (_coroutine == null) return "ERROR:no_coroutine";
            return _coroutine.RunAndWait(MouseClickChoice(entityId));
        }

        /// <summary>
        /// 通过网络 API 提交发现选择（用于 Rewind 等特殊选择按钮）
        /// </summary>
        public static string ApplyChoiceApi(int entityId)
        {
            if (!EnsureTypes()) return "ERROR:not_initialized";
            var ret = TrySendChoiceViaNetwork(entityId);
            return string.IsNullOrWhiteSpace(ret)
                ? "FAIL:CHOICE:network:" + entityId
                : ret;
        }

        private static string TrySendChoiceViaNetwork(int entityId)
        {
            return TrySendChoiceViaNetwork(new[] { entityId });
        }

        private static string TrySendChoiceViaNetwork(int[] entityIds)
        {
            var gs = GetGameState();
            if (gs == null) return null;
            var choices = TryGetFriendlyChoicePacket(gs);
            if (choices == null) return null;

            var choiceId = GetIntFieldOrProp(choices, "ID");
            if (choiceId <= 0) choiceId = GetIntFieldOrProp(choices, "Id");
            if (choiceId <= 0) return null;

            var network = GetSingleton(_networkType);
            if (network == null) return null;

            var allowedEntityIds = new HashSet<int>();
            AppendChoiceEntityIds(choices, allowedEntityIds);
            var picks = (entityIds ?? Array.Empty<int>())
                .Where(id => id > 0)
                .Where(id => allowedEntityIds.Count == 0 || allowedEntityIds.Contains(id))
                .Distinct()
                .ToArray();

            var methods = _networkType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, "SendChoices", StringComparison.OrdinalIgnoreCase) && m.GetParameters().Length == 2)
                .ToArray();

            foreach (var method in methods)
            {
                var pars = method.GetParameters();
                if (pars[0].ParameterType != typeof(int)) continue;
                if (!TryBuildIntCollectionArg(pars[1].ParameterType, picks, out var pickArg)) continue;
                try
                {
                    method.Invoke(network, new[] { (object)choiceId, pickArg });
                    return "OK:CHOICE:network:" + (picks.Length == 0 ? "none" : string.Join(",", picks));
                }
                catch { }
            }
            return null;
        }

        private static IEnumerator<float> MouseClickChoice(int entityId, bool allowApiFallback = true)
        {
            InputHook.Simulating = true;

            var hasCurrentChoiceSnapshot = TryGetCurrentChoiceSnapshotForEntity(entityId, out var currentChoiceSnapshot, out var choiceMechanism);
            var mouseOnlyChoice = hasCurrentChoiceSnapshot && IsMouseOnlyChoiceSnapshot(currentChoiceSnapshot, choiceMechanism);
            if (hasCurrentChoiceSnapshot)
            {
                AppendActionTrace("choice_click_prepare entityId=" + entityId + " mechanism=" + choiceMechanism + " mode=" + (currentChoiceSnapshot?.Mode ?? "UNKNOWN"));
                if (!WaitForChoiceEntityReady(entityId, choiceMechanism, out var choiceReadyDetail))
                {
                    AppendActionTrace("choice_wait_timeout entityId=" + entityId + " mechanism=" + choiceMechanism + " detail=" + choiceReadyDetail);
                    _coroutine.SetResult("FAIL:CHOICE:wait_timeout:" + entityId + ":" + choiceMechanism + ":" + choiceReadyDetail);
                    yield break;
                }

                AppendActionTrace("choice_wait_ready entityId=" + entityId + " mechanism=" + choiceMechanism + " detail=" + choiceReadyDetail);
            }

            // 记录点击前的选择快照，用于确认是否真的提交成功。
            CaptureChoiceSnapshot(out var beforeChoiceId, out var beforeSignature);

            bool confirmed = false;
            string confirmDetail = "timeout";

            // 鼠标点击做两次尝试：第一次常规点击，第二次作为“界面刚亮起时序抖动”补偿。
            for (int attempt = 1; attempt <= 2 && !confirmed; attempt++)
            {
                int x, y;
                var gotChoicePos = TryGetChoiceEntityScreenPos(entityId, out x, out y);
                if (!gotChoicePos)
                {
                    confirmDetail = "pos_not_found";
                    if (attempt < 2)
                    {
                        yield return 0.08f;
                        continue;
                    }
                    break;
                }

                foreach (var w in SmoothMove(x, y, 10, 0.012f)) yield return w;
                yield return 0.04f; // 悬停一帧，避免刚出现时第一击丢失
                MouseSimulator.LeftDown();
                yield return 0.08f;
                MouseSimulator.LeftUp();

                // 校验：选择界面应关闭，或切换到新的选择状态（连发现）。
                for (int i = 0; i < 14; i++)
                {
                    yield return 0.12f;
                    if (!CaptureChoiceSnapshot(out var afterChoiceId, out var afterSignature))
                    {
                        confirmed = true;
                        confirmDetail = "closed@mouse" + attempt;
                        break;
                    }

                    if (afterChoiceId != beforeChoiceId || !string.Equals(afterSignature, beforeSignature, StringComparison.Ordinal))
                    {
                        // 签名变化时二次确认：检查目标 entityId 是否真的被选中。
                        // 如果选择界面仍在且目标不在已选列表中，可能是手牌重排等无关变化导致的 false positive。
                        if (CaptureChoiceSnapshotChosen(entityId, out var entityChosen) && !entityChosen)
                        {
                            AppendActionTrace("choice_click_false_positive entityId=" + entityId + " attempt=" + attempt + " afterChoiceId=" + afterChoiceId);
                            beforeChoiceId = afterChoiceId;
                            beforeSignature = afterSignature;
                            confirmDetail = "false_positive@mouse" + attempt;
                            continue;
                        }

                        confirmed = true;
                        confirmDetail = "changed@mouse" + attempt;
                        break;
                    }

                    confirmDetail = "unchanged";
                }

                if (!confirmed && attempt < 2)
                    yield return 0.08f;
            }

            // 鼠标点击仍未确认时，回退网络 API 提交一次，提升抉择提交成功率。
            if (!confirmed && !mouseOnlyChoice && allowApiFallback)
            {
                var apiResult = TrySendChoiceViaNetwork(entityId);
                if (!string.IsNullOrWhiteSpace(apiResult)
                    && apiResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < 12; i++)
                    {
                        yield return 0.12f;
                        if (!CaptureChoiceSnapshot(out var afterChoiceId, out var afterSignature))
                        {
                            confirmed = true;
                            confirmDetail = "closed@api";
                            break;
                        }

                        if (afterChoiceId != beforeChoiceId || !string.Equals(afterSignature, beforeSignature, StringComparison.Ordinal))
                        {
                            confirmed = true;
                            confirmDetail = "changed@api";
                            break;
                        }

                        confirmDetail = "unchanged";
                    }

                    if (confirmed)
                    {
                        _coroutine.SetResult("OK:CHOICE:api:" + entityId + ":" + confirmDetail);
                        yield break;
                    }

                    _coroutine.SetResult("FAIL:CHOICE:not_confirmed:" + entityId + ":" + confirmDetail + ":api_sent");
                    yield break;
                }
            }

            if (!confirmed)
            {
                if (mouseOnlyChoice)
                    AppendActionTrace("choice_mouse_not_confirmed entityId=" + entityId + " mechanism=" + choiceMechanism + " detail=" + confirmDetail);
                _coroutine.SetResult("FAIL:CHOICE:not_confirmed:" + entityId + ":" + confirmDetail);
                yield break;
            }

            _coroutine.SetResult("OK:CHOICE:mouse:" + entityId + ":" + confirmDetail);
        }

        private static bool CaptureChoiceSnapshot(out int choiceId, out string signature)
        {
            choiceId = 0;
            signature = null;

            var gs = GetGameState();
            if (gs == null) return false;

            if (TryBuildChoiceSnapshot(gs, out var snapshot))
            {
                choiceId = snapshot.ChoiceId;
                signature = snapshot.Signature;
                return true;
            }

            return false;
        }

        private static bool CaptureChoiceSnapshotChosen(int entityId, out bool entityChosen)
        {
            entityChosen = false;

            var gs = GetGameState();
            if (gs == null) return false;

            if (!TryBuildChoiceSnapshot(gs, out var snapshot) || snapshot == null)
                return false;

            entityChosen = snapshot.ChosenEntityIds != null && snapshot.ChosenEntityIds.Contains(entityId);
            return true;
        }

        #endregion

        #region 操作等待

        /// <summary>
        /// 检查游戏是否准备好接受下一个操作（由 WAIT_READY 命令调用，不阻塞主线程）
        /// </summary>
        public static bool IsGameReady()
        {
            return EvaluateGameReadyState().IsReady;
        }

        public static string DescribeGameReady()
        {
            var state = EvaluateGameReadyState();
            return ReadyWaitDiagnostics.FormatResponse(
                state.IsReady,
                state.PrimaryReason,
                state.Flags,
                state.DrawEntityId,
                state.DrawCount);
        }

        private static ReadyWaitDiagnosticState EvaluateGameReadyState()
        {
            if (!EnsureTypes())
                return CreateBusyReadyState("types_unavailable");

            var gs = GetGameState();
            if (gs == null)
                return CreateBusyReadyState("game_state_null");

            var observedFriendlyDraw = TryGetFriendlyCardBeingDrawn(gs, out var currentFriendlyDraw, out var currentFriendlyDrawEntityId);
            if (observedFriendlyDraw && currentFriendlyDraw != null)
            {
                UpdateFriendlyDrawGate(currentFriendlyDraw, currentFriendlyDrawEntityId);
                return CreateBusyReadyState("friendly_draw", currentFriendlyDrawEntityId);
            }

            // 网络包阻塞检查
            if (TryInvokeBoolMethod(gs, "IsResponsePacketBlocked", out var blocked) && blocked)
                return CreateBusyReadyState("response_packet_blocked");

            // 输入权限检查
            var inputMgr = GetSingleton(_inputMgrType);
            if (inputMgr != null
                && TryInvokeBoolMethod(inputMgr, "PermitDecisionMakingInput", out var permit)
                && !permit)
                return CreateBusyReadyState("input_denied");

            // 战吼/动画处理中检查（PowerProcessor 正在运行时游戏状态尚未稳定）
            if (TryInvokeBoolMethod(gs, "IsBlockingPowerProcessor", out var bpp) && bpp)
                return CreateBusyReadyState("blocking_power_processor");

            var ppType = _asm?.GetType("PowerProcessor");
            if (ppType != null)
            {
                var pp = GetSingleton(ppType);
                if (pp != null && TryInvokeBoolMethod(pp, "IsRunning", out var ppRunning) && ppRunning)
                    return CreateBusyReadyState("power_processor_running");

                // 精准抽牌检测：检查任务列表中是否有未完成的抽牌任务
                if (pp != null)
                {
                    var pendingDrawTaskCount = CountPendingDrawTasks(pp);
                    if (pendingDrawTaskCount > 0)
                        return CreateBusyReadyState("pending_draw_task", drawCount: pendingDrawTaskCount);
                }
            }

            // 检查 TurnStartManager 是否有待处理抽牌
            var tsmType = _asm?.GetType("TurnStartManager");
            if (tsmType != null)
            {
                var tsm = GetSingleton(tsmType);
                if (tsm != null && TryInvokeBoolMethod(tsm, "IsListeningForTurnEvents", out var listening) && listening)
                {
                    if (TryInvokeMethod(tsm, "GetCardDrawCount", Array.Empty<object>(), out var countObj)
                        && countObj is int count && count > 0)
                        return CreateBusyReadyState("turn_start_draw_count", drawCount: count);
                }
            }

            // 手牌区域布局检查（抽牌动画、卡牌移动等）
            var handZoneBlockingReason = GetHandZoneBlockingReason(gs);
            if (!string.IsNullOrWhiteSpace(handZoneBlockingReason))
                return CreateBusyReadyState(handZoneBlockingReason);

            if (IsFriendlyDrawGateBlocking(gs, observedFriendlyDraw))
                return CreateBusyReadyState("friendly_draw", GetFriendlyDrawGateEntityId());

            if (TryInvokeBoolMethod(gs, "IsBusy", out var busy) && busy)
            {
                if (TryTreatFriendlyHeroEnchantmentBusyAsReady(gs, out var bypassDetail, out var heroEnchantmentBusyState))
                {
                    AppendActionTrace("WAIT_READY bypass game_busy due to hero_enchantment_busy " + bypassDetail);
                    return new ReadyWaitDiagnosticState
                    {
                        IsReady = true,
                        PrimaryReason = ReadyWaitDiagnostics.ReadyReason,
                        Flags = Array.Empty<string>()
                    };
                }

                if (heroEnchantmentBusyState != null)
                    return heroEnchantmentBusyState;

                return CreateBusyReadyState("game_busy");
            }

            ResetFriendlyHeroEnchantmentBusyObservation();
            return new ReadyWaitDiagnosticState
            {
                IsReady = true,
                PrimaryReason = ReadyWaitDiagnostics.ReadyReason,
                Flags = Array.Empty<string>()
            };
        }

        private static bool TryTreatFriendlyHeroEnchantmentBusyAsReady(
            object gameState,
            out string detail,
            out ReadyWaitDiagnosticState busyState)
        {
            detail = "unavailable";
            busyState = null;
            if (gameState == null)
            {
                ResetFriendlyHeroEnchantmentBusyObservation();
                detail = "game_state_null";
                return false;
            }

            if (IsChoiceModeActive(gameState))
            {
                ResetFriendlyHeroEnchantmentBusyObservation();
                detail = "choice_mode_active";
                return false;
            }

            if (IsSubOptionUiReady())
            {
                ResetFriendlyHeroEnchantmentBusyObservation();
                detail = "suboption_ui_ready";
                return false;
            }

            if (TryInvokeMethod(gameState, "GetResponseMode", Array.Empty<object>(), out var responseModeObj)
                && IsTargetSelectionResponseMode(responseModeObj))
            {
                ResetFriendlyHeroEnchantmentBusyObservation();
                detail = "target_selection_mode";
                return false;
            }

            GameStateData state = null;
            try
            {
                state = new GameReader().ReadGameState();
            }
            catch (Exception ex)
            {
                ResetFriendlyHeroEnchantmentBusyObservation();
                detail = "read_state_exception:" + SimplifyException(ex);
                return false;
            }

            if (state == null)
            {
                ResetFriendlyHeroEnchantmentBusyObservation();
                detail = "state_null";
                return false;
            }

            if (!state.IsOurTurn)
            {
                ResetFriendlyHeroEnchantmentBusyObservation();
                detail = "not_our_turn";
                return false;
            }

            if (!TryBuildFriendlyHeroEnchantmentBusySnapshot(gameState, out var snapshot) || snapshot == null)
            {
                ResetFriendlyHeroEnchantmentBusyObservation();
                detail = "snapshot_unavailable";
                return false;
            }

            if (snapshot.PlayerEnchantmentCount <= 0 && snapshot.HeroEnchantmentCount <= 0)
            {
                ResetFriendlyHeroEnchantmentBusyObservation();
                detail = snapshot.Detail ?? "no_enchantments";
                return false;
            }

            var stable = ObserveFriendlyHeroEnchantmentBusySignature(snapshot.Signature, out var stableMs, out var isNewSignature);
            detail =
                (snapshot.Detail ?? "hero_enchantment_busy")
                + " stableMs=" + stableMs
                + " thresholdMs=" + HeroEnchantmentBusyStableDelayMs;

            busyState = CreateBusyReadyState(
                "hero_enchantment_busy",
                BuildFriendlyHeroEnchantmentBusyFlags(snapshot));

            if (!stable)
            {
                if (isNewSignature)
                    AppendActionTrace("WAIT_READY hold game_busy due to hero_enchantment_busy " + detail);
                return false;
            }

            return true;
        }

        private static bool TryBuildFriendlyHeroEnchantmentBusySnapshot(object gameState, out HeroEnchantmentBusySnapshot snapshot)
        {
            snapshot = null;
            if (gameState == null)
                return false;

            try
            {
                var friendlyPlayer = Invoke(gameState, "GetFriendlySidePlayer")
                    ?? Invoke(gameState, "GetFriendlyPlayer")
                    ?? Invoke(gameState, "GetLocalPlayer");
                if (friendlyPlayer == null)
                    return false;

                var hero = Invoke(friendlyPlayer, "GetHero");
                var heroEntity = Invoke(hero, "GetEntity")
                    ?? GetFieldOrProp(hero, "Entity")
                    ?? GetFieldOrProp(hero, "m_entity")
                    ?? hero;

                ReadEnchantmentSnapshotParts(friendlyPlayer, preferDisplayed: false, out var playerSignatureParts, out var playerPreviewParts);
                ReadEnchantmentSnapshotParts(heroEntity, preferDisplayed: true, out var heroSignatureParts, out var heroPreviewParts);

                snapshot = new HeroEnchantmentBusySnapshot
                {
                    PlayerEnchantmentCount = playerSignatureParts.Count,
                    HeroEnchantmentCount = heroSignatureParts.Count
                };

                if (snapshot.PlayerEnchantmentCount <= 0 && snapshot.HeroEnchantmentCount <= 0)
                {
                    snapshot.Detail = "playerCount=0 heroCount=0";
                    return true;
                }

                var signatureParts = new List<string>();
                signatureParts.AddRange(playerSignatureParts.Select(part => "player:" + part));
                signatureParts.AddRange(heroSignatureParts.Select(part => "hero:" + part));
                signatureParts.Sort(StringComparer.Ordinal);

                snapshot.Signature = string.Join(";", signatureParts);
                snapshot.Detail =
                    "playerCount=" + snapshot.PlayerEnchantmentCount
                    + " heroCount=" + snapshot.HeroEnchantmentCount
                    + " player=[" + FormatEnchantmentPreview(playerPreviewParts) + "]"
                    + " hero=[" + FormatEnchantmentPreview(heroPreviewParts) + "]";
                return true;
            }
            catch (Exception ex)
            {
                snapshot = new HeroEnchantmentBusySnapshot
                {
                    Detail = "snapshot_exception:" + SimplifyException(ex)
                };
                return false;
            }
        }

        private static void ReadEnchantmentSnapshotParts(
            object source,
            bool preferDisplayed,
            out List<string> signatureParts,
            out List<string> previewParts)
        {
            signatureParts = new List<string>();
            previewParts = new List<string>();

            if (!TryEnumerateEnchantments(source, preferDisplayed, out var enchantments) || enchantments == null)
                return;

            ReflectionContext ctx = null;
            var hasCtx = false;
            try
            {
                ctx = ReflectionContext.Instance;
                hasCtx = ctx != null && ctx.Init();
            }
            catch
            {
                hasCtx = false;
            }

            foreach (var rawEnchantment in enchantments)
            {
                var enchantment = Invoke(rawEnchantment, "GetEntity")
                    ?? GetFieldOrProp(rawEnchantment, "Entity")
                    ?? GetFieldOrProp(rawEnchantment, "m_entity")
                    ?? rawEnchantment;
                if (enchantment == null)
                    continue;

                var cardId = ResolveCardIdFromObject(enchantment);
                if (string.IsNullOrWhiteSpace(cardId))
                {
                    var enchantCard = GetFieldOrProp(enchantment, "EnchantCard")
                        ?? GetFieldOrProp(enchantment, "m_enchantCard")
                        ?? Invoke(enchantment, "GetEnchantCard");
                    var enchantCardEntity = Invoke(enchantCard, "GetEntity")
                        ?? GetFieldOrProp(enchantCard, "Entity")
                        ?? GetFieldOrProp(enchantCard, "m_entity")
                        ?? enchantCard;
                    cardId = ResolveCardIdFromObject(enchantCardEntity);
                }

                if (string.IsNullOrWhiteSpace(cardId))
                    cardId = "unknown";

                var data1 = hasCtx ? SafeGetTagValue(ctx, enchantment, "TAG_SCRIPT_DATA_NUM_1") : 0;
                var data2 = hasCtx ? SafeGetTagValue(ctx, enchantment, "TAG_SCRIPT_DATA_NUM_2") : 0;
                var data3 = hasCtx ? SafeGetTagValue(ctx, enchantment, "TAG_SCRIPT_DATA_NUM_3") : 0;

                var signature = cardId;
                if (data1 != 0 || data2 != 0 || data3 != 0)
                    signature += "[" + data1 + "/" + data2 + "/" + data3 + "]";

                signatureParts.Add(signature);

                var entityId = ResolveEntityId(enchantment);
                previewParts.Add(entityId > 0 ? signature + "#" + entityId : signature);
            }

            signatureParts.Sort(StringComparer.Ordinal);
            previewParts.Sort(StringComparer.Ordinal);
        }

        private static bool TryEnumerateEnchantments(object source, bool preferDisplayed, out IEnumerable enchantments)
        {
            enchantments = null;
            if (source == null)
                return false;

            if (preferDisplayed
                && TryInvokeMethod(source, "GetDisplayedEnchantments", new object[] { false }, out var displayedEnchantments, out _)
                && displayedEnchantments is IEnumerable displayedEnumerable)
            {
                enchantments = displayedEnumerable;
                return true;
            }

            enchantments = Invoke(source, "GetEnchantments") as IEnumerable
                ?? Invoke(source, "GetAttachments") as IEnumerable
                ?? GetFieldOrProp(source, "m_attachments") as IEnumerable;
            return enchantments != null;
        }

        private static bool ObserveFriendlyHeroEnchantmentBusySignature(string signature, out int stableMs, out bool isNewSignature)
        {
            stableMs = 0;
            isNewSignature = false;
            if (string.IsNullOrWhiteSpace(signature))
            {
                ResetFriendlyHeroEnchantmentBusyObservation();
                return false;
            }

            var nowTick = Environment.TickCount;
            lock (_heroEnchantmentBusySync)
            {
                if (!string.Equals(_heroEnchantmentBusy.Signature, signature, StringComparison.Ordinal))
                {
                    _heroEnchantmentBusy.Signature = signature;
                    _heroEnchantmentBusy.FirstSeenTick = nowTick;
                    isNewSignature = true;
                    return false;
                }

                stableMs = Math.Max(0, unchecked(nowTick - _heroEnchantmentBusy.FirstSeenTick));
                return stableMs >= HeroEnchantmentBusyStableDelayMs;
            }
        }

        private static void ResetFriendlyHeroEnchantmentBusyObservation()
        {
            lock (_heroEnchantmentBusySync)
            {
                _heroEnchantmentBusy.Signature = string.Empty;
                _heroEnchantmentBusy.FirstSeenTick = 0;
            }
        }

        private static IEnumerable<string> BuildFriendlyHeroEnchantmentBusyFlags(HeroEnchantmentBusySnapshot snapshot)
        {
            var flags = new List<string> { "hero_enchantment_busy" };
            if (snapshot != null)
            {
                if (snapshot.PlayerEnchantmentCount > 0)
                    flags.Add("player_enchantments");
                if (snapshot.HeroEnchantmentCount > 0)
                    flags.Add("hero_enchantments");
            }

            return flags;
        }

        private static string FormatEnchantmentPreview(IEnumerable<string> parts)
        {
            if (parts == null)
                return "-";

            var materialized = parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Take(5)
                .ToList();
            if (materialized.Count == 0)
                return "-";

            var joined = string.Join(",", materialized.Take(4));
            return materialized.Count > 4 ? joined + ",..." : joined;
        }

        private static int SafeGetTagValue(ReflectionContext ctx, object source, string tagName)
        {
            if (ctx == null || source == null || string.IsNullOrWhiteSpace(tagName))
                return 0;

            try
            {
                return ctx.GetTagValue(source, tagName);
            }
            catch
            {
                return 0;
            }
        }

        private static ReadyWaitDiagnosticState CreateBusyReadyState(
            string primaryReason,
            int drawEntityId = 0,
            int drawCount = 0)
        {
            return CreateBusyReadyState(primaryReason, null, drawEntityId, drawCount);
        }

        private static ReadyWaitDiagnosticState CreateBusyReadyState(
            string primaryReason,
            IEnumerable<string> flags,
            int drawEntityId = 0,
            int drawCount = 0)
        {
            var normalizedPrimaryReason = string.IsNullOrWhiteSpace(primaryReason)
                ? ReadyWaitDiagnostics.UnknownBusyReason
                : primaryReason.Trim();
            var normalizedFlags = flags?
                .Where(flag => !string.IsNullOrWhiteSpace(flag))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (normalizedFlags == null || normalizedFlags.Length == 0)
                normalizedFlags = new[] { normalizedPrimaryReason };

            return new ReadyWaitDiagnosticState
            {
                IsReady = false,
                PrimaryReason = normalizedPrimaryReason,
                Flags = normalizedFlags,
                DrawEntityId = drawEntityId > 0 ? drawEntityId : 0,
                DrawCount = drawCount > 0 ? drawCount : 0
            };
        }

        private static int CountPendingDrawTasks(object powerProcessor)
        {
            if (powerProcessor == null)
                return 0;

            var pendingDrawTaskCount = 0;
            var taskList = Invoke(powerProcessor, "GetCurrentTaskList");
            if (taskList == null)
                return 0;

            var tasks = Invoke(taskList, "GetTaskList") as IEnumerable;
            if (tasks == null)
                return 0;

            foreach (var task in tasks)
            {
                if (task == null)
                    continue;

                if (!TryInvokeBoolMethod(task, "IsCardDraw", out var isCardDraw) || !isCardDraw)
                    continue;

                if (TryInvokeBoolMethod(task, "IsCompleted", out var isCompleted) && !isCompleted)
                    pendingDrawTaskCount++;
            }

            return pendingDrawTaskCount;
        }

        private static int GetFriendlyDrawGateEntityId()
        {
            lock (_friendlyDrawGateSync)
            {
                TrimExpiredRecentDrawGuard_NoLock();
                if (_friendlyDrawGate.Active && _friendlyDrawGate.EntityId > 0)
                    return _friendlyDrawGate.EntityId;

                if (_friendlyDrawGate.RecentReleasedEntityId > 0)
                    return _friendlyDrawGate.RecentReleasedEntityId;
            }

            return 0;
        }

        /// <summary>
        /// 检查友方手牌区域是否准备好（布局完成，无卡牌移动）
        /// </summary>
        private static bool TryGetFriendlyCardBeingDrawn(object gameState, out object card, out int entityId)
        {
            card = null;
            entityId = 0;
            if (gameState == null)
                return false;

            if (!TryInvokeMethod(gameState, "GetFriendlyCardBeingDrawn", Array.Empty<object>(), out var currentDrawCard))
                return false;

            card = currentDrawCard;
            entityId = ResolveCardEntityId(card);
            return true;
        }

        private static bool IsFriendlyDrawGateBlocking(object gameState, bool drawObservationSucceeded)
        {
            object cachedCard;
            int cachedEntityId;
            lock (_friendlyDrawGateSync)
            {
                TrimExpiredRecentDrawGuard_NoLock();
                if (!_friendlyDrawGate.Active)
                    return false;

                cachedCard = _friendlyDrawGate.Card;
                cachedEntityId = _friendlyDrawGate.EntityId;
            }

            var actorReadyKnown = TryReadCardActorReady(cachedCard, out var actorReady);
            if (actorReadyKnown && !actorReady)
            {
                ResetFriendlyDrawFallbackTimer();
                return true;
            }

            var interactiveKnown = TryReadCardStandInIsInteractive(cachedCard, out var interactive);
            if (actorReadyKnown && interactiveKnown && actorReady && interactive)
            {
                ReleaseFriendlyDrawGate(cachedEntityId, cachedCard, viaFallback: false);
                return false;
            }

            if (interactiveKnown && !interactive)
            {
                if (cachedEntityId > 0
                    && TryIsEntityInFriendlyHand(gameState, cachedEntityId, out var stillInHand))
                {
                    if (stillInHand)
                    {
                        ResetFriendlyDrawFallbackTimer();
                        return true;
                    }

                    return WaitForFriendlyDrawFallback(gameState, cachedEntityId, cachedCard);
                }

                return true;
            }

            if (!drawObservationSucceeded)
                return true;

            return WaitForFriendlyDrawFallback(gameState, cachedEntityId, cachedCard);
        }

        private static bool WaitForFriendlyDrawFallback(object gameState, int entityId, object card)
        {
            var shouldLog = false;
            int releaseTick;
            lock (_friendlyDrawGateSync)
            {
                if (!_friendlyDrawGate.Active)
                    return false;

                if (_friendlyDrawGate.FallbackReleaseTick == 0)
                {
                    _friendlyDrawGate.FallbackReleaseTick = Environment.TickCount + FriendlyDrawFallbackDelayMs;
                    shouldLog = true;
                }

                releaseTick = _friendlyDrawGate.FallbackReleaseTick;
            }

            if (shouldLog)
            {
                AppendActionTrace(
                    "DrawGate fallback armed entity="
                    + entityId
                    + " delayMs="
                    + FriendlyDrawFallbackDelayMs);
            }

            if (Environment.TickCount - releaseTick < 0)
                return true;

            if (!IsHandZoneReady(gameState))
                return true;

            ReleaseFriendlyDrawGate(entityId, card, viaFallback: true);
            return false;
        }

        private static void ResetFriendlyDrawFallbackTimer()
        {
            lock (_friendlyDrawGateSync)
            {
                if (_friendlyDrawGate.Active)
                    _friendlyDrawGate.FallbackReleaseTick = 0;
            }
        }

        private static void UpdateFriendlyDrawGate(object card, int entityId)
        {
            var shouldLog = false;
            lock (_friendlyDrawGateSync)
            {
                TrimExpiredRecentDrawGuard_NoLock();
                shouldLog = !_friendlyDrawGate.Active || _friendlyDrawGate.EntityId != entityId || !ReferenceEquals(_friendlyDrawGate.Card, card);
                _friendlyDrawGate.Active = true;
                _friendlyDrawGate.Card = card;
                _friendlyDrawGate.EntityId = entityId;
                _friendlyDrawGate.FallbackReleaseTick = 0;
                _friendlyDrawGate.RecentReleasedEntityId = 0;
                _friendlyDrawGate.RecentReleasedCard = null;
                _friendlyDrawGate.RecentReleasedUntilTick = 0;
            }

            if (shouldLog)
                AppendActionTrace("DrawGate engaged entity=" + entityId);
        }

        private static void ReleaseFriendlyDrawGate(int entityId, object card, bool viaFallback)
        {
            int releasedEntityId;
            lock (_friendlyDrawGateSync)
            {
                if (!_friendlyDrawGate.Active)
                    return;

                releasedEntityId = entityId > 0 ? entityId : _friendlyDrawGate.EntityId;
                _friendlyDrawGate.Active = false;
                _friendlyDrawGate.Card = null;
                _friendlyDrawGate.EntityId = 0;
                _friendlyDrawGate.FallbackReleaseTick = 0;
                _friendlyDrawGate.RecentReleasedEntityId = releasedEntityId;
                _friendlyDrawGate.RecentReleasedCard = card;
                _friendlyDrawGate.RecentReleasedUntilTick = Environment.TickCount + FriendlyDrawRecentGuardWindowMs;
            }

            AppendActionTrace(
                "DrawGate released entity="
                + releasedEntityId
                + (viaFallback ? " via=fallback" : " via=interactive"));
        }

        private static bool ShouldUseRecentDrawPlayGuard(int entityId)
        {
            if (entityId <= 0)
                return false;

            lock (_friendlyDrawGateSync)
            {
                TrimExpiredRecentDrawGuard_NoLock();
                if (_friendlyDrawGate.Active && _friendlyDrawGate.EntityId == entityId)
                    return true;
                return _friendlyDrawGate.RecentReleasedEntityId == entityId;
            }
        }

        private static bool TryReadRecentDrawCardInteractive(int entityId, object gameState, out bool interactive)
        {
            interactive = true;
            if (entityId <= 0)
                return false;

            object card = null;
            lock (_friendlyDrawGateSync)
            {
                TrimExpiredRecentDrawGuard_NoLock();
                if (_friendlyDrawGate.Active && _friendlyDrawGate.EntityId == entityId)
                    card = _friendlyDrawGate.Card;
                else if (_friendlyDrawGate.RecentReleasedEntityId == entityId)
                    card = _friendlyDrawGate.RecentReleasedCard;
            }

            if (TryReadCardStandInIsInteractive(card, out interactive))
                return true;

            var resolvedCard = ResolveFriendlyHandCardObject(gameState ?? GetGameState(), entityId);
            return TryReadCardStandInIsInteractive(resolvedCard, out interactive);
        }

        private static bool TryReadCardActorReady(object card, out bool actorReady)
        {
            actorReady = false;
            return card != null && TryInvokeBoolMethod(card, "IsActorReady", out actorReady);
        }

        private static bool TryReadCardStandInIsInteractive(object card, out bool interactive)
        {
            interactive = false;
            return card != null && TryInvokeBoolMethod(card, "CardStandInIsInteractive", out interactive);
        }

        private static int ResolveCardEntityId(object card)
        {
            if (card == null)
                return 0;

            var entity = Invoke(card, "GetEntity")
                ?? GetFieldOrProp(card, "Entity")
                ?? GetFieldOrProp(card, "m_entity")
                ?? card;
            return ResolveEntityId(entity);
        }

        private static void TrimExpiredRecentDrawGuard_NoLock()
        {
            if (_friendlyDrawGate.RecentReleasedUntilTick == 0)
                return;

            if (Environment.TickCount - _friendlyDrawGate.RecentReleasedUntilTick < 0)
                return;

            _friendlyDrawGate.RecentReleasedEntityId = 0;
            _friendlyDrawGate.RecentReleasedCard = null;
            _friendlyDrawGate.RecentReleasedUntilTick = 0;
        }

        private static bool IsHandZoneReady(object gameState)
        {
            return string.IsNullOrWhiteSpace(GetHandZoneBlockingReason(gameState));
        }

        private static string GetHandZoneBlockingReason(object gameState)
        {
            try
            {
                var friendlyPlayer = Invoke(gameState, "GetFriendlySidePlayer");
                if (friendlyPlayer == null) return string.Empty;

                var handZone = Invoke(friendlyPlayer, "GetHandZone");
                if (handZone == null) return string.Empty;

                // 检查手牌区域是否正在更新布局（抽牌动画、卡牌移动等）
                if (TryInvokeBoolMethod(handZone, "IsUpdatingLayout", out var updating) && updating)
                    return "hand_layout_updating";

                // 检查布局是否脏（需要更新但还未开始）
                if (TryInvokeBoolMethod(handZone, "IsLayoutDirty", out var dirty) && dirty)
                    return "hand_layout_dirty";

                return string.Empty;
            }
            catch
            {
                return string.Empty; // 出错时不阻塞
            }
        }

        #endregion

        #region Network.Options 核心方法

        /// <summary>
        /// 通过 Network.Options 找到匹配实体的操作并发送
        /// 这是最可靠的方式，直接走炉石的网络协议层
        /// </summary>
        private static string SendOptionForEntity(int entityId, int targetEntityId, int position, bool requireSourceLeaveHand)
        {
            return SendOptionForEntity(entityId, targetEntityId, position, null, requireSourceLeaveHand);
        }

        private static string SendOptionForEntity(int entityId, int targetEntityId, int position, string desiredSubOptionCardId, bool requireSourceLeaveHand)
        {
            try
            {
                var optionsList = FindOptionsList(out var optDiag);
                if (optionsList == null) return optDiag;

                var optionsEnumerable = optionsList as IEnumerable;
                if (optionsEnumerable == null) return "options_not_enumerable:" + optionsList.GetType().Name;

                var optionCount = 0;
                var mainIds = new List<int>();
                foreach (var option in optionsEnumerable)
                {
                    optionCount++;
                    var main = GetFieldOrProp(option, "Main") ?? Invoke(option, "GetMain");
                    if (main == null) continue;

                    var mainId = ResolveOptionEntityId(main);
                    if (mainId <= 0)
                        mainId = ResolveOptionEntityId(option);
                    mainIds.Add(mainId);

                    if (mainId != entityId) continue;

                    var optionIndexByOrder = optionCount - 1;
                    var optionIndexByField = GetIntFieldOrProp(option, "Index");
                    if (optionIndexByField <= 0)
                        optionIndexByField = GetIntFieldOrProp(option, "OptionIndex");

                    var subOptionIndex = ResolveSubOptionIndex(option, main, desiredSubOptionCardId);
                    if (!string.IsNullOrWhiteSpace(desiredSubOptionCardId) && subOptionIndex < 0)
                        continue;
                    var resolvedTargetEntityId = targetEntityId;
                    var targetIndex = -1;
                    var hasTargets = false;
                    var targetEntityIds = new List<int>();
                    var targets = GetFieldOrProp(main, "Targets") as IEnumerable
                        ?? Invoke(main, "GetTargets") as IEnumerable
                        ?? GetFieldOrProp(option, "Targets") as IEnumerable
                        ?? Invoke(option, "GetTargets") as IEnumerable;

                    if (targets != null)
                    {
                        hasTargets = true;
                        var tIdx = 0;
                        foreach (var t in targets)
                        {
                            var tId = ResolveOptionEntityId(t);
                            if (tId > 0)
                                targetEntityIds.Add(tId);

                            if (tId == resolvedTargetEntityId)
                                targetIndex = tIdx;

                            tIdx++;
                        }
                    }

                    if (hasTargets && resolvedTargetEntityId <= 0)
                    {
                        resolvedTargetEntityId = PickPreferredOptionTarget(targetEntityIds);
                        if (resolvedTargetEntityId > 0)
                            targetIndex = targetEntityIds.IndexOf(resolvedTargetEntityId);
                    }

                    if (hasTargets && targetIndex < 0)
                        continue;

                    return TrySendOption(
                        optionIndexByOrder,
                        optionIndexByField,
                        subOptionIndex,
                        targetIndex,
                        resolvedTargetEntityId,
                        position,
                        option,
                        entityId,
                        requireSourceLeaveHand);
                }

                return "no_match:count=" + optionCount + ";ids=" + string.Join(",", mainIds);
            }
            catch (Exception ex) { return "ex:" + SimplifyException(ex); }
        }

        private static int PickPreferredOptionTarget(IReadOnlyList<int> candidateEntityIds)
        {
            if (candidateEntityIds == null || candidateEntityIds.Count == 0)
                return 0;

            var enemyHero = candidateEntityIds.FirstOrDefault(IsEnemyHeroEntityId);
            if (enemyHero > 0)
                return enemyHero;

            var bestEntityId = 0;
            var bestScore = int.MinValue;
            foreach (var candidateEntityId in candidateEntityIds)
            {
                if (candidateEntityId <= 0 || IsFriendlyHeroEntityId(candidateEntityId) || IsEnemyHeroEntityId(candidateEntityId))
                    continue;

                var score = ReadOptionTargetPriority(candidateEntityId);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEntityId = candidateEntityId;
                }
            }

            if (bestEntityId > 0)
                return bestEntityId;

            var friendlyHero = candidateEntityIds.FirstOrDefault(IsFriendlyHeroEntityId);
            if (friendlyHero > 0)
                return friendlyHero;

            return candidateEntityIds[0];
        }

        private static int ReadOptionTargetPriority(int entityId)
        {
            if (entityId <= 0)
                return int.MinValue;

            try
            {
                var gs = GetGameState();
                var entity = GetEntity(gs, entityId);
                if (entity == null)
                    return int.MinValue;

                var ctx = ReflectionContext.Instance;
                if (!ctx.Init())
                    return int.MinValue;

                var atk = ctx.GetTagValue(entity, "ATK");
                var health = ctx.GetTagValue(entity, "HEALTH");
                return atk * 100 + health;
            }
            catch
            {
                return int.MinValue;
            }
        }


        private static string TrySendOption(
            int optionIndexByOrder,
            int optionIndexByField,
            int subOptionIndex,
            int targetIndex,
            int targetEntityId,
            int position,
            object option,
            int sourceEntityId,
            bool requireSourceLeaveHand)
        {
            var gs = GetGameState();
            if (gs == null) return "no_gamestate";

            var vals = "optOrder=" + optionIndexByOrder
                + ",optField=" + optionIndexByField
                + ",sub=" + subOptionIndex
                + ",tgtIndex=" + targetIndex
                + ",tgtEntity=" + targetEntityId;

            var optionCandidates = new List<int>();
            AddCandidate(optionCandidates, optionIndexByOrder);
            AddCandidate(optionCandidates, optionIndexByField);
            AddCandidate(optionCandidates, optionIndexByOrder + 1);
            AddCandidate(optionCandidates, optionIndexByOrder - 1);
            AddCandidate(optionCandidates, optionIndexByField + 1);
            AddCandidate(optionCandidates, optionIndexByField - 1);
            if (optionCandidates.Count == 0)
                optionCandidates.Add(0);

            var targetCandidates = new List<int>();
            if (targetIndex >= 0) targetCandidates.Add(targetIndex);
            if (targetCandidates.Count == 0)
                targetCandidates.Add(-1);

            var submitNames = new[]
            {
                "SendOption",
                "SendSelectedOption",
                "SubmitOption",
                "DoSendOption",
                "OnSelectedOptionsSent"
            };

            var submitTargets = new[] { gs, GetSingleton(_networkType), GetSingleton(_connectApiType) };
            string lastErr = null;

            foreach (var opt in optionCandidates)
            {
                foreach (var tgt in targetCandidates)
                {
                    if (!TrySetSelectedOption(gs, opt, subOptionIndex, tgt, position, out var setErr))
                    {
                        lastErr = "set_failed:" + setErr;
                        continue;
                    }

                    foreach (var submitTarget in submitTargets)
                    {
                        if (submitTarget == null) continue;

                        if (TryInvokeSubmitMethods(
                                submitTarget,
                                submitNames,
                                option,
                                opt,
                                subOptionIndex,
                                tgt,
                                position,
                                sourceEntityId,
                                requireSourceLeaveHand,
                                out _,
                                out var submitErr))
                        {
                            return null;
                        }

                        if (!string.IsNullOrWhiteSpace(submitErr))
                            lastErr = submitTarget.GetType().Name + ":" + submitErr;
                    }
                }
            }

            return "no_submit:" + vals + ":" + (lastErr ?? "no_method_found");
        }

        private static void AddCandidate(List<int> values, int value)
        {
            if (value < 0 || values == null || values.Contains(value))
                return;

            values.Add(value);
        }

        private static int ResolveOptionEntityId(object source)
        {
            if (source == null) return 0;

            var id = ResolveEntityId(source);
            if (id > 0) return id;

            id = GetIntFieldOrProp(source, "EntityID");
            if (id <= 0) id = GetIntFieldOrProp(source, "EntityId");
            if (id <= 0) id = GetIntFieldOrProp(source, "ID");
            if (id <= 0) id = GetIntFieldOrProp(source, "Id");
            if (id > 0) return id;

            var nestedEntity = GetFieldOrProp(source, "Entity")
                ?? GetFieldOrProp(source, "m_entity")
                ?? GetFieldOrProp(source, "m_entityDef")
                ?? GetFieldOrProp(source, "GameEntity")
                ?? Invoke(source, "GetEntity");

            if (nestedEntity != null && !ReferenceEquals(nestedEntity, source))
            {
                id = ResolveEntityId(nestedEntity);
                if (id > 0) return id;
            }

            return 0;
        }

        private static int ResolveSubOptionIndex(object option, object main, string desiredSubOptionCardId)
        {
            if (string.IsNullOrWhiteSpace(desiredSubOptionCardId))
                return -1;

            if (int.TryParse(desiredSubOptionCardId, out var directIndex) && directIndex >= 0)
                return directIndex;

            var candidates = new[]
            {
                GetFieldOrProp(option, "SubOptions"),
                GetFieldOrProp(option, "m_subOptions"),
                GetFieldOrProp(option, "SubOptionInfos"),
                GetFieldOrProp(option, "m_subOptionInfos"),
                GetFieldOrProp(main, "SubOptions"),
                GetFieldOrProp(main, "m_subOptions"),
                GetFieldOrProp(main, "SubOptionInfos"),
                GetFieldOrProp(main, "m_subOptionInfos"),
                Invoke(option, "GetSubOptions"),
                Invoke(main, "GetSubOptions")
            };

            foreach (var candidate in candidates)
            {
                if (!(candidate is IEnumerable items))
                    continue;

                var index = 0;
                foreach (var item in items)
                {
                    if (MatchesSubOptionCardId(item, desiredSubOptionCardId))
                        return index;
                    index++;
                }
            }

            return -1;
        }

        private static bool MatchesSubOptionCardId(object optionLike, string desiredSubOptionCardId)
        {
            if (optionLike == null || string.IsNullOrWhiteSpace(desiredSubOptionCardId))
                return false;

            var direct = GetFieldOrProp(optionLike, "CardId")
                ?? GetFieldOrProp(optionLike, "m_cardId")
                ?? GetFieldOrProp(optionLike, "CardID")
                ?? GetFieldOrProp(optionLike, "AssetId")
                ?? GetFieldOrProp(optionLike, "m_assetId");
            if (direct != null && string.Equals(direct.ToString(), desiredSubOptionCardId, StringComparison.OrdinalIgnoreCase))
                return true;

            var nestedMain = GetFieldOrProp(optionLike, "Main")
                ?? GetFieldOrProp(optionLike, "m_main")
                ?? Invoke(optionLike, "GetMain");
            if (nestedMain != null && !ReferenceEquals(nestedMain, optionLike))
            {
                var nestedDirect = GetFieldOrProp(nestedMain, "CardId")
                    ?? GetFieldOrProp(nestedMain, "m_cardId")
                    ?? GetFieldOrProp(nestedMain, "CardID")
                    ?? GetFieldOrProp(nestedMain, "AssetId")
                    ?? GetFieldOrProp(nestedMain, "m_assetId");
                if (nestedDirect != null && string.Equals(nestedDirect.ToString(), desiredSubOptionCardId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            var optionEntityId = ResolveOptionEntityId(optionLike);
            if (optionEntityId > 0)
            {
                var gameState = GetGameState();
                var resolvedCardId = ResolveEntityCardId(gameState, optionEntityId);
                if (!string.IsNullOrWhiteSpace(resolvedCardId)
                    && string.Equals(resolvedCardId, desiredSubOptionCardId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TrySetSelectedOption(object gameState, int optionIndex, int subOptionIndex, int targetIndex, int position, out string error)
        {
            error = null;
            if (gameState == null)
            {
                error = "no_gamestate";
                return false;
            }

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var gsType = gameState.GetType();

            try
            {
                var setOpt = gsType.GetMethod("SetSelectedOption", flags, null, new[] { typeof(int) }, null);
                if (setOpt == null)
                {
                    error = "no_SetSelectedOption";
                    return false;
                }

                var setSub = gsType.GetMethod("SetSelectedSubOption", flags, null, new[] { typeof(int) }, null);
                var setTgt = gsType.GetMethod("SetSelectedOptionTarget", flags, null, new[] { typeof(int) }, null);
                var setPos = gsType.GetMethod("SetSelectedOptionPosition", flags, null, new[] { typeof(int) }, null);

                setOpt.Invoke(gameState, new object[] { optionIndex });
                setSub?.Invoke(gameState, new object[] { subOptionIndex });
                setTgt?.Invoke(gameState, new object[] { targetIndex });
                setPos?.Invoke(gameState, new object[] { position > 0 ? position : 0 });
                return true;
            }
            catch (Exception ex)
            {
                error = SimplifyException(ex);
                return false;
            }
        }

        private static bool TryInvokeSubmitMethods(
            object target,
            string[] methodNames,
            object option,
            int optionIndex,
            int subOptionIndex,
            int targetIndex,
            int position,
            int sourceEntityId,
            bool requireSourceLeaveHand,
            out string methodUsed,
            out string error)
        {
            methodUsed = null;
            error = null;

            if (target == null || methodNames == null || methodNames.Length == 0)
            {
                error = "invalid_submit_target";
                return false;
            }

            var gameState = GetGameState();
            var selectedBefore = GetSelectedOptionValue(gameState);

            var methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => methodNames.Any(name => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(m => m.GetParameters().Length)
                .ToArray();

            if (methods.Length == 0)
            {
                error = "submit_method_not_found";
                return false;
            }

            string lastError = null;
            foreach (var method in methods)
            {
                object result;
                var parameters = method.GetParameters();
                if (!TryBuildSubmitArgs(
                        parameters,
                        option,
                        optionIndex,
                        subOptionIndex,
                        targetIndex,
                        position,
                        out var args))
                {
                    continue;
                }

                try
                {
                    result = method.Invoke(target, args);
                    if (method.ReturnType == typeof(bool) && result is bool ok && !ok)
                    {
                        lastError = method.Name + ":returned_false";
                        continue;
                    }

                    if (!DidSubmissionStart(
                            gameState,
                            selectedBefore,
                            sourceEntityId,
                            requireSourceLeaveHand,
                            !requireSourceLeaveHand))
                    {
                        lastError = method.Name + ":no_state_change";
                        continue;
                    }

                    methodUsed = method.Name;
                    return true;
                }
                catch (Exception ex)
                {
                    lastError = method.Name + ":" + SimplifyException(ex);
                }
            }

            error = lastError ?? "no_compatible_submit_overload";
            return false;
        }

        private static bool DidSubmissionStart(object gameState, int? selectedBefore, bool assumeSuccessWhenNoSignal = true)
        {
            return DidSubmissionStart(gameState, selectedBefore, 0, false, assumeSuccessWhenNoSignal);
        }

        private static bool DidSubmissionStart(
            object gameState,
            int? selectedBefore,
            int sourceEntityId,
            bool requireSourceLeaveHand,
            bool assumeSuccessWhenNoSignal)
        {
            if (gameState == null)
                return true;

            if (requireSourceLeaveHand && sourceEntityId > 0)
            {
                if (TryIsEntityInFriendlyHand(gameState, sourceEntityId, out var stillInHand))
                    return !stillInHand;
                return false;
            }

            var hasSignal = false;

            if (TryInvokeBoolMethod(gameState, "IsResponsePacketBlocked", out var blocked))
            {
                hasSignal = true;
                if (blocked)
                    return true;
            }

            var selectedAfter = GetSelectedOptionValue(gameState);
            if (selectedAfter.HasValue)
            {
                hasSignal = true;
                if (selectedAfter.Value < 0)
                    return true;
            }

            if (selectedBefore.HasValue && selectedAfter.HasValue)
            {
                hasSignal = true;
                if (selectedBefore.Value != selectedAfter.Value)
                    return true;
            }

            if (TryInvokeMethod(gameState, "GetResponseMode", Array.Empty<object>(), out var modeObj) && modeObj != null)
            {
                hasSignal = true;
                var mode = modeObj.ToString();
                if (!string.Equals(mode, "OPTION", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return assumeSuccessWhenNoSignal && !hasSignal;
        }

        private static int? GetSelectedOptionValue(object gameState)
        {
            if (gameState == null)
                return null;

            var value = GetIntFieldOrProp(gameState, "m_selectedOption");
            if (value != 0 || HasFieldOrProp(gameState, "m_selectedOption"))
                return value;

            value = GetIntFieldOrProp(gameState, "SelectedOption");
            if (value != 0 || HasFieldOrProp(gameState, "SelectedOption"))
                return value;

            if (TryInvokeMethod(gameState, "GetSelectedOption", Array.Empty<object>(), out var result))
            {
                try
                {
                    return Convert.ToInt32(result);
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool TryBuildSubmitArgs(
            ParameterInfo[] parameters,
            object option,
            int optionIndex,
            int subOptionIndex,
            int targetIndex,
            int position,
            out object[] args)
        {
            args = null;
            if (parameters == null)
                return false;

            var built = new object[parameters.Length];
            var hasKnownArg = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var paramType = parameter.ParameterType;
                var paramName = (parameter.Name ?? string.Empty).ToLowerInvariant();

                if (option != null && paramType.IsInstanceOfType(option))
                {
                    built[i] = option;
                    hasKnownArg = true;
                    continue;
                }

                if (paramType == typeof(int))
                {
                    if (paramName.Contains("sub"))
                        built[i] = subOptionIndex;
                    else if (paramName.Contains("target"))
                        built[i] = targetIndex;
                    else if (paramName.Contains("position") || paramName.Contains("pos"))
                        built[i] = position > 0 ? position : 0;
                    else
                        built[i] = optionIndex;

                    hasKnownArg = true;
                    continue;
                }

                if (paramType == typeof(bool))
                {
                    built[i] = false;
                    continue;
                }

                if (paramType == typeof(string))
                {
                    built[i] = string.Empty;
                    continue;
                }

                if (!paramType.IsValueType)
                {
                    built[i] = null;
                    continue;
                }

                built[i] = Activator.CreateInstance(paramType);
            }

            if (parameters.Length > 0 && !hasKnownArg)
                return false;

            args = built;
            return true;
        }


        private static object FindOptionsList(out string diag)
        {
            diag = null;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var tried = new List<string>();

            // 优先从 GameState 获取
            var gs = GetGameState();
            if (gs != null)
            {
                var result = TryGetOptions(gs, flags, tried);
                if (result != null) return result;
            }

            // 再从 Network / ConnectAPI 获取
            foreach (var singletonType in new[] { _networkType, _connectApiType })
            {
                var inst = GetSingleton(singletonType);
                if (inst != null)
                {
                    var result = TryGetOptions(inst, flags, tried);
                    if (result != null) return result;
                }
            }

            // 列出所有 option 相关成员用于诊断
            var members = new List<string>();
            foreach (var type in new[] { _gameStateType, _networkType, _connectApiType })
            {
                if (type == null) continue;
                var allMembers = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var m in allMembers)
                {
                    if (m.Name.IndexOf("ption", StringComparison.OrdinalIgnoreCase) >= 0)
                        members.Add(type.Name + "." + m.Name + "(" + m.MemberType + ")");
                }
            }

            diag = "no_options:tried=" + string.Join(",", tried) + ";members=" + string.Join(",", members);
            return null;
        }

        private static object TryGetOptions(object target, BindingFlags flags, List<string> tried)
        {
            var type = target.GetType();
            var names = new[] { "GetOptions", "GetOptionsPacket", "m_options", "m_lastOptions", "m_optionsPacket", "Options" };
            foreach (var name in names)
            {
                object val = null;
                var mi = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
                if (mi != null)
                {
                    tried.Add(type.Name + "." + name + "()");
                    try { val = mi.Invoke(target, null); } catch { }
                }
                else
                {
                    val = GetFieldOrProp(target, name);
                    if (val != null) tried.Add(type.Name + "." + name);
                }

                if (val == null) continue;

                // 直接是列表
                if (val is IEnumerable && !(val is string)) return val;

                // 包装对象，尝试提取内部列表
                var inner = ExtractListFromWrapper(val);
                if (inner != null)
                {
                    tried.Add(val.GetType().Name + "->unwrap");
                    return inner;
                }
            }
            return null;
        }

        private static IEnumerable ExtractListFromWrapper(object wrapper)
        {
            if (wrapper == null) return null;
            var innerNames = new[] { "List", "Options", "m_options", "m_list", "options" };
            foreach (var name in innerNames)
            {
                var val = GetFieldOrProp(wrapper, name);
                if (val is IEnumerable e && !(val is string)) return e;
            }
            // 尝试所有 IEnumerable 字段
            var wType = wrapper.GetType();
            foreach (var f in wType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (typeof(IEnumerable).IsAssignableFrom(f.FieldType) && f.FieldType != typeof(string))
                {
                    var val = f.GetValue(wrapper);
                    if (val is IEnumerable e) return e;
                }
            }
            return null;
        }

        #endregion

        #region 反射工具

        private static object GetGameState()
        {
            return InvokeStatic(_gameStateType, "Get");
        }

        private static object GetEntity(object gameState, int entityId)
        {
            if (gameState == null || entityId <= 0) return null;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var methods = gameState.GetType().GetMethods(flags)
                .Where(m =>
                    (string.Equals(m.Name, "GetEntity", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.Name, "GetEntityByID", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.Name, "GetEntityById", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.Name, "FindEntity", StringComparison.OrdinalIgnoreCase))
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(int))
                .ToArray();

            foreach (var method in methods)
            {
                try
                {
                    var entity = method.Invoke(gameState, new object[] { entityId });
                    if (entity != null) return entity;
                }
                catch
                {
                }
            }

            var entities = Invoke(gameState, "GetEntities") as IEnumerable
                ?? GetFieldOrProp(gameState, "m_entities") as IEnumerable
                ?? GetFieldOrProp(gameState, "Entities") as IEnumerable;
            if (entities != null)
            {
                foreach (var entity in entities)
                {
                    if (ResolveEntityId(entity) == entityId)
                        return entity;
                }
            }

            return null;
        }

        private static bool TryIsEntityInFriendlyHand(object gameState, int entityId, out bool inHand)
        {
            inHand = false;
            if (gameState == null || entityId <= 0)
                return false;

            if (!TryGetFriendlyHandCards(gameState, out var cards))
                return false;

            foreach (var candidate in cards)
            {
                var candidateEntity = Invoke(candidate, "GetEntity")
                    ?? GetFieldOrProp(candidate, "Entity")
                    ?? candidate;
                if (candidateEntity == null) continue;

                if (ResolveEntityId(candidateEntity) == entityId)
                {
                    inHand = true;
                    break;
                }
            }

            return true;
        }

        private static bool TryGetFriendlyHandCards(object gameState, out IEnumerable cards)
        {
            cards = null;
            if (gameState == null)
                return false;

            var friendly = Invoke(gameState, "GetFriendlySidePlayer")
                ?? Invoke(gameState, "GetFriendlyPlayer")
                ?? Invoke(gameState, "GetLocalPlayer");
            if (friendly == null)
                return false;

            var handZone = Invoke(friendly, "GetHandZone")
                ?? Invoke(friendly, "GetHand")
                ?? GetFieldOrProp(friendly, "m_handZone")
                ?? GetFieldOrProp(friendly, "HandZone");
            if (handZone == null)
                return false;

            cards = Invoke(handZone, "GetCards") as IEnumerable
                ?? GetFieldOrProp(handZone, "Cards") as IEnumerable
                ?? GetFieldOrProp(handZone, "m_cards") as IEnumerable
                ?? handZone as IEnumerable;

            return cards != null;
        }

        private static bool TryReadFriendlyHandEntityIds(object gameState, out HashSet<int> ids)
        {
            ids = new HashSet<int>();
            if (!TryGetFriendlyHandCards(gameState, out var cards) || cards == null)
                return false;

            foreach (var candidate in cards)
            {
                var candidateEntity = Invoke(candidate, "GetEntity")
                    ?? GetFieldOrProp(candidate, "Entity")
                    ?? candidate;
                if (candidateEntity == null) continue;

                var id = ResolveEntityId(candidateEntity);
                if (id > 0)
                    ids.Add(id);
            }

            return true;
        }

        private static object ResolveFriendlyHandCardObject(object gameState, int entityId)
        {
            if (gameState == null || entityId <= 0)
                return null;

            if (!TryGetFriendlyHandCards(gameState, out var cards) || cards == null)
                return null;

            foreach (var candidate in cards)
            {
                var candidateEntity = Invoke(candidate, "GetEntity")
                    ?? GetFieldOrProp(candidate, "Entity")
                    ?? GetFieldOrProp(candidate, "m_entity")
                    ?? candidate;
                if (candidateEntity == null)
                    continue;

                if (ResolveEntityId(candidateEntity) == entityId)
                    return candidate;
            }

            return null;
        }

        private static bool TryGetHeldCardEntityId(object inputMgr, out int heldEntityId)
        {
            heldEntityId = 0;
            if (inputMgr == null)
                return false;

            var heldCard = GetFieldOrProp(inputMgr, "m_heldCard")
                ?? GetFieldOrProp(inputMgr, "heldCard")
                ?? Invoke(inputMgr, "GetHeldCard");
            if (heldCard == null)
                return false;

            var heldEntity = Invoke(heldCard, "GetEntity")
                ?? GetFieldOrProp(heldCard, "Entity")
                ?? GetFieldOrProp(heldCard, "m_entity")
                ?? heldCard;
            heldEntityId = ResolveEntityId(heldEntity);
            return heldEntityId > 0;
        }

        private enum HeldCardWaitStatus
        {
            None = 0,
            Expected = 1,
            Mismatch = 2
        }

        private static HeldCardWaitStatus WaitForHeldCardAfterGrab(object inputMgr, int expectedEntityId, out int heldEntityId)
        {
            heldEntityId = 0;
            if (inputMgr == null || expectedEntityId <= 0)
                return HeldCardWaitStatus.None;

            const int timeoutMs = 100;
            const int pollMs = 8;
            var deadline = Environment.TickCount + timeoutMs;

            while (true)
            {
                if (TryGetHeldCardEntityId(inputMgr, out heldEntityId))
                {
                    return heldEntityId == expectedEntityId
                        ? HeldCardWaitStatus.Expected
                        : HeldCardWaitStatus.Mismatch;
                }

                if (Environment.TickCount - deadline >= 0)
                    break;

                Thread.Sleep(pollMs);
            }

            heldEntityId = 0;
            return HeldCardWaitStatus.None;
        }

        private static object ResolveGrabGameObject(object entity, object card)
        {
            try
            {
                var actor = card != null ? Invoke(card, "GetActor") : null;
                return GetFieldOrProp(card, "gameObject")
                    ?? GetFieldOrProp(actor, "gameObject")
                    ?? GetFieldOrProp(entity, "gameObject");
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGrabCardViaCurrentClientFlow(
            object card,
            object inputMgr,
            int entityId,
            int sourceZonePosition,
            out string methodUsed,
            out int heldEntityId,
            out string detail)
        {
            methodUsed = string.Empty;
            heldEntityId = 0;
            detail = "current_client_card_missing";

            if (card == null)
                return false;
            if (inputMgr == null || entityId <= 0)
            {
                detail = "input_manager_missing";
                return false;
            }

            var cardGameObject = GetFieldOrProp(card, "gameObject");
            var zoneHand = Invoke(card, "GetZone");
            if (zoneHand == null || !string.Equals(zoneHand.GetType().Name, "ZoneHand", StringComparison.OrdinalIgnoreCase))
            {
                detail = "zonehand_missing";
                return false;
            }

            var standIn = ResolveCardStandIn(zoneHand, card, out var standInDetail);
            var standInGameObject = standIn != null ? GetFieldOrProp(standIn, "gameObject") : null;
            var attempts = new List<string>();

            AppendActionTrace(
                "Current-client grab prepare entityId=" + entityId
                + " zonePos=" + sourceZonePosition
                + " cardGameObject=" + DescribeObjectName(cardGameObject)
                + " standInType=" + DescribeObjectType(standIn)
                + " standInGameObject=" + DescribeObjectName(standInGameObject)
                + " standInDetail=" + standInDetail);

            if (standIn != null && standInGameObject != null)
            {
                if (TryInvokeMethod(inputMgr, "TryHandleClickOnCard", new object[] { standInGameObject, standIn }, out _, out var standInInvokeError))
                {
                    var holdStatus = WaitForHeldCardAfterGrab(inputMgr, entityId, out heldEntityId);
                    if (holdStatus == HeldCardWaitStatus.Expected)
                    {
                        methodUsed = "TryHandleClickOnCard";
                        detail = "ok";
                        AppendActionTrace(
                            "API grabbed card entityId=" + entityId
                            + " via TryHandleClickOnCard"
                            + " zonePos=" + sourceZonePosition);
                        return true;
                    }

                    if (holdStatus == HeldCardWaitStatus.Mismatch)
                    {
                        attempts.Add("TryHandleClickOnCard:held_mismatch:" + heldEntityId);
                        TryResetHeldCard();
                    }
                    else
                    {
                        attempts.Add("TryHandleClickOnCard:held_card_not_detected");
                    }
                }
                else
                {
                    attempts.Add("TryHandleClickOnCard:invoke_failed:" + standInInvokeError);
                }
            }
            else
            {
                attempts.Add("TryHandleClickOnCard:standin_unavailable:" + standInDetail);
            }

            if (cardGameObject != null)
            {
                if (TryInvokeMethod(inputMgr, "HandleClickOnCard", new object[] { cardGameObject, true }, out _, out var handleClickError))
                {
                    var holdStatus = WaitForHeldCardAfterGrab(inputMgr, entityId, out heldEntityId);
                    if (holdStatus == HeldCardWaitStatus.Expected)
                    {
                        methodUsed = "HandleClickOnCard";
                        detail = "ok";
                        AppendActionTrace(
                            "API grabbed card entityId=" + entityId
                            + " via HandleClickOnCard"
                            + " zonePos=" + sourceZonePosition);
                        return true;
                    }

                    if (holdStatus == HeldCardWaitStatus.Mismatch)
                    {
                        attempts.Add("HandleClickOnCard:held_mismatch:" + heldEntityId);
                        TryResetHeldCard();
                    }
                    else
                    {
                        attempts.Add("HandleClickOnCard:held_card_not_detected");
                    }
                }
                else
                {
                    attempts.Add("HandleClickOnCard:invoke_failed:" + handleClickError);
                }

                if (TryInvokeMethod(inputMgr, "GrabCard", new object[] { cardGameObject }, out _, out var grabError))
                {
                    var holdStatus = WaitForHeldCardAfterGrab(inputMgr, entityId, out heldEntityId);
                    if (holdStatus == HeldCardWaitStatus.Expected)
                    {
                        methodUsed = "GrabCard";
                        detail = "ok";
                        AppendActionTrace(
                            "API grabbed card entityId=" + entityId
                            + " via GrabCard"
                            + " zonePos=" + sourceZonePosition);
                        return true;
                    }

                    if (holdStatus == HeldCardWaitStatus.Mismatch)
                    {
                        attempts.Add("GrabCard:held_mismatch:" + heldEntityId);
                        TryResetHeldCard();
                    }
                    else
                    {
                        attempts.Add("GrabCard:held_card_not_detected");
                    }
                }
                else
                {
                    attempts.Add("GrabCard:invoke_failed:" + grabError);
                }
            }
            else
            {
                attempts.Add("card_gameobject_missing");
            }

            detail = string.Join(" | ", attempts.ToArray());
            AppendActionTrace(
                "Current-client grab failed entityId=" + entityId
                + " zonePos=" + sourceZonePosition
                + " detail=" + detail);
            return false;
        }

        private static object ResolveCardStandIn(object zoneHand, object card, out string detail)
        {
            detail = string.Empty;
            if (zoneHand == null || card == null)
            {
                detail = "missing_zonehand_or_card";
                return null;
            }

            if (TryInvokeMethod(zoneHand, "GetStandIn", new object[] { card }, out var standIn, out var getStandInError) && standIn != null)
                return standIn;

            if (TryInvokeMethod(zoneHand, "CreateCardStandIn", new object[] { card }, out _, out var createStandInError))
            {
                if (TryInvokeMethod(zoneHand, "GetStandIn", new object[] { card }, out standIn, out getStandInError) && standIn != null)
                    return standIn;
            }

            standIn = GetFieldOrProp(zoneHand, "CurrentStandIn")
                ?? GetFieldOrProp(zoneHand, "m_hiddenStandIn");
            if (standIn != null)
                return standIn;

            detail = "get=" + (string.IsNullOrWhiteSpace(getStandInError) ? "null" : getStandInError)
                + ";create=" + (string.IsNullOrWhiteSpace(createStandInError) ? "null" : createStandInError);
            return null;
        }

        private static bool TryGrabCardViaCardObject(
            object card,
            object inputMgr,
            int entityId,
            int sourceZonePosition,
            out string methodUsed,
            out int heldEntityId,
            out string detail)
        {
            methodUsed = string.Empty;
            heldEntityId = 0;
            detail = "card_object_missing";

            if (card == null)
                return false;
            if (inputMgr == null || entityId <= 0)
            {
                detail = "input_manager_missing";
                return false;
            }

            var methodNames = new[]
            {
                "LightningPickup",
                "Pickup",
                "PickUp"
            };

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var lastFailure = "no_card_pickup_method";
            var attemptFailures = new List<string>();
            var cardType = DescribeObjectType(card);

            foreach (var methodName in methodNames)
            {
                var methods = card.GetType().GetMethods(flags)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(m => m.GetParameters().Length)
                    .ToArray();

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    var signature = DescribeParameters(parameters);
                    if (!TryBuildCardPickupArgs(parameters, out var args))
                    {
                        lastFailure = "no_compatible_card_pickup_overload:" + methodName;
                        attemptFailures.Add("Card." + methodName + "[" + signature + "]:" + lastFailure);
                        continue;
                    }

                    try
                    {
                        method.Invoke(card, args);

                        var holdStatus = WaitForHeldCardAfterGrab(inputMgr, entityId, out heldEntityId);
                        if (holdStatus == HeldCardWaitStatus.Expected)
                        {
                            methodUsed = "Card." + methodName;
                            detail = "ok";
                            AppendActionTrace(
                                "API grabbed card entityId=" + entityId
                                + " via Card." + methodName
                                + " zonePos=" + sourceZonePosition
                                + " cardType=" + card.GetType().FullName);
                            return true;
                        }

                        if (holdStatus == HeldCardWaitStatus.Mismatch)
                        {
                            lastFailure = "held_mismatch:Card." + methodName + ":" + heldEntityId;
                            attemptFailures.Add("Card." + methodName + "[" + signature + "]:" + lastFailure);
                            AppendActionTrace(
                                "API grab mismatch expected=" + entityId
                                + " actual=" + heldEntityId
                                + " via Card." + methodName
                                + " zonePos=" + sourceZonePosition);
                            TryResetHeldCard();
                            break;
                        }

                        lastFailure = "held_card_not_detected:Card." + methodName;
                        attemptFailures.Add("Card." + methodName + "[" + signature + "]:" + lastFailure);
                    }
                    catch (Exception ex)
                    {
                        lastFailure = "invoke_failed:Card." + methodName + ":" + SimplifyException(ex);
                        attemptFailures.Add("Card." + methodName + "[" + signature + "]:" + lastFailure);
                    }
                }
            }

            detail = lastFailure;
            AppendActionTrace(
                "Card-object grab failed entityId=" + entityId
                + " zonePos=" + sourceZonePosition
                + " cardType=" + cardType
                + " detail=" + lastFailure
                + " attempts=" + string.Join(" || ", attemptFailures.Take(8).ToArray()));
            return false;
        }

        private static bool TryResetHeldCard()
        {
            try
            {
                if (!EnsureTypes())
                    return false;

                var inputMgr = GetSingleton(_inputMgrType);
                if (inputMgr == null)
                    return false;

                if (TryInvokeParameterlessMethods(inputMgr, new[]
                {
                    "CancelHeldCard",
                    "CancelDragCard",
                    "ClearHeldCard",
                    "ReleaseHeldCard",
                    "OnCardDragCanceled",
                    "CancelDrag",
                    "Abort"
                }, out _))
                {
                    return true;
                }

                var type = inputMgr.GetType();
                var field = type.GetField("m_heldCard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? type.GetField("heldCard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(inputMgr, null);
                    return true;
                }

                var prop = type.GetProperty("heldCard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? type.GetProperty("HeldCard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? type.GetProperty("m_heldCard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(inputMgr, null, null);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static string ResolveEntityCardId(object gameState, int entityId)
        {
            if (gameState == null || entityId <= 0)
                return string.Empty;

            var entity = GetEntity(gameState, entityId);
            if (entity == null)
                return string.Empty;

            var cardIdObj = Invoke(entity, "GetCardId")
                ?? GetFieldOrProp(entity, "CardId")
                ?? GetFieldOrProp(entity, "m_cardId")
                ?? GetFieldOrProp(entity, "m_cardID");
            return cardIdObj?.ToString() ?? string.Empty;
        }

        private static int ResolveEntityZoneTag(object gameState, int entityId)
        {
            if (gameState == null || entityId <= 0)
                return 0;

            var entity = GetEntity(gameState, entityId);
            if (entity == null)
                return 0;

            try
            {
                var ctx = ReflectionContext.Instance;
                if (ctx.Init())
                {
                    var zone = ctx.GetTagValue(entity, "ZONE");
                    if (zone > 0)
                        return zone;
                }
            }
            catch
            {
            }

            var fallback = GetIntFieldOrProp(entity, "Zone");
            if (fallback <= 0) fallback = GetIntFieldOrProp(entity, "ZONE");
            if (fallback <= 0) fallback = GetIntFieldOrProp(entity, "m_zone");
            return fallback > 0 ? fallback : 0;
        }

        private static int ResolveEntityZonePosition(object gameState, int entityId)
        {
            if (gameState == null || entityId <= 0)
                return 0;

            var entity = GetEntity(gameState, entityId);
            if (entity == null)
                return 0;

            try
            {
                var ctx = ReflectionContext.Instance;
                if (ctx.Init())
                {
                    var zonePos = ctx.GetTagValue(entity, "ZONE_POSITION");
                    if (zonePos > 0)
                        return zonePos;
                }
            }
            catch
            {
            }

            var fallback = GetIntFieldOrProp(entity, "ZonePosition");
            if (fallback <= 0) fallback = GetIntFieldOrProp(entity, "ZONE_POSITION");
            if (fallback <= 0) fallback = GetIntFieldOrProp(entity, "m_zonePosition");
            return fallback > 0 ? fallback : 0;
        }

        private static void AddDistinctObjectCandidate(List<object> list, object candidate)
        {
            if (list == null || candidate == null)
                return;

            if (!list.Any(existing => ReferenceEquals(existing, candidate)))
                list.Add(candidate);
        }

        private static string DescribeObjectType(object value)
        {
            return value == null ? "null" : value.GetType().FullName;
        }

        private static string DescribeObjectName(object value)
        {
            if (value == null)
                return "null";

            var name = GetFieldOrProp(value, "name")?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                return DescribeObjectType(value) + ":" + name;

            return DescribeObjectType(value);
        }

        private static string DescribeParameters(ParameterInfo[] parameters)
        {
            if (parameters == null || parameters.Length == 0)
                return "void";

            return string.Join(
                ",",
                parameters.Select(p => p.ParameterType.Name + " " + (p.Name ?? string.Empty)).ToArray());
        }

        private static void AppendActionTrace(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                var logDir = System.IO.Path.GetDirectoryName(typeof(ActionExecutor).Assembly.Location);
                var logPath = string.IsNullOrWhiteSpace(logDir)
                    ? "payload_error.log"
                    : System.IO.Path.Combine(logDir, "payload_error.log");
                System.IO.File.AppendAllText(
                    logPath,
                    DateTime.Now + ": [Action] " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static object GetSingleton(Type type)
        {
            if (type == null) return null;
            var mi = type.GetMethod("Get", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (mi != null) return mi.Invoke(null, null);
            return null;
        }

        private static object Invoke(object obj, string method)
        {
            if (obj == null) return null;
            var mi = obj.GetType().GetMethod(method,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi != null) return mi.Invoke(obj, null);
            return null;
        }

        private static bool TryInvokeMethod(object obj, string method, object[] args, out object result)
        {
            return TryInvokeMethod(obj, method, args, out result, out _);
        }

        private static bool TryInvokeMethod(object obj, string method, object[] args, out object result, out string error)
        {
            result = null;
            error = null;
            if (obj == null || string.IsNullOrWhiteSpace(method))
            {
                error = "invalid_target_or_method";
                return false;
            }

            args = args ?? Array.Empty<object>();
            var methods = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase))
                .Where(m => m.GetParameters().Length == args.Length)
                .ToArray();

            if (methods.Length == 0)
            {
                error = "method_not_found";
                return false;
            }

            string lastError = null;
            foreach (var mi in methods)
            {
                var pars = mi.GetParameters();
                var invokeArgs = new object[args.Length];
                var compatible = true;

                for (int i = 0; i < pars.Length; i++)
                {
                    var arg = args[i];
                    var paramType = pars[i].ParameterType;

                    if (arg == null)
                    {
                        if (paramType.IsValueType && Nullable.GetUnderlyingType(paramType) == null)
                        {
                            compatible = false;
                            break;
                        }

                        invokeArgs[i] = null;
                        continue;
                    }

                    if (paramType.IsInstanceOfType(arg))
                    {
                        invokeArgs[i] = arg;
                        continue;
                    }

                    try
                    {
                        invokeArgs[i] = Convert.ChangeType(arg, paramType);
                    }
                    catch
                    {
                        compatible = false;
                        break;
                    }
                }

                if (!compatible)
                    continue;

                try
                {
                    result = mi.Invoke(obj, invokeArgs);
                    return true;
                }
                catch (Exception ex)
                {
                    lastError = mi.Name + ":" + SimplifyException(ex);
                }
            }

            error = lastError ?? "no_compatible_overload";
            return false;
        }

        private static bool TryInvokeBoolMethod(object obj, string method, out bool value)
        {
            value = false;
            if (!TryInvokeMethod(obj, method, Array.Empty<object>(), out var result))
                return false;

            if (result is bool boolResult)
            {
                value = boolResult;
                return true;
            }

            try
            {
                value = Convert.ToBoolean(result);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ReadBoolValue(object value)
        {
            if (value == null)
                return false;

            try
            {
                return Convert.ToBoolean(value);
            }
            catch
            {
                return false;
            }
        }

        private static string SimplifyException(Exception ex)
        {
            if (ex == null) return "unknown";

            var current = ex;
            while (current is TargetInvocationException && current.InnerException != null)
                current = current.InnerException;

            var message = current.Message;
            if (string.IsNullOrWhiteSpace(message))
                return current.GetType().Name;

            return current.GetType().Name + ":" + message;
        }

        private static object InvokeStatic(Type type, string method)
        {
            if (type == null) return null;
            var mi = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (mi != null) return mi.Invoke(null, null);
            return null;
        }

        private static object GetFieldOrProp(object obj, string name)
        {
            if (obj == null) return null;
            var type = obj.GetType();
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null) return prop.GetValue(obj, null);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return field.GetValue(obj);
            return null;
        }

        private static bool HasFieldOrProp(object obj, string name)
        {
            if (obj == null) return false;
            var type = obj.GetType();
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null) return true;
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null;
        }

        private static int GetIntFieldOrProp(object obj, string name)
        {
            var val = GetFieldOrProp(obj, name);
            if (val == null) return 0;

            try
            {
                if (val is int i) return i;
                if (val is short s) return s;
                if (val is long l) return (int)l;
                if (val is byte b) return b;

                var t = val.GetType();
                if (t.IsEnum) return Convert.ToInt32(val);

                return Convert.ToInt32(val);
            }
            catch
            {
                return 0;
            }
        }

        private static object[] BuildArgs(
            ParameterInfo[] pars,
            object entity,
            int targetEntityId,
            int position,
            int sourceZonePosition = 0)
        {
            var args = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
            {
                if (i == 0) args[i] = entity;
                else if (pars[i].ParameterType == typeof(int))
                {
                    var paramName = (pars[i].Name ?? string.Empty).ToLowerInvariant();
                    if (paramName.Contains("target"))
                        args[i] = targetEntityId;
                    else if (paramName.Contains("index")
                        || paramName.Contains("idx")
                        || paramName.Contains("slot")
                        || paramName == "i")
                    {
                        args[i] = sourceZonePosition > 0
                            ? Math.Max(0, sourceZonePosition - 1)
                            : 0;
                    }
                    else if (paramName.Contains("zoneposition")
                        || paramName.Contains("zone_position"))
                    {
                        args[i] = sourceZonePosition > 0 ? sourceZonePosition : 0;
                    }
                    else if (paramName.Contains("pos"))
                        args[i] = position;
                    else
                        args[i] = 0;
                }
                else if (pars[i].ParameterType == typeof(bool))
                {
                    args[i] = false;
                }
                else if (pars[i].ParameterType.IsValueType)
                {
                    args[i] = Activator.CreateInstance(pars[i].ParameterType);
                }
                else
                {
                    args[i] = null;
                }
            }
            return args;
        }

        private static bool TryBuildCardPickupArgs(ParameterInfo[] pars, out object[] args)
        {
            args = null;
            if (pars == null)
                return false;

            var built = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
            {
                var paramType = pars[i].ParameterType;
                if (paramType.IsByRef)
                    return false;

                var paramName = (pars[i].Name ?? string.Empty).ToLowerInvariant();
                if (paramType == typeof(int))
                {
                    built[i] = paramName.Contains("timeout") || paramName.Contains("delay")
                        ? 100
                        : 0;
                    continue;
                }

                if (paramType == typeof(bool))
                {
                    built[i] = false;
                    continue;
                }

                if (paramType == typeof(string))
                {
                    built[i] = string.Empty;
                    continue;
                }

                if (paramType.IsValueType)
                {
                    built[i] = Activator.CreateInstance(paramType);
                    continue;
                }

                built[i] = null;
            }

            args = built;
            return true;
        }

        private static bool TryBuildGrabArgs(
            ParameterInfo[] pars,
            object firstArg,
            int sourceZonePosition,
            out object[] args)
        {
            args = null;
            if (pars == null || pars.Length == 0 || firstArg == null)
                return false;

            if (!pars[0].ParameterType.IsInstanceOfType(firstArg))
                return false;

            var built = new object[pars.Length];
            built[0] = firstArg;

            for (int i = 1; i < pars.Length; i++)
            {
                var paramType = pars[i].ParameterType;
                var paramName = (pars[i].Name ?? string.Empty).ToLowerInvariant();

                if (paramType == typeof(int))
                {
                    if (paramName.Contains("index")
                        || paramName.Contains("idx")
                        || paramName.Contains("slot")
                        || paramName == "i")
                    {
                        built[i] = sourceZonePosition > 0
                            ? Math.Max(0, sourceZonePosition - 1)
                            : 0;
                    }
                    else if (paramName.Contains("zoneposition")
                        || paramName.Contains("zone_position"))
                    {
                        built[i] = sourceZonePosition > 0 ? sourceZonePosition : 0;
                    }
                    else
                    {
                        built[i] = 0;
                    }

                    continue;
                }

                if (paramType == typeof(bool))
                {
                    built[i] = false;
                    continue;
                }

                if (paramType.IsValueType)
                {
                    built[i] = Activator.CreateInstance(paramType);
                    continue;
                }

                built[i] = null;
            }

            args = built;
            return true;
        }

        #endregion

        #region 战旗模式鼠标协程

        /// <summary>
        /// 战旗购买：将商店实体拖动到我方英雄购买区，使其先进入手牌。
        /// </summary>
        private static IEnumerator<float> BgMouseBuy(int shopEntityId, int position)
        {
            InputHook.Simulating = true;

            var gsBeforeBuy = GetGameState();
            var hasBeforeHand = TryReadFriendlyHandEntityIds(gsBeforeBuy, out var beforeHandIds);
            var beforeHandCount = hasBeforeHand ? beforeHandIds.Count : 0;
            var beforeZone = ResolveEntityZoneTag(gsBeforeBuy, shopEntityId);
            if (!TryReadBattlegroundsShopMetrics(out var beforeShopSignature, out var beforeGold, out _, out _))
            {
                beforeShopSignature = string.Empty;
                beforeGold = -1;
            }

            // 获取商店牌屏幕坐标
            if (!TryResolveBattlegroundsShopCardScreenPos(shopEntityId, position, out var sx, out var sy, out var shopPosDetail))
            {
                _coroutine.SetResult("FAIL:BG_BUY:shop_pos:" + shopEntityId + ":slot=" + position + ":" + shopPosDetail);
                yield break;
            }

            // 购买区落点：我方英雄附近
            if (!TryResolveBattlegroundsBuyDropScreenPos(out var dx, out var dy))
            {
                dx = MouseSimulator.GetScreenWidth() / 2;
                dy = (int)(MouseSimulator.GetScreenHeight() * 0.82f);
            }

            // 拖动：商店 -> 我方英雄购买区
            foreach (var w in SmoothMove(sx, sy, 8, 0.008f)) yield return w;
            MouseSimulator.LeftDown();
            yield return 0.05f;
            foreach (var w in SmoothMove(dx, dy, 12, 0.008f)) yield return w;
            yield return 0.04f;
            MouseSimulator.LeftUp();
            yield return 0.20f;

            var buyConfirmed = false;
            for (var retry = 0; retry < 15; retry++)
            {
                yield return 0.12f;

                var gsAfterBuy = GetGameState();
                var afterZone = ResolveEntityZoneTag(gsAfterBuy, shopEntityId);
                var hasAfterHand = TryReadFriendlyHandEntityIds(gsAfterBuy, out var afterHandIds);
                var afterHandCount = hasAfterHand ? afterHandIds.Count : -1;
                var movedIntoHand = hasAfterHand && afterHandIds.Contains(shopEntityId);
                var handIncreased = hasBeforeHand && hasAfterHand && afterHandCount > beforeHandCount;

                var leftShop = afterZone > 0
                    && afterZone != 1
                    && afterZone != 5
                    && afterZone != beforeZone;

                var shopChanged = false;
                var goldSpent = false;
                if (TryReadBattlegroundsShopMetrics(out var afterShopSignature, out var afterGold, out _, out _))
                {
                    shopChanged = !string.Equals(beforeShopSignature, afterShopSignature, StringComparison.Ordinal);
                    goldSpent = beforeGold >= 0 && afterGold >= 0 && afterGold < beforeGold;
                }

                if (movedIntoHand
                    || handIncreased
                    || afterZone == 3
                    || (leftShop && (shopChanged || goldSpent)))
                {
                    buyConfirmed = true;
                    break;
                }
            }

            if (!buyConfirmed)
            {
                _coroutine.SetResult("FAIL:BG_BUY:not_confirmed:" + shopEntityId + ":pos=" + position);
                yield break;
            }

            _coroutine.SetResult("OK:BG_BUY:" + shopEntityId);
        }

        private static bool TryResolveBattlegroundsShopCardScreenPos(
            int shopEntityId,
            int shopPosition,
            out int x,
            out int y,
            out string detail)
        {
            x = y = 0;
            detail = "shop_card_not_found";

            if (shopEntityId > 0 && GameObjectFinder.GetEntityScreenPos(shopEntityId, out x, out y))
            {
                detail = "entity";
                return true;
            }

            var gameState = GetGameState();
            if (gameState == null)
            {
                detail = "game_state_null";
                return false;
            }

            var opposing = Invoke(gameState, "GetOpposingSidePlayer") ?? Invoke(gameState, "GetOpposingPlayer");
            if (opposing == null)
            {
                detail = "opposing_player_null";
                return false;
            }

            var zone = Invoke(opposing, "GetBattlefieldZone") ?? Invoke(opposing, "GetPlayZone");
            if (zone == null)
            {
                detail = "shop_zone_null";
                return false;
            }

            var cards = Invoke(zone, "GetCards") as IEnumerable
                ?? GetFieldOrProp(zone, "m_cards") as IEnumerable;
            if (cards == null)
            {
                detail = "shop_cards_null";
                return false;
            }

            foreach (var rawCard in cards)
            {
                if (rawCard == null)
                    continue;

                var entity = Invoke(rawCard, "GetEntity") ?? GetFieldOrProp(rawCard, "Entity") ?? GetFieldOrProp(rawCard, "m_entity");
                var entityId = ResolveEntityId(entity);
                var zonePosition = entityId > 0
                    ? ResolveEntityZonePosition(gameState, entityId)
                    : GetIntFieldOrProp(entity ?? rawCard, "ZonePosition");

                var matchesEntity = shopEntityId > 0 && entityId == shopEntityId;
                var matchesPosition = shopPosition > 0 && zonePosition == shopPosition;
                if (!matchesEntity && !matchesPosition)
                    continue;

                if (GameObjectFinder.GetObjectScreenPos(rawCard, out x, out y)
                    || (entityId > 0 && GameObjectFinder.GetEntityScreenPos(entityId, out x, out y)))
                {
                    detail = matchesEntity ? "shop_card_object:entity" : "shop_card_object:slot";
                    return true;
                }

                detail = "shop_card_object_pos_failed";
            }

            return false;
        }

        /// <summary>
        /// 战旗出售：将场上随从拖动到 Bob 的头像区域（屏幕上半部分中央）
        /// </summary>
        private static IEnumerator<float> BgMouseSell(int boardEntityId)
        {
            InputHook.Simulating = true;

            if (!GameObjectFinder.GetEntityScreenPos(boardEntityId, out var sx, out var sy))
            {
                _coroutine.SetResult("FAIL:BG_SELL:board_pos:" + boardEntityId);
                yield break;
            }

            // Bob 区域通常在屏幕上半中央（商店区域）
            int tx = MouseSimulator.GetScreenWidth() / 2;
            int ty = (int)(MouseSimulator.GetScreenHeight() * 0.25f);

            foreach (var w in SmoothMove(sx, sy, 8, 0.008f)) yield return w;
            MouseSimulator.LeftDown();
            yield return 0.05f;
            foreach (var w in SmoothMove(tx, ty, 12, 0.008f)) yield return w;
            yield return 0.04f;
            MouseSimulator.LeftUp();
            yield return 0.18f;

            _coroutine.SetResult("OK:BG_SELL:" + boardEntityId);
        }

        /// <summary>
        /// 战旗调整站位：将场上随从拖到新的位置槽位
        /// </summary>
        private static IEnumerator<float> BgMouseMove(int boardEntityId, int newIndex)
        {
            InputHook.Simulating = true;

            var gsBeforeMove = GetGameState();
            var sourceZonePosition = ResolveEntityZonePosition(gsBeforeMove, boardEntityId);
            int totalMinions = GetFriendlyMinionCount();
            var desiredFinalPosition = NormalizeBattlegroundBoardPosition(newIndex, totalMinions);

            if (sourceZonePosition > 0 && sourceZonePosition == desiredFinalPosition)
            {
                _coroutine.SetResult("OK:BG_MOVE:" + boardEntityId + ":" + desiredFinalPosition + ":noop");
                yield break;
            }

            if (!GameObjectFinder.GetEntityScreenPos(boardEntityId, out var sx, out var sy))
            {
                _coroutine.SetResult("FAIL:BG_MOVE:source_pos:" + boardEntityId);
                yield break;
            }

            var dropResolved = false;
            var dropPosition = 0;
            var dx = 0;
            var dy = 0;

            var targetEntityId = ResolveFriendlyBoardEntityIdAtPosition(gsBeforeMove, desiredFinalPosition, boardEntityId);
            if (targetEntityId > 0 && GameObjectFinder.GetEntityScreenPos(targetEntityId, out dx, out dy))
            {
                dropResolved = true;
            }
            else
            {
                dropPosition = ResolveBattlegroundMoveDropPosition(sourceZonePosition, desiredFinalPosition, totalMinions);
                if (GameObjectFinder.GetBoardDropZoneScreenPos(dropPosition, totalMinions, out dx, out dy))
                    dropResolved = true;
            }

            if (!dropResolved)
            {
                _coroutine.SetResult("FAIL:BG_MOVE:drop_pos:" + desiredFinalPosition);
                yield break;
            }

            foreach (var w in SmoothMove(sx, sy, 6, 0.008f)) yield return w;
            MouseSimulator.LeftDown();
            yield return 0.05f;
            foreach (var w in SmoothMove(dx, dy, 10, 0.008f)) yield return w;
            yield return 0.04f;
            MouseSimulator.LeftUp();

            var latestZonePosition = ResolveEntityZonePosition(GetGameState(), boardEntityId);
            for (var retry = 0; retry < 10; retry++)
            {
                yield return 0.06f;

                latestZonePosition = ResolveEntityZonePosition(GetGameState(), boardEntityId);
                if (desiredFinalPosition > 0 && latestZonePosition == desiredFinalPosition)
                {
                    _coroutine.SetResult("OK:BG_MOVE:" + boardEntityId + ":" + desiredFinalPosition);
                    yield break;
                }
            }

            _coroutine.SetResult(
                "FAIL:BG_MOVE:not_confirmed:"
                + boardEntityId
                + ":from=" + sourceZonePosition
                + ":want=" + desiredFinalPosition
                + ":target=" + targetEntityId
                + ":drop=" + dropPosition
                + ":now=" + latestZonePosition);
        }

        private static int NormalizeBattlegroundBoardPosition(int position, int totalMinions)
        {
            if (totalMinions <= 0)
                return Math.Max(1, position);

            if (position <= 0)
                return totalMinions;

            if (position > totalMinions)
                return totalMinions;

            return position;
        }

        private static int ResolveBattlegroundMoveDropPosition(int sourceZonePosition, int desiredFinalPosition, int totalMinions)
        {
            var slotCount = Math.Max(1, totalMinions + 1);
            var finalPosition = desiredFinalPosition > 0
                ? desiredFinalPosition
                : Math.Max(1, totalMinions);

            // 炉石战旗拖拽使用“插入到缝隙”语义：
            // 向右移动时，要落到目标最终位置的右侧缝隙，最终才能停在期望位次。
            var dropPosition = sourceZonePosition > 0 && finalPosition > sourceZonePosition
                ? finalPosition + 1
                : finalPosition;

            if (dropPosition <= 0)
                return 1;

            if (dropPosition > slotCount)
                return slotCount;

            return dropPosition;
        }

        private static int ResolveFriendlyBoardEntityIdAtPosition(object gameState, int zonePosition, int excludedEntityId)
        {
            if (gameState == null || zonePosition <= 0)
                return 0;

            var friendly = Invoke(gameState, "GetFriendlySidePlayer") ?? Invoke(gameState, "GetFriendlyPlayer");
            if (friendly == null)
                return 0;

            var zone = Invoke(friendly, "GetBattlefieldZone") ?? Invoke(friendly, "GetPlayZone");
            if (zone == null)
                return 0;

            var cards = Invoke(zone, "GetCards") as IEnumerable
                ?? GetFieldOrProp(zone, "m_cards") as IEnumerable;
            if (cards == null)
                return 0;

            foreach (var rawCard in cards)
            {
                if (rawCard == null)
                    continue;

                var entity = Invoke(rawCard, "GetEntity") ?? rawCard;
                var entityId = ResolveEntityId(entity);
                if (entityId <= 0 || entityId == excludedEntityId)
                    continue;

                if (ResolveEntityZonePosition(gameState, entityId) == zonePosition)
                    return entityId;
            }

            return 0;
        }

        private static bool IsBattlegroundMagneticPlay(object gameState, int handEntityId, int targetEntityId)
        {
            if (gameState == null || handEntityId <= 0 || targetEntityId <= 0)
                return false;

            if (!TryGetFriendlyBattlefieldEntityZonePosition(gameState, targetEntityId, out _))
                return false;

            var sourceEntity = GetEntity(gameState, handEntityId);
            if (sourceEntity == null)
                return false;

            return HasSourceEntityTag(sourceEntity, "MAGNETIC")
                || SourceHasMechanic(sourceEntity, "MAGNETIC");
        }

        private static bool TryGetFriendlyBattlefieldCards(object gameState, out IEnumerable cards)
        {
            cards = null;
            if (gameState == null)
                return false;

            var friendly = Invoke(gameState, "GetFriendlySidePlayer") ?? Invoke(gameState, "GetFriendlyPlayer");
            if (friendly == null)
                return false;

            var zone = Invoke(friendly, "GetBattlefieldZone") ?? Invoke(friendly, "GetPlayZone");
            if (zone == null)
                return false;

            cards = Invoke(zone, "GetCards") as IEnumerable
                ?? GetFieldOrProp(zone, "m_cards") as IEnumerable
                ?? zone as IEnumerable;
            return cards != null;
        }

        private static bool TryGetFriendlyBattlefieldEntityZonePosition(object gameState, int entityId, out int zonePosition)
        {
            zonePosition = 0;
            if (entityId <= 0 || !TryGetFriendlyBattlefieldCards(gameState, out var cards) || cards == null)
                return false;

            foreach (var rawCard in cards)
            {
                if (rawCard == null)
                    continue;

                var entity = Invoke(rawCard, "GetEntity")
                    ?? GetFieldOrProp(rawCard, "Entity")
                    ?? rawCard;
                if (entity == null || ResolveEntityId(entity) != entityId)
                    continue;

                zonePosition = ResolveEntityZonePosition(gameState, entityId);
                return zonePosition > 0;
            }

            return false;
        }

        private static bool TryResolveBattlegroundsMagneticDropScreenPos(
            object gameState,
            int targetEntityId,
            int totalMinions,
            out int x,
            out int y,
            out int targetZonePosition,
            out int leftGapX,
            out int leftGapY,
            out string gapSource)
        {
            x = y = 0;
            leftGapX = leftGapY = 0;
            targetZonePosition = 0;
            gapSource = string.Empty;

            if (gameState == null || targetEntityId <= 0 || totalMinions <= 0)
                return false;

            if (!TryGetFriendlyBattlefieldEntityZonePosition(gameState, targetEntityId, out targetZonePosition)
                || targetZonePosition <= 0)
            {
                return false;
            }

            if (!GameObjectFinder.GetEntityScreenPos(targetEntityId, out var targetX, out var targetY))
                return false;

            var hasFallbackGap = GameObjectFinder.GetBoardDropZoneScreenPos(
                targetZonePosition,
                totalMinions,
                out var fallbackGapX,
                out var fallbackGapY);

            var leftNeighborEntityId = ResolveFriendlyBoardEntityIdAtPosition(
                gameState,
                targetZonePosition - 1,
                targetEntityId);
            var leftNeighborX = 0;
            var leftNeighborY = 0;
            var hasLeftNeighbor = leftNeighborEntityId > 0
                && GameObjectFinder.GetEntityScreenPos(leftNeighborEntityId, out leftNeighborX, out leftNeighborY);

            var rightNeighborEntityId = ResolveFriendlyBoardEntityIdAtPosition(
                gameState,
                targetZonePosition + 1,
                targetEntityId);
            var rightNeighborX = 0;
            var rightNeighborY = 0;
            var hasRightNeighbor = rightNeighborEntityId > 0
                && GameObjectFinder.GetEntityScreenPos(rightNeighborEntityId, out rightNeighborX, out rightNeighborY);

            if (!BattlegroundsMagneticDropMath.TryResolveGapScreenPos(
                    targetZonePosition,
                    targetX,
                    targetY,
                    hasFallbackGap,
                    fallbackGapX,
                    fallbackGapY,
                    hasLeftNeighbor,
                    leftNeighborX,
                    leftNeighborY,
                    hasRightNeighbor,
                    rightNeighborX,
                    rightNeighborY,
                    out leftGapX,
                    out leftGapY,
                    out gapSource))
            {
                return false;
            }

            x = leftGapX;
            y = leftGapY;
            return true;
        }

        private static IEnumerator<float> BgPickHero(int choiceIndex)
        {
            InputHook.Simulating = true;

            if (!TryGetHeroPickCardByIndex(choiceIndex, out var snapshot, out var card, out var entityId, out var detail, allowLooseChoicePacket: true))
            {
                _coroutine.SetResult("FAIL:BG_HERO_PICK:" + detail);
                yield break;
            }

            var beforeSignature = BuildMulliganSnapshotSignature(snapshot);
            var selected = false;
            string selectionDetail = null;
            var hadInitialSelection = TryReadHeroPickSelectionState(snapshot, card, entityId, out var initialSelected, out var initialVisualSelected, out _, out _)
                && initialSelected;

            if (TryGetHeroPickCardClickPos(snapshot, card, entityId, choiceIndex - 1, out var cx, out var cy, out detail))
            {
                for (var attempt = 0; attempt < 2 && !selected; attempt++)
                {
                    foreach (var w in SmoothMove(cx, cy, 10, 0.012f)) yield return w;
                    MouseSimulator.LeftDown();
                    yield return 0.05f;
                    MouseSimulator.LeftUp();

                    for (var retry = 0; retry < 10; retry++)
                    {
                        yield return 0.08f;
                        if (TryReadHeroPickSelectionState(snapshot, card, entityId, out var isSelected, out var isVisualSelected, out _, out selectionDetail)
                            && isSelected
                            && (!hadInitialSelection || isVisualSelected || !initialVisualSelected))
                        {
                            selected = true;
                            break;
                        }
                    }
                }
            }

            if (!selected)
            {
                if (TrySelectHeroPickCardViaApi(snapshot, card, entityId, out detail))
                {
                    for (var retry = 0; retry < 10; retry++)
                    {
                        yield return 0.08f;
                        if (TryReadHeroPickSelectionState(snapshot, card, entityId, out var isSelected, out var isVisualSelected, out _, out selectionDetail)
                            && isSelected
                            && (!hadInitialSelection || isVisualSelected || !initialVisualSelected))
                        {
                            selected = true;
                            break;
                        }
                    }
                }
            }

            if (!selected)
            {
                _coroutine.SetResult("FAIL:BG_HERO_PICK:not_selected:" + entityId + ":" + (selectionDetail ?? detail ?? "unknown"));
                yield break;
            }

            for (var retry = 0; retry < 12; retry++)
            {
                if (TryReadHeroPickConfirmButtonState(snapshot.MulliganManager, out var confirmButtonEnabled) && confirmButtonEnabled)
                    break;

                yield return 0.08f;
                TryGetMulliganReadySnapshot(out snapshot, out _, allowLooseChoicePacket: true);
            }

            var confirmMethod = string.Empty;
            var confirmSent = false;
            if (TryConfirmHeroPickViaManager(snapshot?.MulliganManager, out var managerConfirmDetail))
            {
                confirmMethod = managerConfirmDetail;
                confirmSent = true;
            }
            else if (TryClickHeroPickConfirmButton(snapshot?.MulliganManager, out var buttonConfirmDetail))
            {
                confirmMethod = buttonConfirmDetail;
                confirmSent = true;
            }

            if (!confirmSent)
            {
                _coroutine.SetResult("FAIL:BG_HERO_PICK:confirm_failed:" + entityId);
                yield break;
            }

            for (var retry = 0; retry < 20; retry++)
            {
                yield return 0.15f;
                if (!TryGetMulliganReadySnapshot(out var afterSnapshot, out _, allowLooseChoicePacket: true))
                {
                    _coroutine.SetResult("OK:BG_HERO_PICK:" + entityId + ":closed:" + confirmMethod);
                    yield break;
                }

                var afterSignature = BuildMulliganSnapshotSignature(afterSnapshot);
                if (!string.Equals(beforeSignature, afterSignature, StringComparison.Ordinal))
                {
                    _coroutine.SetResult("OK:BG_HERO_PICK:" + entityId + ":changed:" + confirmMethod);
                    yield break;
                }
            }

            _coroutine.SetResult("FAIL:BG_HERO_PICK:not_confirmed:" + entityId + ":" + confirmMethod);
        }

        private static bool TryConfirmHeroPickViaManager(object mulliganMgr, out string detail)
        {
            detail = "hero_pick_confirm_unavailable";
            if (mulliganMgr == null)
                return false;

            if (TryInvokeMethod(mulliganMgr, "OnMulliganButtonReleased", new object[] { null }, out _, out _))
            {
                detail = "MulliganManager.OnMulliganButtonReleased";
                return true;
            }

            if (TryInvokeMethod(mulliganMgr, "AutomaticContinueMulligan", new object[] { false }, out _, out _))
            {
                detail = "MulliganManager.AutomaticContinueMulligan";
                return true;
            }

            if (TryInvokeParameterlessMethods(mulliganMgr, new[]
            {
                "ConfirmMulligan",
                "ContinueMulligan",
                "Confirm",
                "Submit",
                "Done",
                "Accept"
            }, out var managerMethod))
            {
                detail = "MulliganManager." + managerMethod;
                return true;
            }

            return false;
        }

        private static bool TryClickHeroPickConfirmButton(object mulliganMgr, out string detail)
        {
            detail = "hero_pick_confirm_button_missing";
            if (!TryGetHeroPickConfirmButton(mulliganMgr, out var button, out detail))
            {
                if (!GameObjectFinder.GetMulliganConfirmScreenPos(out var fallbackX, out var fallbackY))
                    return false;

                MouseSimulator.MoveTo(fallbackX, fallbackY);
                Thread.Sleep(40);
                MouseSimulator.LeftDown();
                Thread.Sleep(50);
                MouseSimulator.LeftUp();
                detail = "mouse_confirm:fallback";
                return true;
            }

            if (TryTriggerUiElement(button, out detail))
            {
                detail = "HeroPickConfirm." + detail;
                return true;
            }

            var buttonCandidates = new List<object>();
            AddDistinctObjectCandidate(buttonCandidates, button);
            AddDistinctObjectCandidate(buttonCandidates, GetFieldOrProp(button, "NormalButton"));
            AddDistinctObjectCandidate(buttonCandidates, GetFieldOrProp(button, "m_normalButton"));
            AddDistinctObjectCandidate(buttonCandidates, GetFieldOrProp(button, "Button"));
            AddDistinctObjectCandidate(buttonCandidates, GetFieldOrProp(button, "m_button"));

            foreach (var candidate in buttonCandidates)
            {
                if (candidate == null)
                    continue;

                if (TryTriggerUiElement(candidate, out detail))
                {
                    detail = "HeroPickConfirm." + detail;
                    return true;
                }

                if (TryInvokeParameterlessMethods(candidate, new[]
                {
                    "TriggerPress",
                    "TriggerRelease",
                    "TriggerTap",
                    "OnRelease",
                    "OnClick",
                    "HandleRelease",
                    "DoClick"
                }, out var methodUsed))
                {
                    detail = DescribeObjectType(candidate) + "." + methodUsed;
                    return true;
                }
            }

            if (TryGetObjectScreenPos(button, out var x, out var y))
            {
                MouseSimulator.MoveTo(x, y);
                Thread.Sleep(40);
                MouseSimulator.LeftDown();
                Thread.Sleep(50);
                MouseSimulator.LeftUp();
                detail = "mouse_confirm:button_pos";
                return true;
            }

            if (GameObjectFinder.GetMulliganConfirmScreenPos(out var bx, out var by))
            {
                MouseSimulator.MoveTo(bx, by);
                Thread.Sleep(40);
                MouseSimulator.LeftDown();
                Thread.Sleep(50);
                MouseSimulator.LeftUp();
                detail = "mouse_confirm:finder";
                return true;
            }

            detail = "hero_pick_confirm_click_failed";
            return false;
        }

        private static bool TryGetHeroPickConfirmButton(object mulliganMgr, out object button, out string detail)
        {
            button = null;
            detail = "hero_pick_confirm_button_missing";
            if (mulliganMgr == null)
                return false;

            button = Invoke(mulliganMgr, "GetMulliganButton")
                ?? GetFieldOrProp(mulliganMgr, "mulliganButton")
                ?? GetFieldOrProp(mulliganMgr, "mulliganButtonWidget")
                ?? GetFieldOrProp(mulliganMgr, "m_mulliganButton")
                ?? GetFieldOrProp(mulliganMgr, "m_confirmButton")
                ?? GetFieldOrProp(mulliganMgr, "m_doneButton");
            if (button == null)
                return false;

            detail = DescribeObjectType(button);
            return true;
        }

        private static bool TryGetObjectScreenPos(object source, out int x, out int y)
        {
            x = y = 0;
            if (source == null)
                return false;

            var root = GetFieldOrProp(source, "gameObject") ?? source;
            var transform = GetFieldOrProp(root, "transform")
                ?? GetFieldOrProp(source, "transform");
            if (transform == null && !TryInvokeMethod(root, "get_transform", Array.Empty<object>(), out transform, out _))
                return false;

            var pos = GetFieldOrProp(transform, "position");
            if (pos == null && !TryInvokeMethod(transform, "get_position", Array.Empty<object>(), out pos, out _))
                return false;

            if (!TryReadFloatValue(GetFieldOrProp(pos, "x"), out var xf)
                || !TryReadFloatValue(GetFieldOrProp(pos, "y"), out var yf)
                || !TryReadFloatValue(GetFieldOrProp(pos, "z"), out var zf))
            {
                return false;
            }

            return MouseSimulator.WorldToScreen(xf, yf, zf, out x, out y);
        }

        private static bool TryReadFloatValue(object value, out float result)
        {
            result = 0f;
            if (value == null)
                return false;

            try
            {
                switch (value)
                {
                    case float f:
                        result = f;
                        return true;
                    case double d:
                        result = (float)d;
                        return true;
                    case decimal m:
                        result = (float)m;
                        return true;
                    default:
                        result = Convert.ToSingle(value);
                        return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 战旗升级酒馆：优先调用真实的游戏模式按钮，再回退到鼠标点击。
        /// </summary>
        private static IEnumerator<float> BgMouseTavernUp()
        {
            InputHook.Simulating = true;

            if (!TryReadBattlegroundsShopMetrics(out var beforeShopSignature, out var beforeGold, out var beforeTier, out var beforeFrozen))
            {
                beforeShopSignature = string.Empty;
                beforeGold = beforeTier = beforeFrozen = -1;
            }

            if (!TryActivateBattlegroundsGameModeButton(3, out var detail))
            {
                _coroutine.SetResult("FAIL:BG_TAVERN_UP:" + detail);
                yield break;
            }

            for (var retry = 0; retry < 15; retry++)
            {
                yield return 0.12f;
                if (!TryReadBattlegroundsShopMetrics(out var afterShopSignature, out var afterGold, out var afterTier, out var afterFrozen))
                    continue;

                if (afterTier > beforeTier
                    || (beforeTier >= 0 && afterGold >= 0 && afterGold < beforeGold)
                    || !string.Equals(beforeShopSignature, afterShopSignature, StringComparison.Ordinal))
                {
                    _coroutine.SetResult("OK:BG_TAVERN_UP:" + detail);
                    yield break;
                }
            }

            _coroutine.SetResult("FAIL:BG_TAVERN_UP:not_confirmed:" + detail);
        }

        /// <summary>
        /// 战旗打出手牌：从手牌拖到场上，可选带目标
        /// </summary>
        private static IEnumerator<float> BgMousePlayFromHand(int handEntityId, int targetEntityId, int position, int targetHeroSide, bool sourceUsesBoardDrop, bool isMagneticPlay)
        {
            InputHook.Simulating = true;

            var gsBeforePlay = GetGameState();
            var hasBeforeHand = TryReadFriendlyHandEntityIds(gsBeforePlay, out var beforeHandIds);
            var sourceCardId = ResolveEntityCardId(gsBeforePlay, handEntityId);
            var sourceZonePosition = ResolveEntityZonePosition(gsBeforePlay, handEntityId);

            bool grabbedViaAPI = false;
            string apiGrabMethod = "none";
            string apiGrabDetail = string.Empty;
            int apiHeldEntityId = 0;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (TryGrabCardViaAPI(
                        handEntityId,
                        sourceZonePosition,
                        out apiGrabMethod,
                        out apiHeldEntityId,
                        out apiGrabDetail))
                {
                    grabbedViaAPI = true;
                    break;
                }

                TryResetHeldCard();
                yield return 0.06f;
            }

            if (!grabbedViaAPI)
            {
                AppendActionTrace(
                    "BG_PLAY API grab failed expected=" + handEntityId
                    + " zonePos=" + sourceZonePosition
                    + " cardId=" + sourceCardId
                    + " detail=" + apiGrabDetail);
                _coroutine.SetResult("FAIL:BG_PLAY:grab_api_failed:" + handEntityId + ":" + (string.IsNullOrWhiteSpace(apiGrabDetail) ? "unknown" : apiGrabDetail));
                yield break;
            }

            AppendActionTrace(
                "BG_PLAY API grabbed card expected=" + handEntityId
                + " held=" + apiHeldEntityId
                + " zonePos=" + sourceZonePosition
                + " cardId=" + sourceCardId
                + " via=" + apiGrabMethod);

            int sx = 0;
            int sy = 0;
            if (!GameObjectFinder.GetEntityScreenPos(handEntityId, out sx, out sy))
            {
                var screenWidth = MouseSimulator.GetScreenWidth();
                var screenHeight = MouseSimulator.GetScreenHeight();
                sx = MouseSimulator.CurX > 0 ? MouseSimulator.CurX : Math.Max(0, screenWidth / 2);
                sy = MouseSimulator.CurY > 0 ? MouseSimulator.CurY : (screenHeight > 0 ? (int)(screenHeight * 0.84f) : 0);
            }

            foreach (var w in SmoothMove(sx, sy, 8, 0.008f)) yield return w;
            MouseSimulator.LeftDown();
            yield return 0.08f;

            int totalMinions = GetFriendlyMinionCount();
            int dx = 0;
            int dy = 0;
            bool usedMagneticDrop = false;
            bool targetConfirmationPending = false;

            if (targetEntityId > 0 && !sourceUsesBoardDrop)
            {
                bool gotTarget = false;
                int targetX = 0;
                int targetY = 0;
                for (int retry = 0; retry < 6 && !gotTarget; retry++)
                {
                    if (TryResolvePlayTargetScreenPos(targetEntityId, targetHeroSide, out targetX, out targetY))
                    {
                        gotTarget = true;
                        break;
                    }

                    if (retry < 5)
                        yield return 0.1f;
                }

                if (!gotTarget)
                {
                    MouseSimulator.LeftUp();
                    TryResetHeldCard();
                    _coroutine.SetResult("FAIL:BG_PLAY:target_pos:" + targetEntityId);
                    yield break;
                }

                foreach (var w in SmoothMove(targetX, targetY, 12, 0.008f)) yield return w;
                MouseSimulator.LeftUp();
                yield return 0.14f;

                targetConfirmationPending = IsPlayTargetConfirmationPending(handEntityId);
                if (targetConfirmationPending)
                {
                    for (int attempt = 0; attempt < 2 && targetConfirmationPending; attempt++)
                    {
                        if (attempt > 0)
                        {
                            bool refreshed = false;
                            for (int retry = 0; retry < 4 && !refreshed; retry++)
                            {
                                refreshed = TryResolvePlayTargetScreenPos(targetEntityId, targetHeroSide, out targetX, out targetY);
                                if (!refreshed && retry < 3)
                                    yield return 0.08f;
                            }
                        }

                        foreach (var w in SmoothMove(targetX, targetY, 8, 0.008f)) yield return w;
                        MouseSimulator.LeftDown();
                        yield return 0.04f;
                        MouseSimulator.LeftUp();

                        for (int retry = 0; retry < 5; retry++)
                        {
                            if (!IsPlayTargetConfirmationPending(handEntityId))
                            {
                                targetConfirmationPending = false;
                                break;
                            }

                            yield return 0.06f;
                        }
                    }
                }
            }
            else
            {
                if (sourceUsesBoardDrop && targetEntityId > 0 && isMagneticPlay)
                {
                    var gsBeforeDrop = GetGameState();
                    if (TryResolveBattlegroundsMagneticDropScreenPos(
                            gsBeforeDrop,
                            targetEntityId,
                            totalMinions,
                            out dx,
                            out dy,
                            out var targetZonePosition,
                            out var leftGapX,
                            out var leftGapY,
                            out var gapSource))
                    {
                        usedMagneticDrop = true;
                        AppendActionTrace(
                            "BG_PLAY magnetic_drop sourceEntityId=" + handEntityId
                            + " targetEntityId=" + targetEntityId
                            + " targetZonePosition=" + targetZonePosition
                            + " gapSource=" + gapSource
                            + " leftGap=" + leftGapX + "," + leftGapY
                            + " finalDrop=" + dx + "," + dy);
                    }
                    else
                    {
                        AppendActionTrace(
                            "BG_PLAY magnetic_drop fallback sourceEntityId=" + handEntityId
                            + " targetEntityId=" + targetEntityId
                            + " positionRef=" + position
                            + " totalMinions=" + totalMinions);
                    }
                }

                if (!usedMagneticDrop
                    && !TryResolveBattlegroundsBoardDropScreenPos(position, totalMinions, out dx, out dy))
                {
                    MouseSimulator.LeftUp();
                    TryResetHeldCard();
                    _coroutine.SetResult("FAIL:BG_PLAY:drop_pos");
                    yield break;
                }

                foreach (var w in SmoothMove(dx, dy, 12, 0.008f)) yield return w;
                yield return 0.04f;
                MouseSimulator.LeftUp();
                yield return 0.18f;

                if (targetEntityId > 0)
                {
                    bool sourceLeftHand = false;
                    for (int retry = 0; retry < 12; retry++)
                    {
                        var gs = GetGameState();
                        if (gs != null && TryIsEntityInFriendlyHand(gs, handEntityId, out var inHandAfterDrop))
                        {
                            sourceLeftHand = !inHandAfterDrop;
                            if (sourceLeftHand)
                                break;
                        }

                        yield return 0.05f;
                    }

                    if (!usedMagneticDrop)
                    {
                        bool targetConfirmed = false;
                        for (int attempt = 0; attempt < 2; attempt++)
                        {
                            bool gotTarget = false;
                            int tx = 0;
                            int ty = 0;
                            for (int retry = 0; retry < 6 && !gotTarget; retry++)
                            {
                                if (TryResolvePlayTargetScreenPos(targetEntityId, targetHeroSide, out tx, out ty))
                                {
                                    gotTarget = true;
                                    break;
                                }

                                if (retry < 5)
                                    yield return 0.1f;
                            }

                            if (!gotTarget)
                            {
                                _coroutine.SetResult("FAIL:BG_PLAY:target_pos:" + targetEntityId);
                                yield break;
                            }

                            foreach (var w in SmoothMove(tx, ty, 8, 0.008f)) yield return w;
                            MouseSimulator.LeftDown();
                            yield return 0.04f;
                            MouseSimulator.LeftUp();

                            for (int retry = 0; retry < 6; retry++)
                            {
                                if (!IsPlayTargetConfirmationPending(handEntityId))
                                {
                                    targetConfirmed = true;
                                    break;
                                }

                                yield return 0.06f;
                            }

                            if (targetConfirmed)
                                break;
                        }

                        targetConfirmationPending = !targetConfirmed
                            && sourceLeftHand
                            && IsPlayTargetConfirmationPending(handEntityId);
                    }
                }
            }

            yield return 0.25f;

            var gsAfterPlay = GetGameState();
            var hasAfterHand = TryReadFriendlyHandEntityIds(gsAfterPlay, out var afterHandIds);
            bool resolvedStillInHand = false;
            bool stillInHand = false;
            if (gsAfterPlay != null && TryIsEntityInFriendlyHand(gsAfterPlay, handEntityId, out stillInHand))
                resolvedStillInHand = true;

            if (resolvedStillInHand && stillInHand)
            {
                AppendActionTrace(
                    "BG_PLAY still in hand entityId=" + handEntityId
                    + " targetId=" + targetEntityId
                    + " usesBoardDrop=" + sourceUsesBoardDrop
                    + " cardId=" + sourceCardId);
                _coroutine.SetResult("FAIL:BG_PLAY:still_in_hand:" + handEntityId);
                yield break;
            }

            if (resolvedStillInHand
                && !stillInHand
                && hasBeforeHand
                && hasAfterHand
                && beforeHandIds.Contains(handEntityId))
            {
                var removed = beforeHandIds.Where(id => !afterHandIds.Contains(id)).ToList();
                if (removed.Count == 1 && removed[0] != handEntityId)
                {
                    AppendActionTrace(
                        "BG_PLAY source mismatch expected=" + handEntityId
                        + " actualRemoved=" + removed[0]
                        + " cardId=" + sourceCardId);
                    _coroutine.SetResult("FAIL:BG_PLAY:source_mismatch:" + handEntityId + ":" + removed[0]);
                    yield break;
                }

                if (removed.Count > 1 && !removed.Contains(handEntityId))
                {
                    AppendActionTrace(
                        "BG_PLAY source mismatch expected=" + handEntityId
                        + " actualRemoved=" + removed[0]
                        + " cardId=" + sourceCardId);
                    _coroutine.SetResult("FAIL:BG_PLAY:source_mismatch:" + handEntityId + ":" + removed[0]);
                    yield break;
                }
            }

            if (targetEntityId > 0 && !usedMagneticDrop)
            {
                if (!targetConfirmationPending)
                {
                    for (int retry = 0; retry < 4; retry++)
                    {
                        if (!IsPlayTargetConfirmationPending(handEntityId))
                            break;

                        targetConfirmationPending = true;
                        yield return 0.05f;
                    }
                }

                if (targetConfirmationPending)
                {
                    _coroutine.SetResult("FAIL:BG_PLAY:target_not_confirmed:" + handEntityId + ":" + targetEntityId);
                    yield break;
                }
            }

            _coroutine.SetResult("OK:BG_PLAY:" + handEntityId + ":api_grab");
        }

        private static IEnumerator<float> BgMouseHeroReroll(int choiceIndex)
        {
            InputHook.Simulating = true;

            if (!TryGetMulliganReadySnapshot(out var snapshot, out var snapshotDetail, allowLooseChoicePacket: true))
            {
                _coroutine.SetResult("FAIL:BG_HERO_REROLL:hero_pick_not_ready:" + snapshotDetail);
                yield break;
            }

            var beforeSignature = BuildMulliganSnapshotSignature(snapshot);
            string detail;
            object selectedCard = null;
            int selectedEntityId = 0;
            var hadInitialRerollButtonState = false;
            var initialRerollButtonActive = false;
            var initialRerollButtonEnabled = false;
            if (choiceIndex > 0)
            {
                if (!TryGetHeroPickCardByIndex(choiceIndex, out snapshot, out var card, out var entityId, out detail))
                {
                    _coroutine.SetResult("FAIL:BG_HERO_REROLL:" + detail);
                    yield break;
                }

                selectedCard = card;
                selectedEntityId = entityId;
                hadInitialRerollButtonState = TryReadHeroRerollButtonState(card, out initialRerollButtonActive, out initialRerollButtonEnabled, out _);

                var rerollSent = false;
                var entity = Invoke(card, "GetEntity") ?? GetFieldOrProp(card, "Entity") ?? GetFieldOrProp(card, "m_entity");
                if (entity == null && snapshot.GameState != null && entityId > 0)
                    entity = GetEntity(snapshot.GameState, entityId);

                var actor = Invoke(card, "GetActor");
                if (actor != null
                    && TryInvokeMethod(actor, "GetHeroRerollButton", Array.Empty<object>(), out var heroRerollButton, out _)
                    && heroRerollButton != null
                    && TryTriggerUiElement(heroRerollButton, out detail))
                {
                    rerollSent = true;
                }
                else if (entity != null && TryInvokeMethod(snapshot.MulliganManager, "RequestHeroReroll", new object[] { entity }, out _, out _))
                {
                    rerollSent = true;
                    detail = "RequestHeroReroll";
                }
                else
                {
                    if (actor != null && TryInvokeMethod(actor, "OnMulliganHeroRerollButtonReleased", new object[] { null }, out _, out _))
                    {
                        rerollSent = true;
                        detail = "OnMulliganHeroRerollButtonReleased";
                    }
                }

                if (!rerollSent)
                {
                    if (!TryGetHeroRerollButtonScreenPos(card, out var x, out var y, out detail))
                    {
                        _coroutine.SetResult("FAIL:BG_HERO_REROLL:button_pos:" + detail);
                        yield break;
                    }

                    foreach (var w in SmoothMove(x, y, 12, 0.012f)) yield return w;
                    yield return 0.05f;
                    MouseSimulator.LeftDown();
                    yield return 0.06f;
                    MouseSimulator.LeftUp();
                    detail = "mouse_click";
                }
            }
            else
            {
                if (!TryInvokeGlobalHeroRefresh(snapshot.MulliganManager, out detail))
                {
                    _coroutine.SetResult("FAIL:BG_HERO_REROLL:" + detail);
                    yield break;
                }
            }

            for (var retry = 0; retry < 50; retry++)
            {
                yield return 0.12f;
                if (!TryGetMulliganReadySnapshot(out var afterSnapshot, out _, allowLooseChoicePacket: true))
                {
                    _coroutine.SetResult("OK:BG_HERO_REROLL:" + (choiceIndex > 0 ? choiceIndex.ToString() : "all") + ":closed:" + detail);
                    yield break;
                }

                var afterSignature = BuildMulliganSnapshotSignature(afterSnapshot);
                if (!string.Equals(beforeSignature, afterSignature, StringComparison.Ordinal))
                {
                    _coroutine.SetResult("OK:BG_HERO_REROLL:" + (choiceIndex > 0 ? choiceIndex.ToString() : "all") + ":changed:" + detail);
                    yield break;
                }

                if (choiceIndex > 0
                    && selectedCard != null
                    && TryReadHeroRerollButtonState(selectedCard, out var rerollButtonActive, out var rerollButtonEnabled, out _)
                    && hadInitialRerollButtonState
                    && initialRerollButtonActive
                    && initialRerollButtonEnabled
                    && (!rerollButtonActive || !rerollButtonEnabled))
                {
                    _coroutine.SetResult("OK:BG_HERO_REROLL:" + choiceIndex + ":consumed:" + detail);
                    yield break;
                }

                if (choiceIndex > 0
                    && TryGetHeroPickCardByIndex(choiceIndex, out _, out _, out var afterEntityId, out _, allowLooseChoicePacket: true)
                    && selectedEntityId > 0
                    && afterEntityId > 0
                    && afterEntityId != selectedEntityId)
                {
                    _coroutine.SetResult("OK:BG_HERO_REROLL:" + choiceIndex + ":slot_changed:" + detail);
                    yield break;
                }
            }

            _coroutine.SetResult("FAIL:BG_HERO_REROLL:not_confirmed:" + (choiceIndex > 0 ? choiceIndex.ToString() : "all") + ":" + detail);
        }

        private static string BuildMulliganSnapshotSignature(MulliganReadySnapshot snapshot)
        {
            if (snapshot == null || snapshot.ChoiceParts == null || snapshot.ChoiceParts.Count == 0)
                return string.Empty;

            return string.Join(";", snapshot.ChoiceParts);
        }

        private static bool TryGetHeroPickCardByIndex(
            int oneBasedIndex,
            out MulliganReadySnapshot snapshot,
            out object card,
            out int entityId,
            out string detail,
            bool allowLooseChoicePacket = false)
        {
            snapshot = null;
            card = null;
            entityId = 0;
            detail = null;

            if (oneBasedIndex <= 0)
            {
                detail = "invalid_choice_index";
                return false;
            }

            if (!TryGetMulliganReadySnapshot(out snapshot, out detail, allowLooseChoicePacket))
                return false;

            if (oneBasedIndex > snapshot.StartingCards.Count)
            {
                detail = "choice_index_out_of_range:" + oneBasedIndex + "/" + snapshot.StartingCards.Count;
                return false;
            }

            card = snapshot.StartingCards[oneBasedIndex - 1];
            entityId = ResolveEntityId(Invoke(card, "GetEntity") ?? GetFieldOrProp(card, "Entity") ?? GetFieldOrProp(card, "m_entity"));
            if (card == null || entityId <= 0)
            {
                detail = "hero_card_entity_missing";
                return false;
            }

            detail = "ok";
            return true;
        }

        private static bool TryResolveBattlegroundsBoardDropScreenPos(int position, int totalMinions, out int x, out int y)
        {
            x = y = 0;
            int boardX = 0, boardY = 0;
            int releaseX = 0, releaseY = 0;
            var hasBoard = GameObjectFinder.GetBoardDropZoneScreenPos(position, totalMinions, out boardX, out boardY);
            var hasRelease = GameObjectFinder.GetPlayReleaseScreenPos(out releaseX, out releaseY);
            var screenWidth = MouseSimulator.GetScreenWidth();
            var screenHeight = MouseSimulator.GetScreenHeight();
            if (screenWidth <= 0 || screenHeight <= 0)
                return false;

            x = hasBoard ? boardX : (hasRelease ? releaseX : screenWidth / 2);
            y = hasBoard ? boardY : (hasRelease ? releaseY : (int)(screenHeight * 0.72f));
            if (hasRelease)
                y = Math.Max(y, releaseY);

            y = Math.Max(y, (int)(screenHeight * 0.68f));
            return true;
        }

        private static bool TryResolveBattlegroundsBuyDropScreenPos(out int x, out int y)
        {
            x = y = 0;

            if (GameObjectFinder.GetHeroScreenPos(true, out x, out y))
            {
                var screenHeight = MouseSimulator.GetScreenHeight();
                if (screenHeight > 0)
                    y = Math.Max(0, y - (int)(screenHeight * 0.035f));
                return true;
            }

            var screenWidth = MouseSimulator.GetScreenWidth();
            var fallbackHeight = MouseSimulator.GetScreenHeight();
            if (screenWidth <= 0 || fallbackHeight <= 0)
                return false;

            x = screenWidth / 2;
            y = (int)(fallbackHeight * 0.82f);
            return true;
        }

        private static bool TryGetCurrentSelectedChooseOneEntityId(object mulliganMgr, out int entityId)
        {
            entityId = 0;
            if (mulliganMgr == null)
                return false;

            if (!TryInvokeMethod(mulliganMgr, "GetCurrentSelectedChooseOneEntity", Array.Empty<object>(), out var entity, out _)
                || entity == null)
            {
                return false;
            }

            entityId = ResolveEntityId(entity);
            return entityId > 0;
        }

        private static bool TryGetHeroPickCardClickPos(
            MulliganReadySnapshot snapshot,
            object card,
            int entityId,
            int cardIndex,
            out int x,
            out int y,
            out string detail)
        {
            x = y = 0;
            detail = null;

            if (TryResolveHeroPickVisualTarget(card, out var visualTarget, out var actor, out detail))
            {
                if (visualTarget != null && GameObjectFinder.GetObjectScreenPos(visualTarget, out x, out y))
                {
                    detail = "visual_target";
                    return true;
                }

                if (actor != null && GameObjectFinder.GetObjectScreenPos(actor, out x, out y))
                {
                    detail = "actor_target";
                    return true;
                }
            }

            return TryGetMulliganCardClickPos(entityId, cardIndex, snapshot?.StartingCards?.Count ?? 0, out x, out y, out detail);
        }

        private static bool TryResolveHeroPickVisualTarget(object card, out object visualTarget, out object actor, out string detail)
        {
            visualTarget = null;
            actor = null;
            detail = "card_null";
            if (card == null)
                return false;

            actor = Invoke(card, "GetActor")
                ?? GetFieldOrProp(card, "Actor")
                ?? GetFieldOrProp(card, "m_actor");
            if (actor == null)
            {
                detail = "actor_missing";
                return false;
            }

            var actorGameObject = GetFieldOrProp(actor, "gameObject");
            var actorTransform = actorGameObject != null ? GetFieldOrProp(actorGameObject, "transform") : GetFieldOrProp(actor, "transform");
            var parent = actorTransform != null ? GetFieldOrProp(actorTransform, "parent") : null;
            visualTarget = GetFieldOrProp(parent, "gameObject")
                ?? actorGameObject
                ?? actor;
            detail = "ok";
            return true;
        }

        private static bool TryReadHeroPickSelectionState(
            MulliganReadySnapshot snapshot,
            object card,
            int entityId,
            out bool isSelected,
            out bool isVisualSelected,
            out bool isConfirmHighlighted,
            out string detail)
        {
            isSelected = false;
            isVisualSelected = false;
            isConfirmHighlighted = false;
            detail = "hero_pick_state_unavailable";
            if (snapshot == null || snapshot.MulliganManager == null || entityId <= 0)
                return false;

            var selectedEntityId = 0;
            TryGetCurrentSelectedChooseOneEntityId(snapshot.MulliganManager, out selectedEntityId);

            var marked = false;
            var markedReady = false;
            if (snapshot.CardIndexByEntityId.TryGetValue(entityId, out var cardIndex))
                markedReady = TryReadMulliganMarkedState(snapshot.MulliganManager, cardIndex, out marked, out _);

            var buttonActive = TryReadHeroPickConfirmButtonState(snapshot.MulliganManager, out var buttonEnabled);
            if (TryResolveHeroPickActor(card, out var actor))
            {
                isVisualSelected = TryReadGameObjectActive(GetFieldOrProp(actor, "m_fullSelectionHighlight"));
                isConfirmHighlighted = TryReadGameObjectActive(GetFieldOrProp(actor, "m_confirmSelectionHighlight"));
            }

            isSelected = isVisualSelected
                || (selectedEntityId == entityId && (!markedReady || marked) && buttonActive);

            detail = "selectedEntity=" + selectedEntityId
                + ";marked=" + (markedReady ? (marked ? "1" : "0") : "na")
                + ";buttonActive=" + (buttonActive ? "1" : "0")
                + ";buttonEnabled=" + (buttonEnabled ? "1" : "0")
                + ";visualSelected=" + (isVisualSelected ? "1" : "0")
                + ";visualConfirm=" + (isConfirmHighlighted ? "1" : "0");
            return true;
        }

        private static bool TryResolveHeroPickActor(object card, out object actor)
        {
            actor = null;
            if (card == null)
                return false;

            actor = Invoke(card, "GetActor")
                ?? GetFieldOrProp(card, "Actor")
                ?? GetFieldOrProp(card, "m_actor");
            return actor != null;
        }

        private static bool TryReadHeroPickConfirmButtonState(object mulliganMgr, out bool enabled)
        {
            enabled = false;
            if (mulliganMgr == null)
                return false;

            var button = Invoke(mulliganMgr, "GetMulliganButton")
                ?? GetFieldOrProp(mulliganMgr, "mulliganButton")
                ?? GetFieldOrProp(mulliganMgr, "mulliganButtonWidget");
            if (button == null)
                return false;

            enabled = TryReadEnabledState(button);
            return TryReadGameObjectActive(button);
        }

        private static bool TryReadEnabledState(object target)
        {
            if (target == null)
                return false;

            var direct = GetFieldOrProp(target, "m_enabled")
                ?? GetFieldOrProp(target, "enabled")
                ?? GetFieldOrProp(target, "Active");
            if (direct != null)
                return ReadBoolValue(direct);

            if (TryInvokeMethod(target, "GetEnabled", Array.Empty<object>(), out var enabledObj))
                return ReadBoolValue(enabledObj);

            if (TryInvokeMethod(target, "IsEnabled", Array.Empty<object>(), out enabledObj))
                return ReadBoolValue(enabledObj);

            return false;
        }

        private static bool TryReadGameObjectActive(object target)
        {
            if (target == null)
                return false;

            var gameObject = GetFieldOrProp(target, "gameObject") ?? target;
            var active = GetFieldOrProp(gameObject, "activeSelf")
                ?? GetFieldOrProp(gameObject, "activeInHierarchy");
            if (active != null)
                return ReadBoolValue(active);

            if (TryInvokeMethod(gameObject, "get_activeSelf", Array.Empty<object>(), out var activeObj))
                return ReadBoolValue(activeObj);

            if (TryInvokeMethod(gameObject, "get_activeInHierarchy", Array.Empty<object>(), out activeObj))
                return ReadBoolValue(activeObj);

            return false;
        }

        private static bool TrySelectHeroPickCardViaApi(MulliganReadySnapshot snapshot, object card, int entityId, out string detail)
        {
            detail = "toggle_failed";
            if (snapshot == null || snapshot.MulliganManager == null || card == null || entityId <= 0)
                return false;

            if (TryInvokeMethod(snapshot.MulliganManager, "ToggleHoldState", new object[] { card }, out _, out _))
            {
                detail = "ToggleHoldState(Card)";
                return true;
            }

            var entity = snapshot.GameState != null ? GetEntity(snapshot.GameState, entityId) : null;
            if (entity != null)
            {
                var inputMgr = GetSingleton(_inputMgrType);
                if (inputMgr != null
                    && TryInvokeMethod(inputMgr, "DoNetworkResponse", new object[] { entity }, out var responseResult, out _)
                    && (!(responseResult is bool ok) || ok))
                {
                    detail = "InputManager.DoNetworkResponse";
                    return true;
                }
            }

            return false;
        }

        private static bool TryTriggerUiElement(object source, out string detail)
        {
            detail = "ui_element_missing";
            if (!TryResolvePegUiElement(source, out var uiElement) || uiElement == null)
                return false;

            TryInvokeMethod(uiElement, "SetEnabled", new object[] { true, false }, out _, out _);
            TryInvokeMethod(uiElement, "SetEnabled", new object[] { true }, out _, out _);

            var methods = new List<string>();
            if (TryInvokeParameterlessMethods(uiElement, new[] { "TriggerPress" }, out var pressMethod))
            {
                methods.Add(pressMethod);
                Thread.Sleep(40);
            }

            if (TryInvokeParameterlessMethods(uiElement, new[] { "TriggerRelease", "TriggerTap" }, out var releaseMethod))
            {
                methods.Add(releaseMethod);
            }

            if (methods.Count == 0)
            {
                detail = "ui_trigger_methods_missing:" + DescribeObjectType(uiElement);
                return false;
            }

            detail = DescribeObjectType(uiElement) + "." + string.Join("+", methods);
            return true;
        }

        private static bool TryResolvePegUiElement(object source, out object uiElement)
        {
            uiElement = null;
            if (source == null || _asm == null)
                return false;

            var pegUiElementType = _asm.GetType("PegUIElement");
            if (pegUiElementType == null)
                return false;

            var candidates = new List<object>();
            AddDistinctObjectCandidate(candidates, source);
            AddDistinctObjectCandidate(candidates, GetFieldOrProp(source, "gameObject"));
            AddDistinctObjectCandidate(candidates, GetFieldOrProp(source, "transform"));

            foreach (var candidate in candidates)
            {
                if (candidate == null)
                    continue;

                if (pegUiElementType.IsInstanceOfType(candidate))
                {
                    uiElement = candidate;
                    return true;
                }

                if (TryResolveComponentByType(candidate, pegUiElementType, out uiElement))
                    return true;

                if (TryFindNamedDescendant(candidate, out var namedCandidate, "button", "refresh", "reroll", "freeze", "upgrade", "confirm")
                    && TryResolveComponentByType(namedCandidate, pegUiElementType, out uiElement))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveComponentByType(object source, Type componentType, out object component)
        {
            component = null;
            if (source == null || componentType == null)
                return false;

            var roots = new List<object>();
            AddDistinctObjectCandidate(roots, source);
            AddDistinctObjectCandidate(roots, GetFieldOrProp(source, "gameObject"));

            foreach (var root in roots)
            {
                if (root == null)
                    continue;

                if (componentType.IsInstanceOfType(root))
                {
                    component = root;
                    return true;
                }

                if (TryInvokeMethod(root, "GetComponent", new object[] { componentType }, out var found, out _) && found != null)
                {
                    component = found;
                    return true;
                }

                if (TryInvokeMethod(root, "GetComponentInChildren", new object[] { componentType }, out found, out _) && found != null)
                {
                    component = found;
                    return true;
                }

                if (TryInvokeMethod(root, "GetComponentInChildren", new object[] { componentType, true }, out found, out _) && found != null)
                {
                    component = found;
                    return true;
                }

                if (TryInvokeMethod(root, "GetComponentInParent", new object[] { componentType }, out found, out _) && found != null)
                {
                    component = found;
                    return true;
                }

                if (TryInvokeMethod(root, "GetComponentsInChildren", new object[] { componentType }, out found, out _)
                    && TryGetFirstEnumerableValue(found, out component))
                {
                    return true;
                }

                if (TryInvokeMethod(root, "GetComponentsInChildren", new object[] { componentType, true }, out found, out _)
                    && TryGetFirstEnumerableValue(found, out component))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetFirstEnumerableValue(object source, out object value)
        {
            value = null;
            if (!(source is IEnumerable enumerable))
                return false;

            foreach (var item in enumerable)
            {
                if (item == null)
                    continue;

                value = item;
                return true;
            }

            return false;
        }

        private static bool TryInvokeGlobalHeroRefresh(object mulliganMgr, out string detail)
        {
            detail = "mulligan_manager_missing";
            if (mulliganMgr == null)
                return false;

            if (TryInvokeMethod(mulliganMgr, "OnMulliganRefreshButtonReleased", new object[] { null }, out _, out _))
            {
                detail = "OnMulliganRefreshButtonReleased";
                return true;
            }

            if (TryInvokeMethod(mulliganMgr, "GetMulliganRefreshButton", Array.Empty<object>(), out var refreshButton, out _)
                && refreshButton != null)
            {
                if (TryTriggerUiElement(refreshButton, out detail))
                    return true;

                if (GameObjectFinder.GetObjectScreenPos(refreshButton, out var x, out var y))
                {
                    TryClickScreenPos(x, y);
                    detail = "mouse_refresh_button";
                    return true;
                }
            }

            detail = "refresh_button_missing";
            return false;
        }

        private static bool TryGetHeroRerollButtonScreenPos(object card, out int x, out int y, out string detail)
        {
            x = y = 0;
            detail = "hero_card_missing";
            if (card == null)
                return false;

            var actor = Invoke(card, "GetActor") ?? GetFieldOrProp(card, "Actor") ?? card;
            var explicitCandidates = new[]
            {
                TryInvokeMethod(actor, "GetHeroRerollButton", Array.Empty<object>(), out var rerollButton, out _) ? rerollButton : null,
                GetFieldOrProp(actor, "m_heroRerollButton"),
                GetFieldOrProp(actor, "heroRerollButton"),
                GetFieldOrProp(actor, "m_rerollButtonReference"),
                GetFieldOrProp(actor, "m_rerollButton")
            };

            foreach (var candidate in explicitCandidates)
            {
                if (candidate != null && GameObjectFinder.GetObjectScreenPos(candidate, out x, out y))
                {
                    detail = "explicit_field";
                    return true;
                }
            }

            if (TryFindNamedDescendant(card, out var namedCandidate, "refresh", "reroll"))
            {
                if (GameObjectFinder.GetObjectScreenPos(namedCandidate, out x, out y))
                {
                    detail = "named_descendant";
                    return true;
                }
            }

            if (TryFindNamedDescendant(actor, out var actorNamedCandidate, "reroll", "refresh"))
            {
                if (GameObjectFinder.GetObjectScreenPos(actorNamedCandidate, out x, out y))
                {
                    detail = "named_descendant";
                    return true;
                }
            }

            if (!GameObjectFinder.GetObjectScreenPos(actor, out var cx, out var cy))
            {
                detail = "hero_card_pos_missing";
                return false;
            }

            var screenWidth = MouseSimulator.GetScreenWidth();
            var screenHeight = MouseSimulator.GetScreenHeight();
            x = cx - (int)(screenWidth * 0.055f);
            y = cy - (int)(screenHeight * 0.06f);
            detail = "fallback_offset";
            return true;
        }

        private static bool TryReadHeroRerollButtonState(object card, out bool active, out bool enabled, out string detail)
        {
            active = false;
            enabled = false;
            detail = "hero_card_missing";
            if (card == null)
                return false;

            var actor = Invoke(card, "GetActor") ?? GetFieldOrProp(card, "Actor") ?? card;
            var candidates = new[]
            {
                TryInvokeMethod(actor, "GetHeroRerollButton", Array.Empty<object>(), out var rerollButton, out _) ? rerollButton : null,
                GetFieldOrProp(actor, "m_heroRerollButton"),
                GetFieldOrProp(actor, "heroRerollButton"),
                GetFieldOrProp(actor, "m_rerollButtonReference"),
                GetFieldOrProp(actor, "m_rerollButton")
            };

            foreach (var candidate in candidates)
            {
                if (candidate == null)
                    continue;

                active = TryReadGameObjectActive(candidate);
                enabled = TryReadEnabledState(candidate);
                detail = "explicit_field";
                return true;
            }

            detail = "refresh_button_missing";
            return false;
        }

        private static bool TryGetBattlegroundsShop()
        {
            return TryGetBattlegroundsShop(out _);
        }

        private static bool TryGetBattlegroundsShop(out object shop)
        {
            shop = null;
            if (_asm == null)
                return false;

            var type = _asm.GetType("TB_BaconShop");
            if (type == null)
                return false;

            shop = GetSingleton(type) ?? InvokeStatic(type, "Get");
            return shop != null;
        }

        private static bool TryGetBattlegroundsGameModeButtonCardFromZones(int slot, out object card, out object entity, out string detail)
        {
            card = null;
            entity = null;
            detail = "zone_mgr_not_found";

            if (_asm == null)
                return false;

            var zoneMgrType = _asm.GetType("ZoneMgr");
            var playerSideType = _asm.GetType("Player+Side");
            var zoneGameModeButtonType = _asm.GetType("ZoneGameModeButton");
            if (zoneMgrType == null || playerSideType == null || zoneGameModeButtonType == null)
            {
                detail = "zone_types_missing";
                return false;
            }

            var zoneMgr = GetSingleton(zoneMgrType);
            if (zoneMgr == null)
            {
                detail = "zone_mgr_singleton_missing";
                return false;
            }

            object friendlySide;
            try
            {
                friendlySide = Enum.Parse(playerSideType, "FRIENDLY", ignoreCase: true);
            }
            catch
            {
                detail = "friendly_side_enum_missing";
                return false;
            }

            if (!TryInvokeMethod(zoneMgr, "FindZonesForSide", new[] { friendlySide }, out var zones, out _)
                || !(zones is IEnumerable zoneEnumerable))
            {
                detail = "find_zones_for_side_failed";
                return false;
            }

            foreach (var zone in zoneEnumerable)
            {
                if (zone == null || !zoneGameModeButtonType.IsInstanceOfType(zone))
                    continue;

                var buttonSlot = GetIntFieldOrProp(zone, "m_ButtonSlot");
                if (buttonSlot <= 0)
                    buttonSlot = GetIntFieldOrProp(zone, "ButtonSlot");
                if (buttonSlot != slot)
                    continue;

                card = Invoke(zone, "GetFirstCard") ?? GetFieldOrProp(zone, "m_cards");
                if (card is IEnumerable cardEnumerable)
                    TryGetFirstEnumerableValue(cardEnumerable, out card);

                if (card == null)
                {
                    detail = "zone_button_card_missing:" + slot;
                    return false;
                }

                entity = Invoke(card, "GetEntity") ?? GetFieldOrProp(card, "Entity") ?? GetFieldOrProp(card, "m_entity");
                if (entity == null)
                {
                    detail = "zone_button_entity_missing:" + slot;
                    return false;
                }

                detail = "zone_button";
                return true;
            }

            detail = "zone_button_not_found:" + slot;
            return false;
        }

        private static bool TryGetBattlegroundsGameModeButtonCard(int slot, out object card, out object entity, out string detail)
        {
            card = null;
            entity = null;
            detail = "shop_not_found";

            if (TryGetBattlegroundsShop(out var shop) && shop != null)
            {
                switch (slot)
                {
                    case 1:
                        card = Invoke(shop, "GetFreezeButtonCard");
                        break;
                    case 2:
                        card = Invoke(shop, "GetRefreshButtonCard");
                        break;
                    case 3:
                        card = Invoke(shop, "GetTavernUpgradeButtonCard");
                        break;
                }

                if (card == null && TryInvokeMethod(shop, "GetGameModeButtonBySlot", new object[] { slot }, out var fallbackCard, out _))
                {
                    card = fallbackCard;
                }
            }

            if (card == null && TryGetBattlegroundsGameModeButtonCardFromZones(slot, out card, out entity, out var zoneDetail))
            {
                detail = zoneDetail;
                return true;
            }

            if (card == null)
            {
                detail = detail == "shop_not_found" ? "button_card_missing:" + slot : detail;
                return false;
            }

            entity = Invoke(card, "GetEntity") ?? GetFieldOrProp(card, "Entity") ?? GetFieldOrProp(card, "m_entity");
            if (entity == null)
            {
                detail = "button_entity_missing:" + slot;
                return false;
            }

            detail = "ok";
            return true;
        }

        private static bool TryActivateEntityViaInputManager(object card, object entity, out string detail)
        {
            detail = "input_manager_missing";
            var inputMgr = GetSingleton(_inputMgrType);
            if (inputMgr == null || entity == null)
                return false;

            if (TryInvokeBoolMethod(inputMgr, "PermitDecisionMakingInput", out var permitInput) && !permitInput)
            {
                detail = "input_not_ready";
                return false;
            }

            if (TryInvokeMethod(inputMgr, "DoNetworkResponse", new object[] { entity }, out var responseResult, out _)
                && (!(responseResult is bool ok) || ok))
            {
                detail = "DoNetworkResponse";
                return true;
            }

            if (TryInvokeMethod(inputMgr, "HandleClickOnCardInBattlefield", new object[] { entity }, out _, out _))
            {
                detail = "HandleClickOnCardInBattlefield";
                return true;
            }

            if (TryInvokeMethod(inputMgr, "HandleClickOnCardInBattlefield", new object[] { entity, true }, out _, out _))
            {
                detail = "HandleClickOnCardInBattlefield(bool)";
                return true;
            }

            var cardGameObject = GetFieldOrProp(card, "gameObject");
            if (cardGameObject != null
                && TryInvokeMethod(inputMgr, "HandleClickOnCard", new object[] { cardGameObject, true }, out _, out _))
            {
                detail = "HandleClickOnCard";
                return true;
            }

            if (cardGameObject != null
                && TryInvokeMethod(inputMgr, "HandleClickOnCard", new object[] { cardGameObject, false }, out _, out _))
            {
                detail = "HandleClickOnCard(false)";
                return true;
            }

            detail = "input_methods_failed";
            return false;
        }

        private static bool TryClickScreenPos(int x, int y)
        {
            if (x <= 0 || y <= 0)
                return false;

            InputHook.Simulating = true;

            foreach (var _ in SmoothMove(x, y, 10, 0.01f))
            {
            }

            MouseSimulator.MoveTo(x, y);
            Thread.Sleep(80);
            MouseSimulator.LeftDown();
            Thread.Sleep(100);
            MouseSimulator.LeftUp();
            return true;
        }

        private static bool TryActivateBattlegroundsGameModeButton(int slot, out string detail)
        {
            detail = "button_not_found";
            if (!TryGetBattlegroundsGameModeButtonCard(slot, out var card, out var entity, out detail))
                return false;

            TryInvokeMethod(card, "SetInputEnabled", new object[] { true }, out _, out _);

            if (TryTriggerUiElement(card, out detail))
                return true;

            if (TryActivateEntityViaInputManager(card, entity, out detail))
                return true;

            var entityId = ResolveEntityId(entity);
            if (entityId > 0 && GameObjectFinder.GetEntityScreenPos(entityId, out var x, out var y) && TryClickScreenPos(x, y))
            {
                detail = "mouse_entity_click";
                return true;
            }

            if (card != null && GameObjectFinder.GetObjectScreenPos(card, out x, out y) && TryClickScreenPos(x, y))
            {
                detail = "mouse_card_click";
                return true;
            }

            detail = "button_click_failed";
            return false;
        }

        private static bool TryReadBattlegroundsShopMetrics(out string shopSignature, out int gold, out int tavernTier, out int frozenState)
        {
            shopSignature = string.Empty;
            gold = tavernTier = frozenState = -1;

            var gameState = GetGameState();
            if (gameState == null)
                return false;

            var friendly = Invoke(gameState, "GetFriendlySidePlayer") ?? Invoke(gameState, "GetFriendlyPlayer");
            var opposing = Invoke(gameState, "GetOpposingSidePlayer") ?? Invoke(gameState, "GetOpposingPlayer");
            if (friendly == null)
                return false;

            var ctx = ReflectionContext.Instance;
            if (!ctx.Init())
                return false;

            var resources = ctx.GetTagValue(friendly, "RESOURCES");
            var used = ctx.GetTagValue(friendly, "RESOURCES_USED");
            var temp = ctx.GetTagValue(friendly, "TEMP_RESOURCES");
            gold = Math.Max(0, resources - used + temp);
            tavernTier = ctx.GetTagValue(friendly, "PLAYER_TECH_LEVEL");
            frozenState = ctx.GetTagValue(friendly, "BACON_SHOP_IS_FROZEN");

            var shopParts = new List<string>();
            var zone = opposing != null ? (Invoke(opposing, "GetBattlefieldZone") ?? Invoke(opposing, "GetPlayZone")) : null;
            var cards = zone != null ? (Invoke(zone, "GetCards") as IEnumerable ?? GetFieldOrProp(zone, "m_cards") as IEnumerable) : null;
            if (cards != null)
            {
                foreach (var rawCard in cards)
                {
                    if (rawCard == null)
                        continue;

                    var cardEntity = Invoke(rawCard, "GetEntity") ?? GetFieldOrProp(rawCard, "Entity") ?? GetFieldOrProp(rawCard, "m_entity");
                    var entityId = ResolveEntityId(cardEntity);
                    if (entityId <= 0)
                        continue;

                    shopParts.Add(entityId
                        + ":" + ResolveEntityCardId(gameState, entityId)
                        + ":" + ResolveEntityZonePosition(gameState, entityId));
                }
            }

            shopSignature = gold
                + "|" + tavernTier
                + "|" + frozenState
                + "|" + string.Join(";", shopParts.OrderBy(part => part, StringComparer.Ordinal).ToArray());
            return true;
        }

        private static bool TryFindNamedDescendant(object source, out object found, params string[] nameHints)
        {
            found = null;
            if (source == null || nameHints == null || nameHints.Length == 0)
                return false;

            var root = GetFieldOrProp(source, "gameObject") ?? source;
            var rootTransform = GetFieldOrProp(root, "transform");
            if (rootTransform == null)
                return false;

            var queue = new Queue<object>();
            queue.Enqueue(rootTransform);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == null)
                    continue;

                var name = GetFieldOrProp(current, "name")?.ToString()
                    ?? GetFieldOrProp(GetFieldOrProp(current, "gameObject"), "name")?.ToString()
                    ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name)
                    && nameHints.Any(hint => name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    found = GetFieldOrProp(current, "gameObject") ?? current;
                    return true;
                }

                var childCount = GetIntFieldOrProp(current, "childCount");
                if (childCount <= 0)
                    continue;

                for (var i = 0; i < childCount; i++)
                {
                    if (TryInvokeMethod(current, "GetChild", new object[] { i }, out var child, out _)
                        && child != null)
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 战旗刷新商店：优先调用真实的游戏模式按钮，再回退到鼠标点击。
        /// </summary>
        private static IEnumerator<float> BgMouseReroll()
        {
            InputHook.Simulating = true;

            if (!TryReadBattlegroundsShopMetrics(out var beforeShopSignature, out var beforeGold, out var beforeTier, out var beforeFrozen))
            {
                beforeShopSignature = string.Empty;
                beforeGold = beforeTier = beforeFrozen = -1;
            }

            if (!TryActivateBattlegroundsGameModeButton(2, out var detail))
            {
                _coroutine.SetResult("FAIL:BG_REROLL:" + detail);
                yield break;
            }

            for (var retry = 0; retry < 18; retry++)
            {
                yield return 0.12f;
                if (!TryReadBattlegroundsShopMetrics(out var afterShopSignature, out var afterGold, out var afterTier, out var afterFrozen))
                    continue;

                if (!string.Equals(beforeShopSignature, afterShopSignature, StringComparison.Ordinal)
                    || (beforeGold >= 0 && afterGold >= 0 && afterGold < beforeGold))
                {
                    _coroutine.SetResult("OK:BG_REROLL:" + detail);
                    yield break;
                }
            }

            _coroutine.SetResult("FAIL:BG_REROLL:not_confirmed:" + detail);
        }

        /// <summary>
        /// 战旗冻结商店：优先调用真实的游戏模式按钮，再回退到鼠标点击。
        /// </summary>
        private static IEnumerator<float> BgMouseFreeze()
        {
            InputHook.Simulating = true;

            if (!TryReadBattlegroundsShopMetrics(out var beforeShopSignature, out var beforeGold, out var beforeTier, out var beforeFrozen))
            {
                beforeShopSignature = string.Empty;
                beforeGold = beforeTier = beforeFrozen = -1;
            }

            if (!TryActivateBattlegroundsGameModeButton(1, out var detail))
            {
                _coroutine.SetResult("FAIL:BG_FREEZE:" + detail);
                yield break;
            }

            for (var retry = 0; retry < 15; retry++)
            {
                yield return 0.12f;
                if (!TryReadBattlegroundsShopMetrics(out var afterShopSignature, out var afterGold, out var afterTier, out var afterFrozen))
                    continue;

                if (afterFrozen != beforeFrozen
                    || !string.Equals(beforeShopSignature, afterShopSignature, StringComparison.Ordinal))
                {
                    _coroutine.SetResult("OK:BG_FREEZE:" + detail);
                    yield break;
                }
            }

            _coroutine.SetResult("FAIL:BG_FREEZE:not_confirmed:" + detail);
        }

        #endregion

        #region 探测工具

        /// <summary>
        /// 探测手牌卡牌 GameObject 上的组件、Renderer、Bounds 等信息，
        /// 用于确认能否获取卡牌边框坐标。
        /// </summary>
        private static string ProbeHandCardComponents()
        {
            try
            {
                if (!EnsureTypes()) return "PROBE:no_asm";

                var gs = GetGameState();
                if (gs == null) return "PROBE:no_gamestate";

                var friendly = Invoke(gs, "GetFriendlySidePlayer")
                    ?? Invoke(gs, "GetFriendlyPlayer")
                    ?? Invoke(gs, "GetLocalPlayer");
                if (friendly == null) return "PROBE:no_player";

                var handZone = Invoke(friendly, "GetHandZone")
                    ?? Invoke(friendly, "GetHand")
                    ?? GetFieldOrProp(friendly, "m_handZone")
                    ?? GetFieldOrProp(friendly, "HandZone");
                if (handZone == null) return "PROBE:no_handzone";

                var cards = Invoke(handZone, "GetCards") as IEnumerable
                    ?? GetFieldOrProp(handZone, "Cards") as IEnumerable
                    ?? GetFieldOrProp(handZone, "m_cards") as IEnumerable
                    ?? handZone as IEnumerable;
                if (cards == null) return "PROBE:no_cards";

                var results = new System.Collections.Generic.List<string>();
                int cardIndex = 0;
                foreach (var card in cards)
                {
                    if (card == null) continue;
                    if (cardIndex >= 3) break; // 只探测前3张牌

                    var entity = Invoke(card, "GetEntity");
                    var entityId = entity != null ? GetEntityIdSafe(entity) : 0;
                    var actor = Invoke(card, "GetActor");
                    var go = actor != null
                        ? GetFieldOrProp(actor, "gameObject")
                        : GetFieldOrProp(card, "gameObject");
                    if (go == null) go = GetFieldOrProp(card, "gameObject");

                    if (go == null)
                    {
                        results.Add($"card[{cardIndex}] eid={entityId} go=null");
                        cardIndex++;
                        continue;
                    }

                    // 列出 GameObject 上所有组件
                    var components = new System.Collections.Generic.List<string>();
                    try
                    {
                        var getComps = go.GetType().GetMethod("GetComponents",
                            new[] { typeof(Type) });
                        if (getComps != null)
                        {
                            var componentType = typeof(UnityEngine.Component);
                            var allComps = getComps.Invoke(go, new object[] { componentType }) as Array;
                            if (allComps != null)
                            {
                                foreach (var comp in allComps)
                                {
                                    if (comp != null)
                                        components.Add(comp.GetType().Name);
                                }
                            }
                        }
                    }
                    catch { components.Add("enum_error"); }

                    // 尝试获取 Renderer 和 Bounds
                    string boundsInfo = "no_renderer";
                    try
                    {
                        var rendererType = typeof(UnityEngine.Renderer);
                        var getComp = go.GetType().GetMethod("GetComponentInChildren",
                            new[] { typeof(Type) });
                        object renderer = null;
                        if (getComp != null)
                            renderer = getComp.Invoke(go, new object[] { rendererType });

                        if (renderer != null)
                        {
                            var boundsProp = renderer.GetType().GetProperty("bounds");
                            if (boundsProp != null)
                            {
                                var bounds = (UnityEngine.Bounds)boundsProp.GetValue(renderer);
                                var min = bounds.min;
                                var max = bounds.max;
                                var center = bounds.center;
                                boundsInfo = $"bounds(min={min.x:F2},{min.y:F2},{min.z:F2} max={max.x:F2},{max.y:F2},{max.z:F2} center={center.x:F2},{center.y:F2},{center.z:F2})";

                                // 同时输出中心点 Transform 坐标做对比
                                var transform = GetFieldOrProp(go, "transform");
                                var pos = transform != null ? GetFieldOrProp(transform, "position") : null;
                                if (pos != null)
                                {
                                    var px = GetFloatField(pos, "x");
                                    var py = GetFloatField(pos, "y");
                                    var pz = GetFloatField(pos, "z");
                                    boundsInfo += $" transform({px:F2},{py:F2},{pz:F2})";
                                }
                            }
                            else
                            {
                                boundsInfo = "renderer_no_bounds_prop(" + renderer.GetType().Name + ")";
                            }
                        }
                    }
                    catch (Exception ex) { boundsInfo = "bounds_error:" + ex.GetType().Name; }

                    // 尝试获取 Collider（纯反射，避免直接引用 PhysicsModule）
                    string colliderInfo = "no_collider";
                    try
                    {
                        var colliderTypeName = "UnityEngine.Collider, UnityEngine.PhysicsModule";
                        var colliderType = Type.GetType(colliderTypeName)
                            ?? AppDomain.CurrentDomain.GetAssemblies()
                                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                                .FirstOrDefault(t => t.FullName == "UnityEngine.Collider");
                        if (colliderType != null)
                        {
                            var getComp = go.GetType().GetMethod("GetComponentInChildren",
                                new[] { typeof(Type) });
                            object collider = null;
                            if (getComp != null)
                                collider = getComp.Invoke(go, new object[] { colliderType });
                            if (collider != null)
                            {
                                var cBoundsProp = collider.GetType().GetProperty("bounds");
                                if (cBoundsProp != null)
                                {
                                    var cBounds = (UnityEngine.Bounds)cBoundsProp.GetValue(collider);
                                    colliderInfo = $"collider({collider.GetType().Name} min={cBounds.min.x:F2},{cBounds.min.y:F2},{cBounds.min.z:F2} max={cBounds.max.x:F2},{cBounds.max.y:F2},{cBounds.max.z:F2})";
                                }
                                else
                                {
                                    colliderInfo = "collider(" + collider.GetType().Name + ")";
                                }
                            }
                        }
                        else
                        {
                            colliderInfo = "collider_type_not_found";
                        }
                    }
                    catch (Exception ex) { colliderInfo = "collider_error:" + ex.GetType().Name; }

                    results.Add($"card[{cardIndex}] eid={entityId} components=[{string.Join(",", components)}] {boundsInfo} {colliderInfo}");
                    cardIndex++;
                }

                if (results.Count == 0)
                    return "PROBE:hand_empty";

                return "PROBE:OK|" + string.Join("|", results);
            }
            catch (Exception ex)
            {
                return "PROBE:error:" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static int GetEntityIdSafe(object entity)
        {
            try
            {
                var tagType = _asm?.GetType("GAME_TAG");
                if (tagType != null)
                {
                    var entityIdField = tagType.GetField("ENTITY_ID");
                    if (entityIdField != null)
                    {
                        var tagVal = entityIdField.GetValue(null);
                        var getTag = entity.GetType().GetMethod("GetTag",
                            new[] { tagType });
                        if (getTag != null)
                        {
                            var result = getTag.Invoke(entity, new[] { tagVal });
                            if (result is int i) return i;
                        }
                    }
                }
                // fallback
                var idProp = entity.GetType().GetProperty("Id")
                    ?? entity.GetType().GetProperty("EntityId");
                if (idProp != null)
                {
                    var val = idProp.GetValue(entity);
                    if (val is int id) return id;
                }
            }
            catch { }
            return 0;
        }

        private static float GetFloatField(object obj, string name)
        {
            if (obj == null) return 0f;
            var fi = obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (fi != null) return (float)fi.GetValue(obj);
            var pi = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null) return (float)pi.GetValue(obj);
            return 0f;
        }

        #endregion
    }
}
