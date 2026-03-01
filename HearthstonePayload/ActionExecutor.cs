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
                    return _coroutine.RunAndWait(MousePlayCard(
                        int.Parse(parts[1]),
                        parts.Length > 2 ? int.Parse(parts[2]) : 0,
                        parts.Length > 3 ? int.Parse(parts[3]) : 0));
                case "ATTACK":
                    return _coroutine.RunAndWait(MouseAttack(
                        int.Parse(parts[1]), int.Parse(parts[2])));
                case "HERO_POWER":
                    return _coroutine.RunAndWait(MouseHeroPower(
                        parts.Length > 2 ? int.Parse(parts[2]) : 0));
                case "USE_LOCATION":
                    return _coroutine.RunAndWait(MouseUseLocation(int.Parse(parts[1])));
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
        /// 点击场上地标激活效果
        /// </summary>
        private static IEnumerator<float> MouseUseLocation(int entityId)
        {
            InputHook.Simulating = true;
            if (!GameObjectFinder.GetEntityScreenPos(entityId, out var x, out var y))
            {
                _coroutine.SetResult("FAIL:USE_LOCATION:pos:" + entityId);
                yield break;
            }
            MouseSimulator.MoveTo(x, y);
            yield return 0.05f;
            MouseSimulator.LeftDown();
            yield return 0.1f;
            MouseSimulator.LeftUp();
            yield return 0.5f;
            _coroutine.SetResult("OK:USE_LOCATION:" + entityId);
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
        /// 鼠标拖拽出牌
        /// </summary>
        private static IEnumerator<float> MousePlayCard(int entityId, int targetEntityId, int position)
        {
            InputHook.Simulating = true;
            int sx = 0, sy = 0;
            bool foundSource = false;
            for (int retry = 0; retry < 5; retry++)
            {
                if (GameObjectFinder.GetEntityScreenPos(entityId, out sx, out sy))
                {
                    foundSource = true;
                    break;
                }
                yield return 0.5f; // 等待500ms让视觉层加载
            }
            if (!foundSource)
            {
                _coroutine.SetResult("FAIL:PLAY:source_pos:" + entityId);
                yield break;
            }

            // 瞬移到手牌位置并拾取
            MouseSimulator.MoveTo(sx, sy);
            yield return 0.05f;
            MouseSimulator.LeftDown();
            yield return 0.1f;

            if (targetEntityId > 0)
            {
                // 有目标：先拖到场中央触发出牌
                int midX = MouseSimulator.GetScreenWidth() / 2;
                int midY = MouseSimulator.GetScreenHeight() / 2;
                foreach (var w in SmoothMove(midX, midY, 15)) yield return w;
                MouseSimulator.LeftUp();
                yield return 0.5f;

                // 点击目标
                bool gotTarget = GameObjectFinder.GetEntityScreenPos(targetEntityId, out var tx, out var ty);
                if (!gotTarget)
                    gotTarget = GameObjectFinder.GetHeroScreenPos(false, out tx, out ty);
                if (!gotTarget)
                {
                    _coroutine.SetResult("FAIL:PLAY:target_pos:" + targetEntityId);
                    yield break;
                }
                // 瞬移到目标并点击
                MouseSimulator.MoveTo(tx, ty);
                yield return 0.05f;
                MouseSimulator.LeftDown();
                yield return 0.05f;
                MouseSimulator.LeftUp();
            }
            else
            {
                // 无目标：拖到放置位置
                int totalMinions = GetFriendlyMinionCount();
                if (!GameObjectFinder.GetBoardDropZoneScreenPos(position, totalMinions, out var dx, out var dy))
                {
                    _coroutine.SetResult("FAIL:PLAY:drop_pos");
                    yield break;
                }
                foreach (var w in SmoothMove(dx, dy, 15)) yield return w;
                MouseSimulator.LeftUp();
            }

            yield return 0.5f;
            _coroutine.SetResult("OK:PLAY:" + entityId);
        }

        /// <summary>
        /// 鼠标拖拽攻击
        /// </summary>
        private static IEnumerator<float> MouseAttack(int attackerEntityId, int targetEntityId)
        {
            InputHook.Simulating = true;
            int sx = 0, sy = 0;
            bool gotAttacker = false;
            var isFriendlyHeroAttacker = IsFriendlyHeroEntityId(attackerEntityId);
            for (int retry = 0; retry < 5; retry++)
            {
                if (GameObjectFinder.GetEntityScreenPos(attackerEntityId, out sx, out sy))
                {
                    gotAttacker = true;
                    break;
                }
                // 尝试英雄位置
                if (isFriendlyHeroAttacker
                    && GameObjectFinder.GetHeroScreenPos(true, out sx, out sy))
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

            bool gotTarget = GameObjectFinder.GetEntityScreenPos(targetEntityId, out var tx, out var ty);
            if (!gotTarget)
                gotTarget = GameObjectFinder.GetHeroScreenPos(false, out tx, out ty);
            if (!gotTarget)
            {
                _coroutine.SetResult("FAIL:ATTACK:target_pos:" + targetEntityId);
                yield break;
            }

            // 瞬移到攻击者并拾取
            MouseSimulator.MoveTo(sx, sy);
            yield return 0.05f;
            MouseSimulator.LeftDown();
            yield return 0.1f;

            // 平滑拖到目标并释放
            foreach (var w in SmoothMove(tx, ty, 15)) yield return w;
            MouseSimulator.LeftUp();
            yield return 0.15f;
            _coroutine.SetResult("OK:ATTACK:" + attackerEntityId);
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
            if (TryApplyMulliganViaManager(replaceEntityIds, out var detail, out var managerAvailable))
                return "OK:mulligan_manager:" + detail;

            if (managerAvailable)
            {
                if (IsMulliganManagerWaitingDetail(detail))
                    return "WAIT:mulligan_manager:" + (detail ?? "unknown");
                return "FAIL:mulligan_manager:" + (detail ?? "unknown");
            }

            // 管理器尚未可用时，不回退鼠标硬点，避免在入场动画阶段误触发留牌。
            return "WAIT:mulligan_manager:not_available";
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
                if (SendOptionForEntity(entityId, 0, -1, false) == null)
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
        /// 格式: originCardId|cardId1,entityId1;cardId2,entityId2;cardId3,entityId3
        /// </summary>
        public static string GetChoiceState()
        {
            if (!EnsureTypes()) return null;
            var gs = GetGameState();
            if (gs == null) return null;

            var responseMode = Invoke(gs, "GetResponseMode");
            if (!IsChoiceResponseMode(responseMode)) return null;

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
            if (sourceEntityId > 0)
            {
                var sourceEntity = GetEntity(gs, sourceEntityId);
                if (sourceEntity != null)
                {
                    var cardIdObj = Invoke(sourceEntity, "GetCardId") ?? GetFieldOrProp(sourceEntity, "CardId");
                    originCardId = cardIdObj?.ToString() ?? "";
                }
            }

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
            return originCardId + "|" + string.Join(";", parts);
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
            if (!GameObjectFinder.GetEntityScreenPos(entityId, out var x, out var y))
            {
                _coroutine.SetResult("FAIL:CHOICE:pos:" + entityId);
                yield break;
            }
            foreach (var w in SmoothMove(x, y)) yield return w;
            MouseSimulator.LeftDown();
            yield return 0.05f;
            MouseSimulator.LeftUp();
            yield return 0.5f;
            _coroutine.SetResult("OK:CHOICE:mouse:" + entityId);
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

                    var subOptionIndex = -1;
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

        private static object[] BuildArgs(ParameterInfo[] pars, object entity, int targetEntityId, int position)
        {
            var args = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
            {
                if (i == 0) args[i] = entity;
                else if (pars[i].ParameterType == typeof(int))
                {
                    if (pars[i].Name.ToLower().Contains("target"))
                        args[i] = targetEntityId;
                    else if (pars[i].Name.ToLower().Contains("pos"))
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
