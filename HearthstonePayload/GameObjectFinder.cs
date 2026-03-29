using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace HearthstonePayload
{
    /// <summary>
    /// 通过反射链定位游戏对象的屏幕位置
    /// Entity → Card → Actor → GameObject → Transform → WorldPos → ScreenPos
    /// </summary>
    public static class GameObjectFinder
    {
        private static Assembly _asm;
        private static Type _gameStateType;

        /// <summary>上次手牌偏移计算的调试信息，供 ActionExecutor 日志输出</summary>
        public static string _lastHandPosDebug = string.Empty;

        private static bool EnsureTypes()
        {
            if (_asm != null) return true;
            _asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (_asm == null) return false;
            _gameStateType = _asm.GetType("GameState");
            return _gameStateType != null;
        }

        /// <summary>
        /// 获取Entity的屏幕坐标
        /// </summary>
        public static bool GetEntityScreenPos(int entityId, out int x, out int y)
        {
            x = y = 0;
            if (!EnsureTypes()) return false;

            // 优先从HandZone查找手牌位置（比Entity反射链更准确）
            if (TryGetHandCardWorldPos(entityId, out var handPos))
            {
                return MouseSimulator.WorldToScreen(
                    GetFloat(handPos, "x"), GetFloat(handPos, "y"), GetFloat(handPos, "z"), out x, out y);
            }

            var worldPos = GetEntityWorldPos(entityId);
            if (worldPos == null) return false;

            var xf = GetFloat(worldPos, "x");
            var yf = GetFloat(worldPos, "y");
            var zf = GetFloat(worldPos, "z");
            return MouseSimulator.WorldToScreen(xf, yf, zf, out x, out y);
        }

        /// <summary>
        /// 获取Entity的世界坐标（反射链）
        /// </summary>
        public static bool GetObjectScreenPos(object obj, out int x, out int y)
        {
            x = y = 0;
            if (!EnsureTypes() || obj == null) return false;

            var pos = GetTransformPos(obj);
            if (pos == null) return false;

            return MouseSimulator.WorldToScreen(
                GetFloat(pos, "x"),
                GetFloat(pos, "y"),
                GetFloat(pos, "z"),
                out x,
                out y);
        }

        public static object GetEntityWorldPos(int entityId)
        {
            if (!EnsureTypes()) return null;
            var gs = InvokeStatic(_gameStateType, "Get");
            if (gs == null) return null;

            var entity = GetEntity(gs, entityId);
            if (entity == null) return null;

            // Entity → Card → Actor → GameObject → Transform → position
            var card = Invoke(entity, "GetCard");
            if (card == null) return null;

            var actor = Invoke(card, "GetActor");
            object go = actor != null ? GetProp(actor, "gameObject") : GetProp(card, "gameObject");
            if (go == null) go = GetProp(card, "gameObject");
            if (go == null) return null;

            var transform = GetProp(go, "transform");
            return transform != null ? GetProp(transform, "position") : null;
        }

        /// <summary>
        /// 通过HandZone遍历手牌，按entityId匹配卡牌的视觉位置
        /// </summary>
        private static bool TryGetHandCardWorldPos(int entityId, out object worldPos)
        {
            worldPos = null;
            try
            {
                var gs = InvokeStatic(_gameStateType, "Get");
                if (gs == null) return false;

                var friendly = Invoke(gs, "GetFriendlySidePlayer")
                    ?? Invoke(gs, "GetFriendlyPlayer")
                    ?? Invoke(gs, "GetLocalPlayer");
                if (friendly == null) return false;

                var handZone = Invoke(friendly, "GetHandZone")
                    ?? Invoke(friendly, "GetHand")
                    ?? GetProp(friendly, "m_handZone")
                    ?? GetProp(friendly, "HandZone");
                if (handZone == null) return false;

                var cards = Invoke(handZone, "GetCards") as IEnumerable
                    ?? GetProp(handZone, "Cards") as IEnumerable
                    ?? GetProp(handZone, "m_cards") as IEnumerable
                    ?? handZone as IEnumerable;
                if (cards == null) return false;

                // 收集所有手牌的 entityId、位置和 card 对象
                var tagType = _asm?.GetType("GAME_TAG");
                var cardList = new System.Collections.Generic.List<(int id, object pos, object card)>();
                foreach (var card in cards)
                {
                    if (card == null) continue;
                    var entity = Invoke(card, "GetEntity");
                    if (entity == null) continue;
                    var id = GetEntityIdFromObject(entity, tagType);
                    var pos = GetTransformPos(card);
                    if (pos != null)
                        cardList.Add((id, pos, card));
                }

                // 找到目标卡牌在手牌中的索引
                int targetIdx = -1;
                for (int i = 0; i < cardList.Count; i++)
                {
                    if (cardList[i].id == entityId)
                    {
                        targetIdx = i;
                        break;
                    }
                }
                if (targetIdx < 0) return false;

                worldPos = cardList[targetIdx].pos;

                // 非最右侧的牌时，计算暴露区域中心进行偏移
                if (targetIdx < cardList.Count - 1)
                {
                    float myX = GetFloat(worldPos, "x");
                    float nextX = GetFloat(cardList[targetIdx + 1].pos, "x");
                    float gap = nextX - myX;
                    if (gap > 0 && gap < 1.5f)
                    {
                        float offset = gap * 0.3f; // 默认兜底
                        float rawHalfWidth = TryGetCardHalfWidth(cardList[targetIdx].card);
                        if (rawHalfWidth > 0)
                        {
                            // Renderer 包含装饰溢出，乘 0.35 近似实际可点击半宽
                            float effectiveHalfWidth = rawHalfWidth * 0.35f;
                            // 暴露区域中心偏移 = 有效半宽 - 间距/2（间距不够时偏左，间距够时几乎不偏）
                            offset = effectiveHalfWidth - gap * 0.5f;
                            if (offset < 0) offset = 0; // 不需要偏移（卡牌不重叠）
                            if (offset > gap * 0.4f) offset = gap * 0.4f;
                        }
                        _lastHandPosDebug = $"idx={targetIdx}/{cardList.Count} gap={gap:F3} rawHW={rawHalfWidth:F3} offset={offset:F3}";

                        worldPos = MakeVector3(myX - offset,
                            GetFloat(worldPos, "y"),
                            GetFloat(worldPos, "z"));
                    }
                }

                return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 尝试获取卡牌的 Renderer 半宽（世界坐标），用于计算手牌重叠的暴露区域。
        /// </summary>
        private static float TryGetCardHalfWidth(object card)
        {
            try
            {
                if (card == null) return 0f;
                var actor = Invoke(card, "GetActor");
                object go = actor != null ? GetProp(actor, "gameObject") : GetProp(card, "gameObject");
                if (go == null) go = GetProp(card, "gameObject");
                if (go == null) return 0f;

                var rendererType = typeof(UnityEngine.Renderer);
                var getComp = go.GetType().GetMethod("GetComponentInChildren", new[] { typeof(Type) });
                if (getComp == null) return 0f;

                var renderer = getComp.Invoke(go, new object[] { rendererType });
                if (renderer == null) return 0f;

                var boundsProp = renderer.GetType().GetProperty("bounds");
                if (boundsProp == null) return 0f;

                var bounds = (UnityEngine.Bounds)boundsProp.GetValue(renderer);
                return bounds.extents.x;
            }
            catch { return 0f; }
        }
        /// </summary>
        public static bool GetHeroPowerScreenPos(out int x, out int y)
        {
            return GetHeroPowerScreenPos(0, out x, out y);
        }

        /// <summary>
        /// 英雄技能位置。sourceHeroPowerEntityId>0 时优先定位指定英雄技能按钮。
        /// </summary>
        public static bool GetHeroPowerScreenPos(int sourceHeroPowerEntityId, out int x, out int y)
        {
            x = y = 0;
            if (!EnsureTypes()) return false;

            var gs = InvokeStatic(_gameStateType, "Get");
            if (gs == null) return false;

            var friendly = Invoke(gs, "GetFriendlySidePlayer")
                ?? Invoke(gs, "GetFriendlyPlayer");
            if (friendly == null) return false;

            var card = ResolveHeroPowerCard(friendly, sourceHeroPowerEntityId);
            if (card == null) return FallbackHeroPower(out x, out y);

            var pos = GetTransformPos(card);
            if (pos == null) return FallbackHeroPower(out x, out y);

            return MouseSimulator.WorldToScreen(
                GetFloat(pos, "x"), GetFloat(pos, "y"), GetFloat(pos, "z"), out x, out y);
        }

        /// <summary>
        /// 英雄位置
        /// </summary>
        public static bool GetHeroScreenPos(bool own, out int x, out int y)
        {
            x = y = 0;
            if (!EnsureTypes()) return false;

            var gs = InvokeStatic(_gameStateType, "Get");
            if (gs == null) return false;

            var player = own
                ? (Invoke(gs, "GetFriendlySidePlayer") ?? Invoke(gs, "GetFriendlyPlayer"))
                : (Invoke(gs, "GetOpposingSidePlayer") ?? Invoke(gs, "GetOpposingPlayer"));
            if (player == null) return FallbackHero(own, out x, out y);

            var hero = Invoke(player, "GetHero");
            if (hero == null) return FallbackHero(own, out x, out y);

            var card = Invoke(hero, "GetCard");
            if (card == null) return FallbackHero(own, out x, out y);

            var pos = GetTransformPos(card);
            if (pos == null) return FallbackHero(own, out x, out y);

            return MouseSimulator.WorldToScreen(
                GetFloat(pos, "x"), GetFloat(pos, "y"), GetFloat(pos, "z"), out x, out y);
        }

        /// <summary>
        /// 结束回合按钮位置
        /// </summary>
        public static bool GetEndTurnButtonScreenPos(out int x, out int y)
        {
            x = y = 0;
            if (!EnsureTypes()) return false;

            var etbType = _asm.GetType("EndTurnButton");
            if (etbType == null) return FallbackEndTurn(out x, out y);

            var etb = InvokeStatic(etbType, "Get");
            if (etb == null) return FallbackEndTurn(out x, out y);

            var pos = GetTransformPos(etb);
            if (pos == null) return FallbackEndTurn(out x, out y);

            if (!MouseSimulator.WorldToScreen(
                GetFloat(pos, "x"), GetFloat(pos, "y"), GetFloat(pos, "z"), out x, out y))
                return FallbackEndTurn(out x, out y);
            return true;
        }

        /// <summary>
        /// 场上放置位置（根据position索引计算）
        /// </summary>
        public static bool GetBoardDropZoneScreenPos(int position, int totalMinions, out int x, out int y)
        {
            x = y = 0;
            // 尝试通过BoardZone获取
            var normalizedPosition = NormalizeBoardDropPosition(position, totalMinions);
            var slots = Math.Max(1, totalMinions + 1);
            if (EnsureTypes())
            {
                var gs = InvokeStatic(_gameStateType, "Get");
                var friendly = gs != null
                    ? (Invoke(gs, "GetFriendlySidePlayer") ?? Invoke(gs, "GetFriendlyPlayer"))
                    : null;
                if (friendly != null)
                {
                    var zone = Invoke(friendly, "GetBattlefieldZone")
                        ?? Invoke(friendly, "GetPlayZone");
                    if (zone != null)
                    {
                        var pos = GetTransformPos(zone);
                        if (pos != null)
                        {
                            // 根据position在zone中心左右偏移
                            float baseX = GetFloat(pos, "x");
                            float baseY = GetFloat(pos, "y");
                            float baseZ = GetFloat(pos, "z");
                            float offset = ((normalizedPosition - 0.5f) - slots / 2.0f) * 1.8f;
                            return MouseSimulator.WorldToScreen(
                                baseX + offset, baseY, baseZ, out x, out y);
                        }
                    }
                }
            }
            return FallbackBoardDrop(normalizedPosition, totalMinions, out x, out y);
        }

        public static bool GetPlayReleaseScreenPos(out int x, out int y)
        {
            x = y = 0;
            if (!EnsureTypes()) return FallbackPlayRelease(out x, out y);

            var gs = InvokeStatic(_gameStateType, "Get");
            if (gs == null) return FallbackPlayRelease(out x, out y);

            var friendly = Invoke(gs, "GetFriendlySidePlayer")
                ?? Invoke(gs, "GetFriendlyPlayer")
                ?? Invoke(gs, "GetLocalPlayer");
            if (friendly == null) return FallbackPlayRelease(out x, out y);

            var zone = Invoke(friendly, "GetBattlefieldZone")
                ?? Invoke(friendly, "GetPlayZone")
                ?? GetProp(friendly, "m_battlefieldZone")
                ?? GetProp(friendly, "BattlefieldZone");
            if (zone == null) return FallbackPlayRelease(out x, out y);

            var pos = GetTransformPos(zone);
            if (pos == null) return FallbackPlayRelease(out x, out y);

            if (!MouseSimulator.WorldToScreen(
                GetFloat(pos, "x"), GetFloat(pos, "y"), GetFloat(pos, "z"), out x, out y))
                return FallbackPlayRelease(out x, out y);

            return true;
        }

        /// <summary>
        /// 我方牌库位置（用于可交易拖拽目标）
        /// </summary>
        public static bool GetFriendlyDeckScreenPos(out int x, out int y)
        {
            x = y = 0;
            if (!EnsureTypes()) return FallbackFriendlyDeck(out x, out y);

            var gs = InvokeStatic(_gameStateType, "Get");
            if (gs == null) return FallbackFriendlyDeck(out x, out y);

            var friendly = Invoke(gs, "GetFriendlySidePlayer")
                ?? Invoke(gs, "GetFriendlyPlayer")
                ?? Invoke(gs, "GetLocalPlayer");
            if (friendly == null) return FallbackFriendlyDeck(out x, out y);

            var deckZone = Invoke(friendly, "GetDeckZone")
                ?? Invoke(friendly, "GetDeck")
                ?? GetProp(friendly, "m_deckZone")
                ?? GetProp(friendly, "DeckZone");
            if (deckZone == null) return FallbackFriendlyDeck(out x, out y);

            var pos = GetTransformPos(deckZone);
            if (pos == null) return FallbackFriendlyDeck(out x, out y);

            if (!MouseSimulator.WorldToScreen(
                GetFloat(pos, "x"), GetFloat(pos, "y"), GetFloat(pos, "z"), out x, out y))
                return FallbackFriendlyDeck(out x, out y);

            return true;
        }

        /// <summary>
        /// 留牌卡牌位置（通过反射获取MulliganManager中的卡牌位置）
        /// </summary>
        public static bool GetMulliganCardScreenPos(int index, int totalCards, out int x, out int y)
        {
            x = y = 0;
            if (!EnsureTypes()) return FallbackMulliganCard(index, totalCards, out x, out y);

            var mmType = _asm.GetType("MulliganManager");
            if (mmType == null) return FallbackMulliganCard(index, totalCards, out x, out y);

            var mm = InvokeStatic(mmType, "Get");
            if (mm == null) return FallbackMulliganCard(index, totalCards, out x, out y);

            var cards = GetProp(mm, "m_startingCards") as IList;
            if (cards == null || index >= cards.Count)
                return FallbackMulliganCard(index, totalCards, out x, out y);

            var card = cards[index];
            if (card == null) return FallbackMulliganCard(index, totalCards, out x, out y);

            var pos = GetTransformPos(card);
            if (pos == null) return FallbackMulliganCard(index, totalCards, out x, out y);

            return MouseSimulator.WorldToScreen(
                GetFloat(pos, "x"), GetFloat(pos, "y"), GetFloat(pos, "z"), out x, out y);
        }

        /// <summary>
        /// 诊断留牌卡牌位置查找失败原因
        /// </summary>
        public static string DiagMulliganCardPos(int index, int totalCards)
        {
            try
            {
                if (!EnsureTypes()) return "ensureTypes=false";
                var mmType = _asm.GetType("MulliganManager");
                if (mmType == null) return "mmType=null";
                var mm = InvokeStatic(mmType, "Get");
                if (mm == null) return "mm=null";
                var cards = GetProp(mm, "m_startingCards") as IList;
                if (cards == null) return "cards=null";
                if (index >= cards.Count) return $"idx={index}>=cnt={cards.Count}";
                var card = cards[index];
                if (card == null) return $"card[{index}]=null";
                var cardType = card.GetType().FullName;
                var go = GetProp(card, "gameObject");
                if (go == null)
                {
                    // 尝试直接获取 transform
                    var tf2 = GetProp(card, "transform");
                    if (tf2 == null) return $"card.gameObject=null,card.transform=null(type={cardType})";
                    var pos2 = GetProp(tf2, "position");
                    return $"card.gameObject=null,transform={tf2!=null},pos={pos2}(type={cardType})";
                }
                var tf = GetProp(go, "transform");
                if (tf == null) return "transform=null";
                var pos = GetProp(tf, "position");
                if (pos == null) return "position=null";
                var xf = GetFloat(pos, "x");
                var yf = GetFloat(pos, "y");
                var zf = GetFloat(pos, "z");
                var w2s = MouseSimulator.WorldToScreen(xf, yf, zf, out var sx, out var sy);
                return $"pos=({xf},{yf},{zf}),w2s={w2s},scr=({sx},{sy})";
            }
            catch (Exception ex)
            {
                return "diagEx=" + ex.Message;
            }
        }

        /// <summary>
        /// 留牌确认按钮位置
        /// </summary>
        public static bool GetMulliganConfirmScreenPos(out int x, out int y)
        {
            x = y = 0;
            if (!EnsureTypes()) return FallbackMulliganConfirm(out x, out y);

            var mmType = _asm.GetType("MulliganManager");
            if (mmType == null) return FallbackMulliganConfirm(out x, out y);

            var mm = InvokeStatic(mmType, "Get");
            if (mm == null) return FallbackMulliganConfirm(out x, out y);

            // 尝试获取确认按钮对象
            var btn = GetProp(mm, "m_mulliganButton")
                ?? GetProp(mm, "m_confirmButton")
                ?? GetProp(mm, "m_doneButton");
            if (btn != null)
            {
                var pos = GetTransformPos(btn);
                if (pos != null)
                    return MouseSimulator.WorldToScreen(
                        GetFloat(pos, "x"), GetFloat(pos, "y"), GetFloat(pos, "z"), out x, out y);
            }

            return FallbackMulliganConfirm(out x, out y);
        }

        /// <summary>
        /// 获取当前手牌中所有卡牌的EntityId列表（用于诊断）
        /// </summary>
        public static System.Collections.Generic.List<int> GetHandEntityIds()
        {
            var result = new System.Collections.Generic.List<int>();
            try
            {
                if (!EnsureTypes()) return result;
                var gs = InvokeStatic(_gameStateType, "Get");
                if (gs == null) return result;
                var friendly = Invoke(gs, "GetFriendlySidePlayer")
                    ?? Invoke(gs, "GetFriendlyPlayer")
                    ?? Invoke(gs, "GetLocalPlayer");
                if (friendly == null) return result;
                var handZone = Invoke(friendly, "GetHandZone")
                    ?? Invoke(friendly, "GetHand")
                    ?? GetProp(friendly, "m_handZone")
                    ?? GetProp(friendly, "HandZone");
                if (handZone == null) return result;
                var cards = Invoke(handZone, "GetCards") as IEnumerable
                    ?? GetProp(handZone, "Cards") as IEnumerable
                    ?? GetProp(handZone, "m_cards") as IEnumerable
                    ?? handZone as IEnumerable;
                if (cards == null) return result;
                var tagType = _asm?.GetType("GAME_TAG");
                foreach (var card in cards)
                {
                    if (card == null) continue;
                    var entity = Invoke(card, "GetEntity");
                    if (entity == null) continue;
                    result.Add(GetEntityIdFromObject(entity, tagType));
                }
            }
            catch { }
            return result;
        }

        #region Fallback固定比例坐标

        private static bool FallbackMulliganCard(int index, int totalCards, out int x, out int y)
        {
            int w = MouseSimulator.GetScreenWidth();
            int h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) { x = y = 0; return false; }
            // 留牌卡牌水平居中排列，间距约屏幕宽度15%
            float centerX = 0.5f;
            float spacing = 0.15f;
            float startX = centerX - (totalCards - 1) * spacing / 2f;
            x = (int)(w * (startX + index * spacing));
            y = (int)(h * 0.5);
            return true;
        }

        private static bool FallbackMulliganConfirm(out int x, out int y)
        {
            int w = MouseSimulator.GetScreenWidth();
            int h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) { x = y = 0; return false; }
            x = (int)(w * 0.5);
            y = (int)(h * 0.8);
            return true;
        }

        private static bool FallbackHeroPower(out int x, out int y)
        {
            int w = MouseSimulator.GetScreenWidth();
            int h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) { x = y = 0; return false; }
            x = (int)(w * 0.655);
            y = (int)(h * 0.785);
            return true;
        }

        private static bool FallbackHero(bool own, out int x, out int y)
        {
            int w = MouseSimulator.GetScreenWidth();
            int h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) { x = y = 0; return false; }
            x = (int)(w * 0.5);
            y = own ? (int)(h * 0.82) : (int)(h * 0.18);
            return true;
        }

        private static bool FallbackEndTurn(out int x, out int y)
        {
            int w = MouseSimulator.GetScreenWidth();
            int h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) { x = y = 0; return false; }
            x = (int)(w * 0.78);
            y = (int)(h * 0.46);
            return true;
        }

        private static bool FallbackBoardDrop(int position, int totalMinions, out int x, out int y)
        {
            int w = MouseSimulator.GetScreenWidth();
            int h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) { x = y = 0; return false; }
            float slots = Math.Max(1, totalMinions + 1);
            float ratio = (position - 0.5f) / slots;
            x = (int)(w * (0.25 + ratio * 0.5));
            y = (int)(h * 0.55);
            return true;
        }

        private static bool FallbackPlayRelease(out int x, out int y)
        {
            int w = MouseSimulator.GetScreenWidth();
            int h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) { x = y = 0; return false; }
            x = (int)(w * 0.5);
            y = (int)(h * 0.56);
            return true;
        }

        private static bool FallbackFriendlyDeck(out int x, out int y)
        {
            int w = MouseSimulator.GetScreenWidth();
            int h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) { x = y = 0; return false; }
            x = (int)(w * 0.88);
            y = (int)(h * 0.79);
            return true;
        }

        #endregion

        #region 反射工具

        private static object GetTransformPos(object obj)
        {
            if (obj == null) return null;
            var go = GetProp(obj, "gameObject");
            if (go == null)
            {
                // obj本身可能就是GameObject
                var transform = GetProp(obj, "transform");
                return transform != null ? GetProp(transform, "position") : null;
            }
            var tf = GetProp(go, "transform");
            return tf != null ? GetProp(tf, "position") : null;
        }

        /// <summary>
        /// 构造一个带 x/y/z 字段的轻量对象，供 GetFloat 读取
        /// </summary>
        private static object MakeVector3(float x, float y, float z)
        {
            return new SimpleVector3 { x = x, y = y, z = z };
        }

        private static object ResolveHeroPowerCard(object friendly, int sourceHeroPowerEntityId)
        {
            if (friendly == null)
                return null;

            var heroPower = Invoke(friendly, "GetHeroPower");
            var heroPowerEntity = Invoke(heroPower, "GetEntity") ?? heroPower;
            if (sourceHeroPowerEntityId <= 0 || GetEntityIdFromObject(heroPowerEntity, _gameStateType?.Assembly.GetType("GAME_TAG")) == sourceHeroPowerEntityId)
            {
                var primaryCard = Invoke(heroPowerEntity, "GetCard") ?? Invoke(heroPower, "GetCard");
                if (primaryCard != null)
                    return primaryCard;
            }

            var gs = InvokeStatic(_gameStateType, "Get");
            var requestedEntity = gs != null ? GetEntity(gs, sourceHeroPowerEntityId) : null;
            if (requestedEntity == null)
                return Invoke(heroPowerEntity, "GetCard") ?? Invoke(heroPower, "GetCard");

            var controller = Invoke(requestedEntity, "GetController") ?? friendly;
            var additionalIndex = GetTagValue(requestedEntity, "ADDITIONAL_HERO_POWER_INDEX");
            if (additionalIndex > 0)
            {
                var indexedCard = InvokeWithIntArg(controller, "GetHeroPowerCardWithIndex", additionalIndex);
                if (indexedCard != null)
                    return indexedCard;
            }

            var directCard = Invoke(requestedEntity, "GetCard");
            if (directCard != null)
                return directCard;

            return Invoke(controller, "GetHeroPowerCard")
                ?? Invoke(heroPowerEntity, "GetCard")
                ?? Invoke(heroPower, "GetCard");
        }

        private struct SimpleVector3
        {
            public float x, y, z;
        }

        private static float GetFloat(object vector3, string field)
        {
            if (vector3 == null) return 0;
            var f = vector3.GetType().GetField(field);
            return f != null ? (float)f.GetValue(vector3) : 0;
        }

        private static int GetTagValue(object entity, string tagName)
        {
            if (entity == null || string.IsNullOrWhiteSpace(tagName) || !EnsureTypes())
                return 0;

            try
            {
                var tagType = _gameStateType?.Assembly.GetType("GAME_TAG");
                if (tagType == null)
                    return 0;

                var tagValue = Enum.Parse(tagType, tagName, true);
                var tagInt = Convert.ToInt32(tagValue);
                var getTag = entity.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(method => method.Name == "GetTag" && method.GetParameters().Length == 1);
                if (getTag == null)
                    return 0;

                var paramType = getTag.GetParameters()[0].ParameterType;
                object arg = paramType.IsEnum
                    ? (paramType == tagType ? tagValue : Enum.ToObject(paramType, tagInt))
                    : Convert.ChangeType(tagInt, paramType);
                var result = getTag.Invoke(entity, new[] { arg });
                return result != null ? Convert.ToInt32(result) : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static int NormalizeBoardDropPosition(int position, int totalMinions)
        {
            var slots = Math.Max(1, totalMinions + 1);
            if (position <= 0)
                return slots;
            if (position > slots)
                return slots;
            return position;
        }

        private static object GetEntity(object gs, int entityId)
        {
            if (gs == null || entityId <= 0) return null;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var name in new[] { "GetEntity", "GetEntityByID", "FindEntity" })
            {
                var mi = gs.GetType().GetMethods(flags)
                    .FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(int));
                if (mi == null) continue;
                try
                {
                    var e = mi.Invoke(gs, new object[] { entityId });
                    if (e != null) return e;
                }
                catch { }
            }
            // fallback：遍历所有实体，按 ENTITY_ID tag 匹配
            return FindEntityByIdFallback(gs, entityId);
        }

        private static object FindEntityByIdFallback(object gs, int entityId)
        {
            try
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                // 尝试获取实体字典/列表
                foreach (var name in new[] { "GetAllEntities", "GetEntities", "m_entityMap", "m_entities" })
                {
                    var mi = gs.GetType().GetMethod(name, flags, null, Type.EmptyTypes, null);
                    object collection = mi != null ? mi.Invoke(gs, null) : null;
                    if (collection == null)
                    {
                        var fi = gs.GetType().GetField(name, flags);
                        collection = fi?.GetValue(gs);
                    }
                    if (collection == null) continue;

                    var enumerable = collection as System.Collections.IEnumerable;
                    if (enumerable == null) continue;

                    var tagType = _gameStateType?.Assembly.GetType("GAME_TAG");
                    foreach (var item in enumerable)
                    {
                        if (item == null) continue;
                        // 字典的 Value
                        var val = item;
                        var valProp = item.GetType().GetProperty("Value");
                        if (valProp != null) val = valProp.GetValue(item);
                        if (val == null) continue;

                        int id = GetEntityIdFromObject(val, tagType);
                        if (id == entityId) return val;
                    }
                }
            }
            catch { }
            return null;
        }

        private static int GetEntityIdFromObject(object entity, Type tagType)
        {
            try
            {
                if (tagType != null)
                {
                    var tagVal = Enum.Parse(tagType, "ENTITY_ID", true);
                    var getTag = entity.GetType().GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "GetTag" && m.GetParameters().Length == 1);
                    if (getTag != null)
                    {
                        var arg = Convert.ChangeType(tagVal, getTag.GetParameters()[0].ParameterType);
                        return Convert.ToInt32(getTag.Invoke(entity, new[] { arg }));
                    }
                }
                // 直接读字段
                foreach (var name in new[] { "EntityID", "EntityId", "ID", "Id" })
                {
                    var fi = entity.GetType().GetField(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fi != null) return Convert.ToInt32(fi.GetValue(entity));
                    var pi = entity.GetType().GetProperty(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pi != null) return Convert.ToInt32(pi.GetValue(entity));
                }
            }
            catch { }
            return 0;
        }

        private static object Invoke(object obj, string method)
        {
            if (obj == null) return null;
            var mi = obj.GetType().GetMethod(method,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return mi?.Invoke(obj, null);
        }

        private static object InvokeWithIntArg(object obj, string method, int arg)
        {
            if (obj == null || string.IsNullOrWhiteSpace(method))
                return null;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var mi = obj.GetType().GetMethods(flags)
                .FirstOrDefault(candidate =>
                    candidate.Name.Equals(method, StringComparison.OrdinalIgnoreCase)
                    && candidate.GetParameters().Length == 1
                    && IsNumericParameterType(candidate.GetParameters()[0].ParameterType));
            if (mi == null)
                return null;

            try
            {
                var parameterType = mi.GetParameters()[0].ParameterType;
                var argValue = Convert.ChangeType(arg, parameterType);
                return mi.Invoke(obj, new[] { argValue });
            }
            catch
            {
                return null;
            }
        }

        private static object InvokeStatic(Type type, string method)
        {
            if (type == null) return null;
            var mi = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return mi?.Invoke(null, null);
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null) return null;
            var type = obj.GetType();
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null) return prop.GetValue(obj);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(obj);
        }

        private static bool IsNumericParameterType(Type type)
        {
            return type == typeof(int)
                || type == typeof(short)
                || type == typeof(byte)
                || type == typeof(long);
        }

        #endregion
    }
}
