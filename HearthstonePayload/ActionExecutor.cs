using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace HearthstonePayload
{
    /// <summary>
    /// 在炉石进程内执行操作（打牌、攻击、结束回合）
    /// 通过鼠标模拟执行游戏操作，不依赖内部API
    /// 协议格式: "类型|源EntityId|目标EntityId|位置"
    /// </summary>
    public static class ActionExecutor
    {
        private static Assembly _asm;
        private static Type _gameStateType;
        private static Type _entityType;
        private static Type _networkType;
        private static Type _inputMgrType;
        private static Type _connectApiType;

        private static CoroutineExecutor _coroutine;

        /// <summary>
        /// 初始化协程执行器引用
        /// </summary>
        public static void Init(CoroutineExecutor executor)
        {
            _coroutine = executor;
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
            return _gameStateType != null;
        }

        /// <summary>
        /// 通过协程+鼠标模拟执行操作（从后台线程调用，阻塞等待完成）
        /// </summary>
        public static string Execute(GameReader reader, string actionData)
        {
            if (string.IsNullOrEmpty(actionData)) return "SKIP:empty";
            if (!EnsureTypes()) return "SKIP:no_asm";

            var parts = actionData.Split('|');
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
                        bool sourceIsMinionCard = false;

                        if (targetId > 0)
                        {
                            try
                            {
                                var s = reader?.ReadGameState();
                                if (s?.HeroFriend != null && s.HeroFriend.EntityId == targetId) targetHeroSide = 0;
                                else if (s?.HeroEnemy != null && s.HeroEnemy.EntityId == targetId) targetHeroSide = 1;

                                var gs = GetGameState();
                                if (gs != null)
                                    TryIsMinionCardEntity(gs, sourceId, out sourceIsMinionCard);
                            }
                            catch { }
                        }

                        return _coroutine.RunAndWait(MousePlayCard(sourceId, targetId, position, targetHeroSide, sourceIsMinionCard));
                    }
                case "ATTACK":
                    {
                        int attackerId = int.Parse(parts[1]);
                        int targetId = int.Parse(parts[2]);
                        bool sourceIsFriendlyHero = false;
                        bool targetIsEnemyHero = false;
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
                            if (beforeState != null
                                && !CanEntityAttackNow(beforeState, attackerId, targetId, out var notReadyReason))
                            {
                                // 英雄攻击：EXHAUSTED / NUM_ATTACKS_THIS_TURN / ATK 等 tag 在客户端帧上频繁出现
                                // 短暂错位，导致这里 false-reject。对英雄攻击完全跳过预检查，
                                // 先执行操作，再由后置 DidAttackApply 确认是否生效。
                                if (!sourceIsFriendlyHero)
                                {
                                    return "FAIL:ATTACK:not_ready:" + attackerId + ":" + notReadyReason;
                                }
                                // 英雄攻击 — 跳过 not_ready 预检查，继续执行
                            }

                            hasBeforeSnapshot = TryCaptureAttackState(beforeState, attackerId, targetId, out beforeSnapshot);
                        }
                        catch { }

                        var maxAttempts = 2;
                        for (int attempt = 0; attempt < maxAttempts; attempt++)
                        {
                            if (attempt > 0)
                            {
                                try { _coroutine.RunAndWait(MouseCancel(), 1200); } catch { }
                                Thread.Sleep(120);
                                beforeState = reader?.ReadGameState();
                                hasBeforeSnapshot = TryCaptureAttackState(beforeState, attackerId, targetId, out beforeSnapshot);
                            }

                            var attackResult = _coroutine.RunAndWait(
                                MouseAttack(
                                    attackerId,
                                    targetId,
                                    sourceIsFriendlyHero,
                                    targetIsEnemyHero));
                            if (!attackResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase) || !hasBeforeSnapshot)
                                return attackResult;

                            for (int i = 0; i < 10; i++)
                            {
                                Thread.Sleep(80);
                                var afterState = reader?.ReadGameState();
                                if (!TryCaptureAttackState(afterState, attackerId, targetId, out var afterSnapshot))
                                    continue;
                                if (DidAttackApply(beforeSnapshot, afterSnapshot))
                                    return attackResult;
                            }
                        }

                        return "FAIL:ATTACK:not_confirmed:" + attackerId;
                    }
                case "HERO_POWER":
                    return _coroutine.RunAndWait(MouseHeroPower(
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
                        var optionResult = SendOptionForEntity(sourceId, targetId, position, subOptionCardId, false);
                        return optionResult ?? "OK:OPTION";
                    }
                case "TRADE":
                    return _coroutine.RunAndWait(MouseTradeCard(int.Parse(parts[1])));
                case "CONCEDE":
                    Concede();
                    return "OK:CONCEDE";
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
            // 瞬移到按钮并点击
            MouseSimulator.MoveTo(x, y);
            yield return 0.05f;
            // 点击
            MouseSimulator.LeftDown();
            yield return 0.05f;
            MouseSimulator.LeftUp();
            yield return 0.15f;
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
                foreach (var w in SmoothMove(sx, sy, 10, 0.01f)) yield return w;
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

            foreach (var w in SmoothMove(sx, sy, 10, 0.01f)) yield return w;
            MouseSimulator.LeftDown();
            yield return 0.08f;
            foreach (var w in SmoothMove(tx, ty, 16, 0.012f)) yield return w;
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

            if (grabbedViaAPI)
            {
                // API 抓取成功，需要同步设置鼠标按下状态
                // 游戏每帧检查 Input.GetMouseButton(0) 来判断玩家是否还在持有卡牌
                // 如果不设置 LeftDown，游戏会认为玩家立即松手导致卡牌弹回
                int midX = MouseSimulator.GetScreenWidth() / 2;
                int midY = MouseSimulator.GetScreenHeight() / 2;
                MouseSimulator.MoveTo(midX, midY);
                MouseSimulator.LeftDown();
                yield return 0.15f;
            }
            else
            {
                // API 失败，回退到鼠标点击手牌位置抓取
                int sx = 0, sy = 0;
                bool foundSource = false;
                for (int retry = 0; retry < 5; retry++)
                {
                    if (GameObjectFinder.GetEntityScreenPos(entityId, out sx, out sy))
                    {
                        foundSource = true;
                        break;
                    }
                    yield return 0.5f;
                }
                if (!foundSource)
                {
                    var handIds = GameObjectFinder.GetHandEntityIds();
                    var handStr = handIds.Count > 0 ? string.Join(",", handIds) : "empty";
                    if (!grabbedViaAPI)
                        _coroutine.SetResult("FAIL:PLAY:grab_verify_failed:" + entityId + ":hand=[" + handStr + "]");
                    else
                        _coroutine.SetResult("FAIL:PLAY:source_pos:" + entityId + ":hand=[" + handStr + "]");
                    yield break;
                }
                MouseSimulator.MoveTo(sx, sy);
                yield return 0.05f;
                MouseSimulator.LeftDown();
                yield return 0.15f;
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

                    foreach (var w in SmoothMove(dx, dy, 15)) yield return w;
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

                    MouseSimulator.MoveTo(tx, ty);
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
                    foreach (var w in SmoothMove(tx, ty, 15)) yield return w;
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
                foreach (var w in SmoothMove(dx, dy, 15)) yield return w;
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
                _coroutine.SetResult("FAIL:PLAY:not_left_hand:" + entityId);
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
            var method = grabbedViaAPI ? "api_grab" : "mouse_grab";
            _coroutine.SetResult("OK:PLAY:" + entityId + ":" + method);
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

            bool grabbedViaAPI = TryGrabCardViaAPI(entityId);
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
                int sx = 0, sy = 0;
                bool foundSource = false;
                for (int retry = 0; retry < 5; retry++)
                {
                    if (GameObjectFinder.GetEntityScreenPos(entityId, out sx, out sy))
                    {
                        foundSource = true;
                        break;
                    }
                    yield return 0.35f;
                }
                if (!foundSource)
                {
                    _coroutine.SetResult("FAIL:TRADE:source_pos:" + entityId);
                    yield break;
                }

                MouseSimulator.MoveTo(sx, sy);
                yield return 0.05f;
                MouseSimulator.LeftDown();
                yield return 0.12f;
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

            var method = grabbedViaAPI ? "api_grab" : "mouse_grab";
            _coroutine.SetResult("OK:TRADE:" + entityId + ":" + method);
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
                var card = Invoke(entity, "GetCard");

                var inputMgr = GetSingleton(_inputMgrType);
                if (inputMgr == null)
                {
                    detail = "input_manager_missing";
                    return false;
                }

                // 尝试多种 InputManager 方法来"抓取"手牌
                // 炉石不同版本可能使用不同的方法名
                var grabMethods = new[]
                {
                    "HandleClickOnCardInHand",
                    "GrabCard",
                    "PickUpCard"
                };

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                string lastFailure = "no_grab_method";

                foreach (var methodName in grabMethods)
                {
                    var methods = _inputMgrType.GetMethods(flags)
                        .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    foreach (var mi in methods)
                    {
                        var parameters = mi.GetParameters();
                        if (parameters.Length == 0) continue;

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

                detail = lastFailure;
                AppendActionTrace("API grab not confirmed entityId=" + entityId + ", detail=" + lastFailure);
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
            MouseSimulator.MoveTo(sx, sy);
            yield return 0.05f;
            MouseSimulator.LeftDown();
            yield return 0.05f;
            MouseSimulator.LeftUp();
            // 给客户端一点时间进入攻击选中态
            yield return 0.08f;

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

                yield return 0.06f;
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
            foreach (var w in SmoothMove(tx, ty, 12)) yield return w;
            MouseSimulator.LeftDown();
            yield return 0.05f;
            MouseSimulator.LeftUp();
            yield return 0.18f;
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

        private static bool DidAttackApply(AttackStateSnapshot before, AttackStateSnapshot after)
        {
            if (after.AttackerAttackCount > before.AttackerAttackCount)
                return true;

            if (!before.AttackerExhausted && after.AttackerExhausted)
                return true;

            if (before.HasWeaponDurability && after.HasWeaponDurability
                && after.WeaponDurability != before.WeaponDurability)
                return true;

            if (before.TargetExists && !after.TargetExists)
                return true;

            if (before.TargetExists && after.TargetExists)
            {
                if (after.TargetHealth != before.TargetHealth)
                    return true;
                if (after.TargetArmor != before.TargetArmor)
                    return true;
                if (after.TargetDivineShield != before.TargetDivineShield)
                    return true;
            }

            return false;
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

        private static IEnumerator<float> MouseHeroPower(int targetEntityId)
        {
            InputHook.Simulating = true;
            if (!GameObjectFinder.GetHeroPowerScreenPos(out var hx, out var hy))
            {
                _coroutine.SetResult("FAIL:HP:pos_not_found");
                yield break;
            }

            // 瞬移并点击英雄技能
            MouseSimulator.MoveTo(hx, hy);
            yield return 0.05f;
            MouseSimulator.LeftDown();
            yield return 0.05f;
            MouseSimulator.LeftUp();
            yield return 0.3f;

            // 如果有目标
            if (targetEntityId > 0)
            {
                bool gotTarget = GameObjectFinder.GetEntityScreenPos(targetEntityId, out var tx, out var ty);
                if (!gotTarget)
                    gotTarget = GameObjectFinder.GetHeroScreenPos(false, out tx, out ty);
                if (!gotTarget)
                {
                    _coroutine.SetResult("FAIL:HP:target_pos:" + targetEntityId);
                    yield break;
                }
                // 瞬移到目标并点击
                MouseSimulator.MoveTo(tx, ty);
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
            yield return 0.3f;

            // 点击要替换的卡牌
            foreach (var idx in replaceIndices)
            {
                if (!GameObjectFinder.GetMulliganCardScreenPos(idx, totalCards, out var cx, out var cy))
                    continue;
                foreach (var w in SmoothMove(cx, cy)) yield return w;
                MouseSimulator.LeftDown();
                yield return 0.05f;
                MouseSimulator.LeftUp();
                yield return 0.4f;
            }

            // 通过 API 确认留牌（虚拟鼠标点击确认按钮对 PegUI 无效）
            var mulliganMgr = TryGetMulliganManager();
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
        /// Apply mulligan replacement list and confirm selection.
        /// </summary>
        public static string ApplyMulligan(string replaceEntityIdsCsv)
        {
            if (!EnsureTypes()) return "ERROR:not_initialized";
            if (_coroutine == null) return "ERROR:no_coroutine";

            var replaceEntityIds = ParseEntityIds(replaceEntityIdsCsv);
            return _coroutine.RunAndWait(ApplyMulliganByMouseWithVerification(replaceEntityIds), 20000);
        }

        /// <summary>
        /// 留牌替换使用鼠标点击卡牌，并在每次点击后校验标记状态是否与预期一致。
        /// 确认按钮逻辑保持现有管理器调用方式。
        /// </summary>
        private static IEnumerator<float> ApplyMulliganByMouseWithVerification(int[] replaceEntityIds)
        {
            InputHook.Simulating = true;
            yield return 0.06f;

            var mulliganMgr = TryGetMulliganManager();
            if (mulliganMgr == null)
            {
                _coroutine.SetResult("WAIT:mulligan_manager:not_available");
                yield break;
            }

            if (TryInvokeBoolMethod(mulliganMgr, "IsMulliganActive", out var isMulliganActive) && !isMulliganActive)
            {
                _coroutine.SetResult("FAIL:mulligan_manager:mulligan_not_active");
                yield break;
            }

            var waitingObj = GetFieldOrProp(mulliganMgr, "m_waitingForUserInput");
            if (!(waitingObj is bool waitingForUserInput) || !waitingForUserInput)
            {
                _coroutine.SetResult("WAIT:mulligan_manager:waiting_for_user_input");
                yield break;
            }

            var gameState = GetGameState();
            if (gameState != null)
            {
                if (TryInvokeBoolMethod(gameState, "IsResponsePacketBlocked", out var responseBlocked) && responseBlocked)
                {
                    _coroutine.SetResult("WAIT:mulligan_manager:response_packet_blocked");
                    yield break;
                }

                var responseMode = Invoke(gameState, "GetResponseMode");
                if (!IsChoiceResponseMode(responseMode))
                {
                    _coroutine.SetResult("WAIT:mulligan_manager:response_mode_not_choice:" + (responseMode?.ToString() ?? "null"));
                    yield break;
                }

                if (Invoke(gameState, "GetFriendlyEntityChoices") == null)
                {
                    _coroutine.SetResult("WAIT:mulligan_manager:friendly_choices_not_ready");
                    yield break;
                }
            }

            var inputMgr = GetSingleton(_inputMgrType);
            if (inputMgr != null && (!TryInvokeBoolMethod(inputMgr, "PermitDecisionMakingInput", out var permitInput) || !permitInput))
            {
                _coroutine.SetResult("WAIT:mulligan_manager:input_not_ready");
                yield break;
            }

            var startingCardsRaw = GetFieldOrProp(mulliganMgr, "m_startingCards") as IEnumerable;
            if (startingCardsRaw == null)
            {
                _coroutine.SetResult("WAIT:mulligan_manager:starting_cards_not_ready");
                yield break;
            }

            var startingCards = startingCardsRaw.Cast<object>().Where(c => c != null).ToList();
            if (startingCards.Count == 0)
            {
                _coroutine.SetResult("WAIT:mulligan_manager:starting_cards_empty");
                yield break;
            }

            if (!TryGetCollectionCount(GetFieldOrProp(mulliganMgr, "m_handCardsMarkedForReplace"), out var markedCount)
                || markedCount < startingCards.Count)
            {
                _coroutine.SetResult("WAIT:mulligan_manager:marked_state_not_ready");
                yield break;
            }

            var cardIndexByEntityId = new Dictionary<int, int>();
            for (int cardIndex = 0; cardIndex < startingCards.Count; cardIndex++)
            {
                var card = startingCards[cardIndex];
                var entity = Invoke(card, "GetEntity")
                    ?? GetFieldOrProp(card, "Entity")
                    ?? GetFieldOrProp(card, "m_entity");
                var entityId = ResolveEntityId(entity);
                if (entityId <= 0) continue;
                if (!cardIndexByEntityId.ContainsKey(entityId))
                    cardIndexByEntityId.Add(entityId, cardIndex);
            }

            if (cardIndexByEntityId.Count == 0)
            {
                _coroutine.SetResult("WAIT:mulligan_manager:starting_cards_entity_not_ready");
                yield break;
            }

            var requestIdSet = new HashSet<int>(replaceEntityIds.Where(id => id > 0).Distinct());
            foreach (var entityId in requestIdSet)
            {
                if (!cardIndexByEntityId.ContainsKey(entityId))
                {
                    _coroutine.SetResult("FAIL:mulligan_manager:entity_not_found:" + entityId);
                    yield break;
                }
            }

            var toggledCount = 0;
            foreach (var pair in cardIndexByEntityId.OrderBy(p => p.Value))
            {
                var entityId = pair.Key;
                var cardIndex = pair.Value;
                var shouldReplace = requestIdSet.Contains(entityId);

                if (!TryReadMulliganMarkedState(mulliganMgr, cardIndex, out var currentMarked, out var markedDetail))
                {
                    _coroutine.SetResult("FAIL:mulligan_manager:marked_state_read_failed:" + markedDetail);
                    yield break;
                }

                if (currentMarked == shouldReplace)
                    continue;

                var toggled = false;
                string lastToggleDetail = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (!TryGetMulliganCardClickPos(entityId, cardIndex, startingCards.Count, out var cx, out var cy, out var posDetail))
                    {
                        lastToggleDetail = "click_pos_failed:" + posDetail;
                        yield return 0.1f;
                        continue;
                    }

                    foreach (var w in SmoothMove(cx, cy, 8, 0.012f)) yield return w;
                    MouseSimulator.LeftDown();
                    yield return 0.05f;
                    MouseSimulator.LeftUp();
                    yield return 0.22f;

                    if (!TryReadMulliganMarkedState(mulliganMgr, cardIndex, out var markedAfter, out markedDetail))
                    {
                        lastToggleDetail = "marked_state_verify_failed:" + markedDetail;
                        continue;
                    }

                    if (markedAfter == shouldReplace)
                    {
                        toggled = true;
                        break;
                    }

                    lastToggleDetail = "toggle_state_mismatch:expected="
                        + (shouldReplace ? "1" : "0")
                        + ":actual=" + (markedAfter ? "1" : "0");
                }

                if (!toggled)
                {
                    _coroutine.SetResult("FAIL:mulligan_manager:toggle_failed:" + entityId + ":" + (lastToggleDetail ?? "unknown"));
                    yield break;
                }

                toggledCount++;
            }

            // 确认逻辑保持现有实现（管理器确认，不改确认按钮路径）
            string continueMethodUsed = null;
            if (TryInvokeMethod(mulliganMgr, "OnMulliganButtonReleased", new object[] { null }, out _, out var continueError))
            {
                continueMethodUsed = "OnMulliganButtonReleased";
            }
            else if (TryInvokeMethod(mulliganMgr, "AutomaticContinueMulligan", new object[] { false }, out _, out continueError))
            {
                continueMethodUsed = "AutomaticContinueMulligan";
            }
            else if (gameState != null && TryInvokeMethod(gameState, "SendChoices", Array.Empty<object>(), out _, out continueError))
            {
                continueMethodUsed = "GameState.SendChoices";
            }

            if (continueMethodUsed == null)
            {
                _coroutine.SetResult("FAIL:mulligan_manager:continue_failed:" + (continueError ?? "unknown"));
                yield break;
            }

            yield return 0.08f;
            _coroutine.SetResult("OK:mulligan_manager:toggle=" + toggledCount + ";request=" + requestIdSet.Count + ";continue=" + continueMethodUsed);
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
        /// Return replaceable mulligan choices only (from MulliganManager.m_startingCards).
        /// Format: cardId1,entityId1;cardId2,entityId2;...
        /// Returns empty string when mulligan ui exists but cards are not ready yet.
        /// Returns null when not in mulligan context.
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

            var startingCardsRaw = GetFieldOrProp(mulliganMgr, "m_startingCards") as IEnumerable;
            if (startingCardsRaw == null)
                return string.Empty;

            var parts = new List<string>();
            foreach (var card in startingCardsRaw)
            {
                if (card == null) continue;

                var entity = Invoke(card, "GetEntity")
                    ?? GetFieldOrProp(card, "Entity")
                    ?? GetFieldOrProp(card, "m_entity");
                if (entity == null) continue;

                var entityId = ResolveEntityId(entity);
                if (entityId <= 0) continue;

                var cardIdObj = Invoke(entity, "GetCardId")
                    ?? GetFieldOrProp(entity, "CardId")
                    ?? Invoke(card, "GetCardId")
                    ?? GetFieldOrProp(card, "CardId");
                var cardId = cardIdObj?.ToString();
                if (string.IsNullOrWhiteSpace(cardId)) continue;

                parts.Add(cardId + "," + entityId);
            }

            return string.Join(";", parts);
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

        private static bool TryApplyMulliganViaManager(int[] replaceEntityIds, out string detail, out bool managerAvailable)
        {
            detail = null;
            managerAvailable = false;
            replaceEntityIds = replaceEntityIds ?? Array.Empty<int>();

            var mulliganMgr = TryGetMulliganManager();
            if (mulliganMgr == null)
            {
                detail = "mulligan_manager_not_found";
                return false;
            }

            managerAvailable = true;

            if (TryInvokeBoolMethod(mulliganMgr, "IsMulliganActive", out var isMulliganActive) && !isMulliganActive)
            {
                detail = "mulligan_not_active";
                return false;
            }

            var waitingObj = GetFieldOrProp(mulliganMgr, "m_waitingForUserInput");
            if (!(waitingObj is bool waitingForUserInput) || !waitingForUserInput)
            {
                detail = "waiting_for_user_input";
                return false;
            }

            var gameState = GetGameState();
            if (gameState != null)
            {
                if (TryInvokeBoolMethod(gameState, "IsResponsePacketBlocked", out var responseBlocked) && responseBlocked)
                {
                    detail = "response_packet_blocked";
                    return false;
                }

                var responseMode = Invoke(gameState, "GetResponseMode");
                if (!IsChoiceResponseMode(responseMode))
                {
                    detail = "response_mode_not_choice:" + (responseMode?.ToString() ?? "null");
                    return false;
                }

                if (Invoke(gameState, "GetFriendlyEntityChoices") == null)
                {
                    detail = "friendly_choices_not_ready";
                    return false;
                }
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

            var startingCards = startingCardsRaw.Cast<object>().ToList();
            if (startingCards.Count == 0)
            {
                detail = "starting_cards_empty";
                return false;
            }

            if (!TryGetCollectionCount(GetFieldOrProp(mulliganMgr, "m_handCardsMarkedForReplace"), out var markedCount)
                || markedCount < startingCards.Count)
            {
                detail = "marked_state_not_ready";
                return false;
            }

            var cardByEntityId = new Dictionary<int, object>();
            var cardIndexByEntityId = new Dictionary<int, int>();
            for (int cardIndex = 0; cardIndex < startingCards.Count; cardIndex++)
            {
                var card = startingCards[cardIndex];
                if (card == null) continue;

                var entity = Invoke(card, "GetEntity");
                if (entity == null) continue;

                var entityId = ResolveEntityId(entity);
                if (entityId <= 0) continue;
                if (cardByEntityId.ContainsKey(entityId)) continue;

                cardByEntityId.Add(entityId, card);
                cardIndexByEntityId.Add(entityId, cardIndex);
            }

            if (cardByEntityId.Count == 0)
            {
                detail = "starting_cards_entity_not_ready";
                return false;
            }

            var requestIdSet = new HashSet<int>(replaceEntityIds
                .Where(id => id > 0)
                .Distinct());

            foreach (var entityId in requestIdSet)
            {
                if (!cardByEntityId.ContainsKey(entityId))
                {
                    detail = "entity_not_found:" + entityId;
                    return false;
                }
            }

            var toggledCount = 0;
            foreach (var pair in cardIndexByEntityId.OrderBy(p => p.Value))
            {
                var entityId = pair.Key;
                var cardIndex = pair.Value;
                var shouldReplace = requestIdSet.Contains(entityId);

                if (!TryReadMulliganMarkedState(mulliganMgr, cardIndex, out var currentMarked, out var markedDetail))
                {
                    detail = "marked_state_read_failed:" + markedDetail;
                    return false;
                }

                if (currentMarked == shouldReplace)
                    continue;

                if (!cardByEntityId.TryGetValue(entityId, out var card) || card == null)
                {
                    detail = "card_not_found:" + entityId;
                    return false;
                }

                if (!TryToggleMulliganCardViaManager(mulliganMgr, card, cardIndex, out var toggleDetail))
                {
                    detail = "toggle_failed:" + entityId + ":" + toggleDetail;
                    return false;
                }

                if (!TryReadMulliganMarkedState(mulliganMgr, cardIndex, out var markedAfter, out markedDetail))
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
            if (TryInvokeMethod(mulliganMgr, "OnMulliganButtonReleased", new object[] { null }, out _, out var continueError))
            {
                continueMethodUsed = "OnMulliganButtonReleased";
            }
            else if (TryInvokeMethod(mulliganMgr, "AutomaticContinueMulligan", new object[] { false }, out _, out continueError))
            {
                continueMethodUsed = "AutomaticContinueMulligan";
            }
            else if (gameState != null && TryInvokeMethod(gameState, "SendChoices", Array.Empty<object>(), out _, out continueError))
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
                // fallback: 通过 EndTurnButton
                var etbType = _asm.GetType("EndTurnButton");
                if (etbType != null)
                {
                    var etb = InvokeStatic(etbType, "Get");
                    if (etb != null)
                    {
                        // 模拟点击结束回合按钮
                        Invoke(etb, "OnEndTurnRequested");
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
                || normalized.Contains("response_packet_blocked")
                || normalized.Contains("friendly_choices_not_ready")
                || normalized.Contains("input_not_ready")
                || normalized.Contains("starting_cards_not_ready")
                || normalized.Contains("starting_cards_empty")
                || normalized.Contains("starting_cards_entity_not_ready")
                || normalized.Contains("marked_state_not_ready")
                || normalized.Contains("mulligan_not_active")
                || normalized.Contains("mulligan_manager_not_found");
        }

        private static bool TrySendMulliganChoices(int[] replaceEntityIds, out string detail)
        {
            detail = null;
            replaceEntityIds = replaceEntityIds ?? Array.Empty<int>();

            if (TrySendMulliganChoicesByChoicePacket(replaceEntityIds, out detail))
                return true;

            var methodNames = new[]
            {
                "SendMulligan",
                "SubmitMulligan",
                "ConfirmMulligan",
                "DoMulligan"
            };

            var gameState = GetGameState();
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

            var mulliganMgr = TryGetMulliganManager();
            if (mulliganMgr != null && TryInvokeMulliganSender(mulliganMgr, methodNames, replaceEntityIds, entityChoices, out detail))
                return true;

            return false;
        }

        private static bool TrySendMulliganChoicesByChoicePacket(int[] replaceEntityIds, out string detail)
        {
            detail = null;

            var network = GetSingleton(_networkType);
            if (network == null) return false;

            var gameState = GetGameState();
            var friendlyChoices = gameState != null ? Invoke(gameState, "GetFriendlyEntityChoices") : null;
            if (friendlyChoices == null)
                friendlyChoices = Invoke(network, "GetEntityChoices");
            if (friendlyChoices == null)
                return false;

            var choiceId = GetIntFieldOrProp(friendlyChoices, "ID");
            if (choiceId <= 0)
                choiceId = GetIntFieldOrProp(friendlyChoices, "Id");
            if (choiceId <= 0)
                return false;

            var allowedEntityIds = new HashSet<int>();
            var entities = GetFieldOrProp(friendlyChoices, "Entities") as IEnumerable;
            if (entities != null)
            {
                foreach (var entity in entities)
                {
                    try
                    {
                        var id = Convert.ToInt32(entity);
                        if (id > 0) allowedEntityIds.Add(id);
                    }
                    catch
                    {
                    }
                }
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
            if (gameState == null)
                return false;

            if (TryInvokeBoolMethod(gameState, "IsInChoiceMode", out var inChoiceMode))
                return inChoiceMode;

            var responseMode = Invoke(gameState, "GetResponseMode");
            return IsChoiceResponseMode(responseMode);
        }

        private static string GetChoiceModeBySourceTags(object sourceEntity)
        {
            if (sourceEntity == null)
                return "CHOOSE_ONE";

            // 与 HB1.1.8 的判定一致：优先用来源实体标签区分具体选择模式。
            if (HasSourceEntityTag(sourceEntity, "DISCOVER")) return "DISCOVER";
            if (HasSourceEntityTag(sourceEntity, "ADAPT")) return "ADAPT";
            if (HasSourceEntityTag(sourceEntity, "DREDGE")) return "DREDGE";
            if (HasSourceEntityTag(sourceEntity, "TITAN")) return "TITAN";

            return "CHOOSE_ONE";
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

            if (!allowEndTurnFallback)
                return false;

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

            if (!IsChoiceModeActive(gs)) return null;

            // 排除调度阶段
            if (TryGetMulliganManager() != null)
            {
                var mulliganMgr = TryGetMulliganManager();
                if (TryInvokeBoolMethod(mulliganMgr, "IsMulliganActive", out var active) && active)
                    return null;
            }

            var choices = Invoke(gs, "GetFriendlyEntityChoices");
            if (choices == null) return null;

            // 获取来源卡牌ID
            var sourceEntityId = GetIntFieldOrProp(choices, "Source");
            if (sourceEntityId <= 0) sourceEntityId = GetIntFieldOrProp(choices, "m_source");
            string originCardId = "";
            object sourceEntity = null;
            if (sourceEntityId > 0)
            {
                sourceEntity = GetEntity(gs, sourceEntityId);
                if (sourceEntity != null)
                {
                    var cardIdObj = Invoke(sourceEntity, "GetCardId") ?? GetFieldOrProp(sourceEntity, "CardId");
                    originCardId = cardIdObj?.ToString() ?? "";
                }
            }
            var choiceMode = GetChoiceModeBySourceTags(sourceEntity);

            // 获取选项实体列表
            var entities = GetFieldOrProp(choices, "Entities") as IEnumerable;
            if (entities == null) return null;

            var parts = new List<string>();
            foreach (var eid in entities)
            {
                int id;
                try { id = System.Convert.ToInt32(eid); } catch { continue; }
                if (id <= 0) continue;
                var entity = GetEntity(gs, id);
                if (entity == null) continue;
                var cid = (Invoke(entity, "GetCardId") ?? GetFieldOrProp(entity, "CardId"))?.ToString() ?? "";
                parts.Add(cid + "," + id);
            }

            if (parts.Count == 0) return null;
            return originCardId + "|" + string.Join(";", parts) + "|" + choiceMode;
        }

        /// <summary>
        /// 按屏幕比例坐标点击（用于 Rewind 等 UI 按钮）
        /// ratioX/ratioY 取值 0.0~1.0
        /// </summary>
        public static string ClickScreen(float ratioX, float ratioY)
        {
            if (_coroutine == null) return "ERROR:no_coroutine";
            return _coroutine.RunAndWait(MouseClickScreenCoroutine(ratioX, ratioY));
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
            var gs = GetGameState();
            if (gs == null) return null;
            var choices = Invoke(gs, "GetFriendlyEntityChoices");
            if (choices == null) return null;

            var choiceId = GetIntFieldOrProp(choices, "ID");
            if (choiceId <= 0) choiceId = GetIntFieldOrProp(choices, "Id");
            if (choiceId <= 0) return null;

            var network = GetSingleton(_networkType);
            if (network == null) return null;

            var methods = _networkType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, "SendChoices", StringComparison.OrdinalIgnoreCase) && m.GetParameters().Length == 2)
                .ToArray();

            foreach (var method in methods)
            {
                var pars = method.GetParameters();
                if (pars[0].ParameterType != typeof(int)) continue;
                if (!TryBuildIntCollectionArg(pars[1].ParameterType, new[] { entityId }, out var pickArg)) continue;
                try
                {
                    method.Invoke(network, new[] { (object)choiceId, pickArg });
                    return "OK:CHOICE:network:" + entityId;
                }
                catch { }
            }
            return null;
        }

        private static IEnumerator<float> MouseClickChoice(int entityId)
        {
            InputHook.Simulating = true;

            // 记录点击前的选择快照，用于确认是否真的提交成功。
            CaptureChoiceSnapshot(out var beforeChoiceId, out var beforeSignature);

            bool confirmed = false;
            string confirmDetail = "timeout";

            // 鼠标点击做两次尝试：第一次常规点击，第二次作为“界面刚亮起时序抖动”补偿。
            for (int attempt = 1; attempt <= 2 && !confirmed; attempt++)
            {
                if (!GameObjectFinder.GetEntityScreenPos(entityId, out var x, out var y))
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
            if (!confirmed)
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

            if (!IsChoiceModeActive(gs)) return false;

            var choices = Invoke(gs, "GetFriendlyEntityChoices");
            if (choices == null) return false;

            choiceId = GetIntFieldOrProp(choices, "ID");
            if (choiceId <= 0) choiceId = GetIntFieldOrProp(choices, "Id");

            var entities = GetFieldOrProp(choices, "Entities") as IEnumerable;
            if (entities == null) return false;

            var ids = new List<int>();
            foreach (var eid in entities)
            {
                try
                {
                    var id = Convert.ToInt32(eid);
                    if (id > 0) ids.Add(id);
                }
                catch
                {
                }
            }

            ids.Sort();
            signature = choiceId.ToString() + ":" + string.Join(",", ids);
            return true;
        }

        #endregion

        #region 操作等待

        /// <summary>
        /// 检查游戏是否准备好接受下一个操作（由 WAIT_READY 命令调用，不阻塞主线程）
        /// </summary>
        public static bool IsGameReady()
        {
            if (!EnsureTypes()) return false;

            var gs = GetGameState();
            if (gs == null) return false;

            // 网络包阻塞检查
            if (TryInvokeBoolMethod(gs, "IsResponsePacketBlocked", out var blocked) && blocked)
                return false;

            // 输入权限检查
            var inputMgr = GetSingleton(_inputMgrType);
            if (inputMgr != null
                && TryInvokeBoolMethod(inputMgr, "PermitDecisionMakingInput", out var permit)
                && !permit)
                return false;

            // 战吼/动画处理中检查（PowerProcessor 正在运行时游戏状态尚未稳定）
            if (TryInvokeBoolMethod(gs, "IsBusy", out var busy) && busy)
                return false;
            if (TryInvokeBoolMethod(gs, "IsBlockingPowerProcessor", out var bpp) && bpp)
                return false;

            var ppType = _asm?.GetType("PowerProcessor");
            if (ppType != null)
            {
                var pp = GetSingleton(ppType);
                if (pp != null && TryInvokeBoolMethod(pp, "IsRunning", out var ppRunning) && ppRunning)
                    return false;
            }

            return true;
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
                    var targetIndex = -1;
                    var hasTargets = false;

                    if (targetEntityId > 0)
                    {
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
                                if (tId == targetEntityId)
                                {
                                    targetIndex = tIdx;
                                    break;
                                }
                                tIdx++;
                            }
                        }

                        if (hasTargets && targetIndex < 0)
                            continue;
                    }

                    return TrySendOption(
                        optionIndexByOrder,
                        optionIndexByField,
                        subOptionIndex,
                        targetIndex,
                        targetEntityId,
                        position,
                        option,
                        entityId,
                        requireSourceLeaveHand);
                }

                return "no_match:count=" + optionCount + ";ids=" + string.Join(",", mainIds);
            }
            catch (Exception ex) { return "ex:" + SimplifyException(ex); }
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

        private static void AppendActionTrace(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                System.IO.File.AppendAllText(
                    "payload_error.log",
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

        #endregion
    }
}
