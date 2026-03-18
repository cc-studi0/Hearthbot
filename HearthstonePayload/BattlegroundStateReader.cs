using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HearthstonePayload
{
    /// <summary>
    /// 在炉石进程内通过反射读取战旗（Battlegrounds）模式的状态。
    /// 与 GameReader 完全独立，不共享构筑模式的数据结构。
    /// </summary>
    public class BattlegroundStateReader
    {
        private ReflectionContext _ctx;

        private bool Init()
        {
            if (_ctx != null && _ctx.IsReady) return true;
            _ctx = ReflectionContext.Instance;
            return _ctx.Init();
        }

        public BattlegroundStateData ReadState()
        {
            if (!Init()) return null;

            try
            {
                var gameState = _ctx.CallStaticAny(_ctx.GameStateType, "Get");
                if (gameState == null) return null;

                var data = new BattlegroundStateData();
                var friendly = GetFriendlyPlayer(gameState);
                var opposing = GetOpposingPlayer(gameState);
                if (friendly == null) return null;

                ReadPhaseAndTurn(gameState, friendly, data);
                ReadEconomy(friendly, data);
                ReadTavern(friendly, data);
                ReadTimewarp(gameState, friendly, data);
                ReadHero(friendly, data);
                ReadHeroPower(friendly, data);
                ReadShop(gameState, friendly, data);
                ReadHand(gameState, friendly, data);
                ReadBoard(gameState, friendly, data);
                ReadGameOver(gameState, friendly, opposing, data);
                ReadPlacement(friendly, data);
                NormalizePhase(data);

                return data;
            }
            catch
            {
                return null;
            }
        }

        // ── 阶段与回合 ──

        private void ReadPhaseAndTurn(object gameState, object friendly, BattlegroundStateData data)
        {
            try
            {
                var gameEntity = _ctx.CallAny(gameState, "GetGameEntity");
                if (gameEntity == null) return;

                data.Turn = _ctx.GetTagValue(gameEntity, "TURN");
                data.Step = _ctx.GetTagValue(gameEntity, "STEP");
                data.NextStep = _ctx.GetTagValue(gameEntity, "NEXT_STEP");

                // STEP tag 判断阶段
                var step = data.Step;

                var currentPlayerId = _ctx.GetTagValue(gameEntity, "CURRENT_PLAYER");
                var friendlyPlayerId = ResolvePlayerId(friendly);
                data.IsOurTurn = currentPlayerId > 0 && currentPlayerId == friendlyPlayerId;

                var stepDecision = IsActionStep(step);
                if (data.IsOurTurn && stepDecision.GetValueOrDefault())
                    data.Phase = "RECRUIT";
                else if (!data.IsOurTurn)
                    data.Phase = "COMBAT";
                else
                    data.Phase = "RECRUIT";

                // 战旗开局英雄选择本质上挂在 MulliganManager 上，单看 tag 容易漏判。
                if (IsHeroPickActive(gameState, friendly))
                {
                    data.Phase = "HERO_PICK";
                    data.IsOurTurn = true;
                }
            }
            catch { }
        }

        // ── 经济 ──

        private void ReadEconomy(object friendly, BattlegroundStateData data)
        {
            try
            {
                var playerEntity = GetPlayerEntity(friendly);
                if (playerEntity == null) return;

                // 战旗使用 RESOURCES / RESOURCES_USED tag 表示金币
                data.MaxGold = _ctx.GetTagValue(playerEntity, "RESOURCES");
                var used = _ctx.GetTagValue(playerEntity, "RESOURCES_USED");
                data.Gold = Math.Max(0, data.MaxGold - used);

                // 也尝试直接读取 TEMP_RESOURCES (额外金币)
                var temp = _ctx.GetTagValue(playerEntity, "TEMP_RESOURCES");
                data.Gold += Math.Max(0, temp);
            }
            catch { }
        }

        // ── 酒馆信息 ──

        private void ReadTavern(object friendly, BattlegroundStateData data)
        {
            try
            {
                var playerEntity = GetPlayerEntity(friendly);
                if (playerEntity == null) return;

                data.TavernTier = _ctx.GetTagValue(playerEntity, "PLAYER_TECH_LEVEL");
                if (data.TavernTier <= 0)
                    data.TavernTier = 1;

                // 升级费用
                data.UpgradeCost = _ctx.GetTagValue(playerEntity, "BACON_SHOP_UPGRADE_COST");
                if (data.UpgradeCost <= 0)
                {
                    // 回退：基于酒馆等级计算默认费用
                    data.UpgradeCost = GetDefaultUpgradeCost(data.TavernTier);
                }

                data.CanUpgrade = data.TavernTier < 6 && data.Gold >= data.UpgradeCost;

                // 刷新费用（通常固定为 1）
                data.RerollCost = _ctx.GetTagValue(playerEntity, "BACON_SHOP_REFRESH_COST");
                if (data.RerollCost <= 0 && data.Phase == "RECRUIT")
                    data.RerollCost = 1;

                data.CanReroll = data.Gold >= data.RerollCost && data.Phase == "RECRUIT";

                // 冻结状态
                data.IsFrozen = _ctx.GetTagValue(playerEntity, "BACON_SHOP_IS_FROZEN") == 1;
            }
            catch { }
        }

        // ── 英雄 ──

        private void ReadHero(object friendly, BattlegroundStateData data)
        {
            try
            {
                var hero = _ctx.CallAny(friendly, "GetHero");
                if (hero == null) return;

                data.HeroEntityId = ResolveEntityId(hero);
                data.HeroCardId = ResolveCardId(hero) ?? "";
                data.HeroPlayState = _ctx.GetTagValue(hero, "PLAYSTATE");
                data.HeroHealth = _ctx.GetTagValue(hero, "HEALTH");
                data.HeroArmor = _ctx.GetTagValue(hero, "ARMOR");

                var damage = _ctx.GetTagValue(hero, "DAMAGE");
                data.HeroHealth = Math.Max(0, data.HeroHealth - damage);
            }
            catch { }
        }

        // ── 英雄技能 ──

        private void ReadHeroPower(object friendly, BattlegroundStateData data)
        {
            try
            {
                data.HeroPowers.Clear();

                var heroPower = _ctx.CallAny(friendly, "GetHeroPower");
                TryAddHeroPower(heroPower, 0, data);

                for (var index = 1; index <= 4; index++)
                {
                    var indexedHeroPowerCard = CallMethodWithIntArg(friendly, index, "GetHeroPowerCardWithIndex");
                    TryAddHeroPower(indexedHeroPowerCard, index, data);
                }

                if (data.HeroPowers.Count > 0)
                {
                    data.HeroPowers = data.HeroPowers
                        .OrderBy(power => power.Index)
                        .ThenBy(power => power.EntityId)
                        .ToList();
                }

                var primaryHeroPower = data.HeroPowers.FirstOrDefault(power => power.Index <= 0)
                    ?? data.HeroPowers.FirstOrDefault();
                if (primaryHeroPower == null)
                    return;

                data.HeroPowerEntityId = primaryHeroPower.EntityId;
                data.HeroPowerCardId = primaryHeroPower.CardId ?? "";
                data.HeroPowerCost = primaryHeroPower.Cost;
                data.HeroPowerAvailable = primaryHeroPower.IsAvailable;
            }
            catch { }
        }

        // ── 商店 ──

        private void ReadShop(object gameState, object friendly, BattlegroundStateData data)
        {
            try
            {
                var friendlyPlayerId = ResolvePlayerId(friendly);
                var baconDummyPlayerId = TryGetBaconDummyPlayerId(gameState);
                var entities = _ctx.GetEntityMapEntries(gameState);

                foreach (var entity in entities)
                {
                    if (entity == null) continue;

                    var zone = _ctx.GetTagValue(entity, "ZONE");
                    var controller = _ctx.GetTagValue(entity, "CONTROLLER");
                    var zonePosition = _ctx.GetTagValue(entity, "ZONE_POSITION");
                    var cardType = _ctx.GetTagValue(entity, "CARDTYPE");
                    if (!IsSupportedShopCardType(cardType) || zonePosition <= 0)
                        continue;

                    var belongsToBob = baconDummyPlayerId > 0
                        ? controller == baconDummyPlayerId
                        : controller != friendlyPlayerId;
                    var isShopZone = zone == 1 || zone == 5;
                    if (belongsToBob && isShopZone)
                    {
                        var minion = ReadMinion(entity);
                        if (minion != null && data.ShopCards.All(existing => existing.EntityId != minion.EntityId))
                            data.ShopCards.Add(minion);
                    }
                }

                // 按 ZonePosition 排序
                data.ShopCards = data.ShopCards.OrderBy(m => m.ZonePosition).ToList();
            }
            catch { }
        }

        // ── 手牌 ──

        private void ReadHand(object gameState, object friendly, BattlegroundStateData data)
        {
            try
            {
                // 手牌区域
                var hand = _ctx.CallAny(friendly,
                    "GetHandZone",
                    "GetHand",
                    "HandZone",
                    "m_handZone");

                if (hand != null)
                {
                    var cards = _ctx.CallGetCards(hand);
                    foreach (var card in cards)
                    {
                        var entity = GetEntity(card);
                        if (entity == null) entity = card;
                        var minion = ReadMinion(entity);
                        if (minion != null)
                            data.HandCards.Add(minion);
                    }
                }

                // 回退：从 EntityMap 扫描 ZONE=HAND 的实体
                if (data.HandCards.Count == 0)
                {
                    var friendlyPlayerId = ResolvePlayerId(friendly);
                    var entities = _ctx.GetEntityMapEntries(gameState);
                    foreach (var entity in entities)
                    {
                        if (entity == null) continue;
                        var zone = _ctx.GetTagValue(entity, "ZONE");
                        var controller = _ctx.GetTagValue(entity, "CONTROLLER");
                        if (zone == 3 && controller == friendlyPlayerId) // HAND=3
                        {
                            var minion = ReadMinion(entity);
                            if (minion != null)
                                data.HandCards.Add(minion);
                        }
                    }
                }

                data.HandCards = data.HandCards.OrderBy(m => m.ZonePosition).ToList();
            }
            catch { }
        }

        // ── 场上随从 ──

        private void ReadBoard(object gameState, object friendly, BattlegroundStateData data)
        {
            try
            {
                var battlefield = _ctx.CallAny(friendly,
                    "GetBattlefieldZone",
                    "GetBattlefield",
                    "BattlefieldZone",
                    "PlayZone",
                    "m_playZone");

                if (battlefield != null)
                {
                    var cards = _ctx.CallGetCards(battlefield);
                    foreach (var card in cards)
                    {
                        var entity = GetEntity(card);
                        if (entity == null) entity = card;
                        var minion = ReadMinion(entity);
                        if (minion != null)
                            data.BoardMinions.Add(minion);
                    }
                }

                if (data.BoardMinions.Count == 0)
                {
                    var friendlyPlayerId = ResolvePlayerId(friendly);
                    var entities = _ctx.GetEntityMapEntries(gameState);
                    foreach (var entity in entities)
                    {
                        if (entity == null) continue;

                        var zone = _ctx.GetTagValue(entity, "ZONE");
                        var controller = _ctx.GetTagValue(entity, "CONTROLLER");
                        var cardType = _ctx.GetTagValue(entity, "CARDTYPE");
                        var zonePosition = _ctx.GetTagValue(entity, "ZONE_POSITION");
                        if (zone == 1 && controller == friendlyPlayerId && cardType == 4 && zonePosition > 0)
                        {
                            var minion = ReadMinion(entity);
                            if (minion != null && data.BoardMinions.All(existing => existing.EntityId != minion.EntityId))
                                data.BoardMinions.Add(minion);
                        }
                    }
                }

                data.BoardMinions = data.BoardMinions.OrderBy(m => m.ZonePosition).ToList();
            }
            catch { }
        }

        // ── 对局结束判断 ──

        private void ReadGameOver(object gameState, object friendly, object opposing, BattlegroundStateData data)
        {
            try
            {
                var hero = _ctx.CallAny(friendly, "GetHero");
                if (hero != null && data.HeroPlayState <= 0)
                    data.HeroPlayState = _ctx.GetTagValue(hero, "PLAYSTATE");

                if (data.Step <= 0 || data.NextStep <= 0)
                {
                    var gameEntity = _ctx.CallAny(gameState, "GetGameEntity");
                    if (gameEntity != null)
                    {
                        if (data.Step <= 0)
                            data.Step = _ctx.GetTagValue(gameEntity, "STEP");
                        if (data.NextStep <= 0)
                            data.NextStep = _ctx.GetTagValue(gameEntity, "NEXT_STEP");
                    }
                }

                var hasTerminalPlayState = TryMapTerminalPlayStateToResult(data.HeroPlayState, out var terminalResult);
                if (hasTerminalPlayState && string.Equals(data.GameResult, "NONE", StringComparison.OrdinalIgnoreCase))
                    data.GameResult = terminalResult;

                if (TryGetEndGameScreenState(out var endgameShown, out var endgameClass, out _))
                {
                    data.IsEndGameScreenVisible = endgameShown;

                    if (string.Equals(data.GameResult, "NONE", StringComparison.OrdinalIgnoreCase)
                        && TryMapEndGameClassToResult(endgameClass, out var inferredResult))
                    {
                        data.GameResult = inferredResult;
                    }
                }

                var hasFinalStep = IsFinalStep(data.Step) || IsFinalStep(data.NextStep);
                data.IsGameOver = data.IsEndGameScreenVisible
                    || (hasTerminalPlayState && hasFinalStep);
            }
            catch { }
        }

        // ── 名次 ──

        private void ReadPlacement(object friendly, BattlegroundStateData data)
        {
            try
            {
                var playerEntity = GetPlayerEntity(friendly);
                var heroEntity = _ctx.CallAny(friendly, "GetHero");

                data.Placement = TryReadPlacementFromTags(playerEntity, heroEntity, friendly);

                if (data.Placement <= 0
                    && TryGetEndGameScreenState(out var endgameShown, out _, out var endGameScreen)
                    && endgameShown)
                {
                    data.Placement = TryReadPlacementFromEndGameScreen(endGameScreen);
                }

                if (data.Placement <= 0
                    && string.Equals(data.GameResult, "WIN", StringComparison.OrdinalIgnoreCase))
                {
                    data.Placement = 1;
                }

                data.PlayerCount = TryReadPlayerCount(playerEntity, heroEntity, friendly);
            }
            catch { }
        }

        private int TryReadPlacementFromTags(params object[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (candidate == null)
                    continue;

                var placement = _ctx.GetTagValue(candidate, "PLAYER_LEADERBOARD_PLACE");
                if (placement > 0)
                    return placement;

                placement = _ctx.GetTagValue(candidate, "BACON_PLAYER_RESULT_PLACE");
                if (placement > 0)
                    return placement;
            }

            return 0;
        }

        private int TryReadPlayerCount(params object[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (candidate == null)
                    continue;

                var count = _ctx.GetTagValue(candidate, "BACON_PLAYER_NUM_REMAINING");
                if (count > 0)
                    return count;
            }

            return 0;
        }

        private bool TryGetEndGameScreenState(out bool shown, out string className, out object screen)
        {
            shown = false;
            className = string.Empty;
            screen = null;
            try
            {
                var type = _ctx?.AsmCSharp?.GetType("EndGameScreen");
                if (type == null)
                    return false;

                screen = _ctx.CallStaticAny(type, "Get");
                if (screen == null)
                    return false;

                var shownObj = _ctx.GetFieldOrPropertyAny(
                    screen,
                    "m_shown",
                    "shown",
                    "IsShown",
                    "m_isShown");

                if (shownObj is bool boolShown)
                    shown = boolShown;
                else if (shownObj != null)
                    shown = _ctx.GetTagValue(shownObj, "VALUE") == 1 || SafeToInt(shownObj) == 1;

                className = _ctx.GetFieldOrPropertyAny(
                    screen,
                    "RealClassName",
                    "m_realClassName",
                    "ClassName",
                    "m_className")?.ToString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(className))
                {
                    className = _ctx.CallAny(screen, "GetRealClassName", "GetClassName")?.ToString() ?? string.Empty;
                }

                if (!shown && IsEndGameScreenProbablyVisible(screen, className))
                    shown = true;

                return true;
            }
            catch
            {
                screen = null;
                shown = false;
                className = string.Empty;
                return false;
            }
        }

        private bool IsEndGameScreenProbablyVisible(object screen, string className)
        {
            if (screen == null)
                return false;

            if (!string.IsNullOrWhiteSpace(className) && IsObjectProbablyVisible(screen))
                return true;

            var candidates = new[]
            {
                _ctx.GetFieldOrPropertyAny(screen, "m_widget", "Widget", "m_root", "m_gameObject", "gameObject"),
                _ctx.GetFieldOrPropertyAny(screen, "m_battlegroundsEndGame", "m_battlegroundsPanel", "m_battlegroundsResults", "m_summaryDisplay"),
                _ctx.GetFieldOrPropertyAny(screen, "m_placeText", "m_placementText", "m_placeLabel", "m_placementLabel"),
                _ctx.GetFieldOrPropertyAny(screen, "m_rankText", "m_rankLabel", "m_resultText")
            };

            return candidates.Any(IsObjectProbablyVisible);
        }

        private bool IsObjectProbablyVisible(object obj)
        {
            if (obj == null)
                return false;

            foreach (var name in new[] { "m_shown", "shown", "IsShown", "m_isShown", "m_visible", "visible", "isVisible" })
            {
                if (TryGetBoolLike(obj, name, out var value) && !value)
                    return false;
            }

            var gameObject = _ctx.GetFieldOrPropertyAny(obj, "gameObject", "GameObject", "m_gameObject", "m_root", "m_RootObject");
            if (gameObject != null)
            {
                foreach (var name in new[] { "activeSelf", "activeInHierarchy" })
                {
                    if (TryGetBoolLike(gameObject, name, out var active) && !active)
                        return false;
                }
            }

            return true;
        }

        private bool TryGetBoolLike(object obj, string name, out bool value)
        {
            value = false;
            if (obj == null || string.IsNullOrWhiteSpace(name))
                return false;

            var raw = _ctx.GetFieldOrPropertyAny(obj, name);
            if (raw is bool fieldBool)
            {
                value = fieldBool;
                return true;
            }

            var called = _ctx.CallAny(obj, name);
            if (called is bool methodBool)
            {
                value = methodBool;
                return true;
            }

            return false;
        }

        private static bool TryMapEndGameClassToResult(string endgameClass, out string result)
        {
            result = "NONE";
            if (string.IsNullOrWhiteSpace(endgameClass))
                return false;

            var lower = endgameClass.ToLowerInvariant();
            if (lower.Contains("victory"))
            {
                result = "WIN";
                return true;
            }

            if (lower.Contains("defeat"))
            {
                result = "LOSS";
                return true;
            }

            if (lower.Contains("tie") || lower.Contains("draw"))
            {
                result = "TIE";
                return true;
            }

            return false;
        }

        private static bool TryMapTerminalPlayStateToResult(int playState, out string result)
        {
            result = "NONE";

            // PLAYSTATE: PLAYING=1, WINNING=2, LOSING=3, WON=4, LOST=5, TIED=6, DISCONNECTED=7, CONCEDED=8
            switch (playState)
            {
                case 4:
                    result = "WIN";
                    return true;
                case 5:
                case 7:
                case 8:
                    result = "LOSS";
                    return true;
                case 6:
                    result = "TIE";
                    return true;
                default:
                    return false;
            }
        }

        private int TryReadPlacementFromEndGameScreen(object endGameScreen)
        {
            if (endGameScreen == null)
                return 0;

            foreach (var target in EnumeratePlacementCandidates(endGameScreen))
            {
                if (TryReadPlacementFromObject(target, out var placement))
                    return placement;
            }

            return 0;
        }

        private IEnumerable<object> EnumeratePlacementCandidates(object endGameScreen)
        {
            if (endGameScreen == null)
                yield break;

            yield return endGameScreen;

            var directChildren = new[]
            {
                _ctx.GetFieldOrPropertyAny(endGameScreen, "m_battlegroundsEndGame", "m_battlegroundsPanel", "m_battlegroundsResults", "m_summaryDisplay"),
                _ctx.GetFieldOrPropertyAny(endGameScreen, "m_placeText", "m_placementText", "m_placeLabel", "m_placementLabel"),
                _ctx.GetFieldOrPropertyAny(endGameScreen, "m_rankText", "m_rankLabel", "m_resultText")
            };

            foreach (var child in directChildren)
            {
                if (child != null)
                    yield return child;
            }
        }

        private bool TryReadPlacementFromObject(object candidate, out int placement)
        {
            placement = 0;
            if (candidate == null)
                return false;

            var directValue = _ctx.GetFieldOrPropertyAny(
                candidate,
                "m_place",
                "Place",
                "m_placement",
                "Placement",
                "m_rank",
                "Rank",
                "m_playerResultPlace",
                "m_leaderboardPlace");

            if (TryParsePlacementValue(directValue, out placement))
                return true;

            var textValue = _ctx.GetFieldOrPropertyAny(
                candidate,
                "Text",
                "text",
                "m_text",
                "m_Text",
                "Label",
                "m_label",
                "m_Label");

            if (TryParsePlacementValue(textValue, out placement))
                return true;

            var methodValue = _ctx.CallAny(candidate, "GetText", "GetLabelText", "GetTextString");
            return TryParsePlacementValue(methodValue, out placement);
        }

        private bool TryParsePlacementValue(object value, out int placement)
        {
            placement = 0;
            if (value == null)
                return false;

            if (TryParsePlacementText(value.ToString(), out placement))
                return true;

            placement = SafeToInt(value);
            return placement > 0;
        }

        private bool TryParsePlacementText(string text, out int placement)
        {
            placement = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(digits) && int.TryParse(digits, out placement) && placement > 0)
                return true;

            var firstIndex = text.IndexOf("第一名", StringComparison.Ordinal);
            if (firstIndex >= 0) { placement = 1; return true; }
            if (text.IndexOf("第二名", StringComparison.Ordinal) >= 0) { placement = 2; return true; }
            if (text.IndexOf("第三名", StringComparison.Ordinal) >= 0) { placement = 3; return true; }
            if (text.IndexOf("第四名", StringComparison.Ordinal) >= 0) { placement = 4; return true; }
            if (text.IndexOf("第五名", StringComparison.Ordinal) >= 0) { placement = 5; return true; }
            if (text.IndexOf("第六名", StringComparison.Ordinal) >= 0) { placement = 6; return true; }
            if (text.IndexOf("第七名", StringComparison.Ordinal) >= 0) { placement = 7; return true; }
            if (text.IndexOf("第八名", StringComparison.Ordinal) >= 0) { placement = 8; return true; }

            return false;
        }

        private static int SafeToInt(object value)
        {
            if (value == null)
                return 0;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        // ── 时空扭曲（Alt Tavern） ──

        private void ReadTimewarp(object gameState, object friendly, BattlegroundStateData data)
        {
            try
            {
                var gameEntity = _ctx.CallAny(gameState, "GetGameEntity");
                if (gameEntity == null) return;

                data.IsTimewarpActive = _ctx.GetTagValue(gameEntity, "BACON_ALT_TAVERN_IN_PROGRESS") != 0;

                if (!data.IsTimewarpActive) return;

                // 读取时空扭曲专属货币
                var playerEntity = GetPlayerEntity(friendly);
                if (playerEntity != null)
                {
                    data.TimewarpCoins = _ctx.GetTagValue(playerEntity, "BACON_ALT_TAVERN_COIN");
                    data.TimewarpCoinsUsed = _ctx.GetTagValue(playerEntity, "BACON_ALT_TAVERN_COIN_USED");
                }

                // 时空扭曲商店的卡牌通过 Choice 系统呈现（SECOND_SHOP 区域），
                // ReadShop 基于 Bob 的控制权扫描可能无法捕获。
                // 此处扫描所有带 BACON_TIMEWARPED tag 的实体，补充到 ShopCards。
                var friendlyPlayerId = ResolvePlayerId(friendly);
                var entities = _ctx.GetEntityMapEntries(gameState);
                foreach (var entity in entities)
                {
                    if (entity == null) continue;
                    var timewarped = _ctx.GetTagValue(entity, "BACON_TIMEWARPED");
                    if (timewarped != 1) continue;

                    var controller = _ctx.GetTagValue(entity, "CONTROLLER");
                    var zone = _ctx.GetTagValue(entity, "ZONE");
                    var zonePosition = _ctx.GetTagValue(entity, "ZONE_POSITION");
                    var cardType = _ctx.GetTagValue(entity, "CARDTYPE");

                    // 仅采集可购买的时空扭曲卡牌
                    if (!IsSupportedShopCardType(cardType)) continue;
                    // zone: PLAY=1, DECK=2, HAND=3, GRAVEYARD=4, SETASIDE=5, SECRET=6
                    // 时空扭曲卡可能在 PLAY(1)/HAND(3)/SETASIDE(5) 区域中
                    if (zone != 1 && zone != 3 && zone != 5) continue;
                    if (zonePosition <= 0) continue;

                    var minion = ReadMinion(entity);
                    if (minion != null && data.ShopCards.All(existing => existing.EntityId != minion.EntityId))
                        data.ShopCards.Add(minion);
                }

                // 按 ZonePosition 重新排序
                if (data.ShopCards.Count > 0)
                    data.ShopCards = data.ShopCards.OrderBy(m => m.ZonePosition).ToList();
            }
            catch { }
        }

        // ── 辅助方法 ──

        private BgMinionData ReadMinion(object entity)
        {
            if (entity == null) return null;

            try
            {
                var entityId = ResolveEntityId(entity);
                if (entityId <= 0) return null;

                var cardId = ResolveCardId(entity);
                if (string.IsNullOrWhiteSpace(cardId)) return null;

                return new BgMinionData
                {
                    EntityId = entityId,
                    CardId = cardId,
                    Attack = _ctx.GetTagValue(entity, "ATK"),
                    Health = Math.Max(0, _ctx.GetTagValue(entity, "HEALTH") - _ctx.GetTagValue(entity, "DAMAGE")),
                    TavernTier = _ctx.GetTagValue(entity, "TECH_LEVEL"),
                    ZonePosition = _ctx.GetTagValue(entity, "ZONE_POSITION"),
                    IsGolden = _ctx.GetTagValue(entity, "PREMIUM") == 1,
                    IsTaunt = _ctx.GetTagValue(entity, "TAUNT") == 1,
                    IsDivineShield = _ctx.GetTagValue(entity, "DIVINE_SHIELD") == 1,
                    IsWindfury = _ctx.GetTagValue(entity, "WINDFURY") == 1,
                    IsVenomous = _ctx.GetTagValue(entity, "POISONOUS") == 1,
                    IsReborn = _ctx.GetTagValue(entity, "REBORN") == 1,
                    Cost = _ctx.GetTagValue(entity, "COST"),
                    CardType = _ctx.GetTagValue(entity, "CARDTYPE"),
                    IsTimewarped = _ctx.GetTagValue(entity, "BACON_TIMEWARPED") == 1
                };
            }
            catch
            {
                return null;
            }
        }

        private object GetFriendlyPlayer(object gameState)
        {
            return _ctx.CallAny(gameState,
                "GetFriendlySidePlayer",
                "GetFriendlyPlayer",
                "GetLocalPlayer");
        }

        private object GetOpposingPlayer(object gameState)
        {
            return _ctx.CallAny(gameState,
                "GetOpposingSidePlayer",
                "GetOpposingPlayer",
                "GetOpponentPlayer");
        }

        private object GetPlayerEntity(object player)
        {
            if (player == null) return null;
            var entity = _ctx.CallAny(player, "GetEntity", "GetGameEntity", "GetPlayerEntity");
            if (entity != null) return entity;
            return _ctx.GetFieldOrPropertyAny(player,
                "Entity", "GameEntity", "PlayerEntity", "m_entity", "m_gameEntity");
        }

        private object GetEntity(object card)
        {
            if (card == null) return null;
            return _ctx.CallAny(card, "GetEntity") ?? card;
        }

        private int ResolveEntityId(object entity)
        {
            if (entity == null) return 0;
            var id = _ctx.CallAny(entity, "GetEntityId", "GetId");
            if (id != null) return _ctx.ToInt(id);
            id = _ctx.GetFieldOrPropertyAny(entity, "m_entityId", "EntityId", "Id");
            return id != null ? _ctx.ToInt(id) : 0;
        }

        private string ResolveCardId(object entity)
        {
            if (entity == null) return null;
            var cardId = _ctx.CallAny(entity, "GetCardId");
            if (cardId != null) return cardId.ToString();
            cardId = _ctx.GetFieldOrPropertyAny(entity, "m_cardId", "CardId");
            return cardId?.ToString();
        }

        private int ResolvePlayerId(object player)
        {
            if (player == null) return 0;

            var id = _ctx.ToInt(_ctx.CallAny(player, "GetPlayerID", "GetPlayerId", "GetID", "GetEntityID", "GetEntityId"));
            if (id > 0) return id;

            id = _ctx.ToInt(_ctx.GetFieldOrPropertyAny(player,
                "PlayerID", "PlayerId", "ID", "EntityID", "EntityId", "m_playerID", "m_playerId", "m_id"));
            if (id > 0) return id;

            id = TryGetTagValueSafe(player, "PLAYER_ID");
            if (id > 0) return id;

            var entity = GetPlayerEntity(player) ?? GetEntity(player);
            id = TryGetTagValueSafe(entity, "CONTROLLER");
            if (id > 0) return id;

            var hero = _ctx.CallAny(player, "GetHero");
            id = TryGetTagValueSafe(hero, "CONTROLLER");
            return id;
        }

        private void NormalizePhase(BattlegroundStateData data)
        {
            if (data == null || string.Equals(data.Phase, "HERO_PICK", StringComparison.Ordinal))
                return;

            if (data.ShopCards.Count > 0)
            {
                data.Phase = "RECRUIT";
                data.IsOurTurn = true;
                return;
            }

            if (data.HandCards.Count > 0 && data.Gold > 0)
            {
                data.Phase = "RECRUIT";
                data.IsOurTurn = true;
                return;
            }

            if (data.IsGameOver)
                return;

            if (!data.IsOurTurn)
                data.Phase = "COMBAT";
        }

        private bool IsHeroPickActive(object gameState, object friendly)
        {
            try
            {
                if (ReadBool(_ctx.CallAny(gameState, "IsMulliganManagerActive", "get_IsMulliganManagerActive")))
                    return true;

                var gameEntity = _ctx.CallAny(gameState, "GetGameEntity");
                if (ReadBool(_ctx.CallAny(gameEntity, "IsMulliganActiveRealTime")))
                    return true;

                var mulliganState = TryGetTagValueSafe(GetPlayerEntity(friendly), "MULLIGAN_STATE");
                if (mulliganState > 0 && IsMulliganActive(mulliganState))
                    return true;

                var mulliganManagerType = _ctx.AsmCSharp?.GetType("MulliganManager");
                var mulliganMgr = _ctx.CallStaticAny(mulliganManagerType, "Get");
                if (mulliganMgr == null)
                    return false;

                if (ReadBool(_ctx.CallAny(mulliganMgr, "IsMulliganActive")))
                    return true;

                var waitingForUserInput = ReadBool(_ctx.GetFieldOrPropertyAny(mulliganMgr, "m_waitingForUserInput", "waitingForUserInput"));
                var introObj = _ctx.GetFieldOrPropertyAny(mulliganMgr, "introComplete");
                var introComplete = !(introObj is bool introFlag) || introFlag;
                var mulliganButton = _ctx.CallAny(mulliganMgr, "GetMulliganButton");
                var startingCards = _ctx.GetFieldOrPropertyAny(mulliganMgr, "m_startingCards", "StartingCards") as System.Collections.IEnumerable;
                var startingCardCount = CountNonNull(startingCards);

                return waitingForUserInput
                    || (introComplete && startingCardCount > 0)
                    || (mulliganButton != null && startingCardCount > 0);
            }
            catch
            {
                return false;
            }
        }

        private int TryGetBaconDummyPlayerId(object gameState)
        {
            try
            {
                var gameEntity = _ctx.CallAny(gameState, "GetGameEntity");
                if (gameEntity == null)
                    return 0;

                return TryGetTagValueSafe(gameEntity, "BACON_DUMMY_PLAYER_ID");
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsSupportedShopCardType(int cardType)
        {
            switch (cardType)
            {
                case 4:  // MINION
                case 5:  // SPELL
                case 7:  // WEAPON
                case 8:  // ITEM
                case 9:  // TOKEN
                case 39: // LOCATION
                case 40: // BATTLEGROUND_QUEST_REWARD
                case 42: // BATTLEGROUND_SPELL
                case 44: // BATTLEGROUND_TRINKET
                    return true;
                default:
                    return false;
            }
        }

        private int TryGetTagValueSafe(object entity, string tagName)
        {
            try { return _ctx.GetTagValue(entity, tagName); }
            catch { return 0; }
        }

        private int TryGetMaxTagValueSafe(string tagName, params object[] candidates)
        {
            var max = 0;
            if (candidates == null || candidates.Length == 0)
                return max;

            foreach (var candidate in candidates)
            {
                if (candidate == null)
                    continue;

                var value = TryGetTagValueSafe(candidate, tagName);
                if (value > max)
                    max = value;
            }

            return max;
        }

        private int TryGetFirstPositiveTagValueSafe(string tagName, params object[] candidates)
        {
            if (candidates == null || candidates.Length == 0)
                return 0;

            foreach (var candidate in candidates)
            {
                if (candidate == null)
                    continue;

                var value = TryGetTagValueSafe(candidate, tagName);
                if (value > 0)
                    return value;
            }

            return 0;
        }

        private void TryAddHeroPower(object heroPowerLike, int fallbackIndex, BattlegroundStateData data)
        {
            if (heroPowerLike == null || data == null)
                return;

            try
            {
                var entity = GetEntity(heroPowerLike) ?? heroPowerLike;
                var entityId = ResolveEntityId(entity);
                if (entityId <= 0)
                    return;

                if (data.HeroPowers.Any(existing => existing.EntityId == entityId))
                    return;

                var cardId = ResolveCardId(entity);
                if (string.IsNullOrWhiteSpace(cardId))
                    cardId = ResolveCardId(heroPowerLike) ?? string.Empty;

                // Additional hero powers do not always expose every tag on the same reflected object.
                // Probe both the resolved entity and the original hero-power/card wrapper so duplicated
                // hero powers can still be distinguished after one of them is exhausted.
                var exhausted = TryGetMaxTagValueSafe("EXHAUSTED", entity, heroPowerLike);
                var resolvedIndex = TryGetFirstPositiveTagValueSafe("ADDITIONAL_HERO_POWER_INDEX", entity, heroPowerLike);
                if (resolvedIndex <= 0)
                    resolvedIndex = fallbackIndex;

                var cost = TryGetFirstPositiveTagValueSafe("COST", entity, heroPowerLike);

                data.HeroPowers.Add(new BgHeroPowerData
                {
                    EntityId = entityId,
                    CardId = cardId,
                    Cost = cost,
                    // Availability is used to disambiguate same-card hero powers. Rely on the instance
                    // readiness tag instead of the inferred phase, which can briefly lag during recruit.
                    IsAvailable = exhausted == 0,
                    Index = resolvedIndex
                });
            }
            catch
            {
            }
        }

        private object CallMethodWithIntArg(object obj, int arg, params string[] methodNames)
        {
            if (obj == null || methodNames == null || methodNames.Length == 0)
                return null;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = obj.GetType();
            foreach (var methodName in methodNames)
            {
                try
                {
                    var method = type.GetMethods(flags)
                        .FirstOrDefault(mi =>
                            string.Equals(mi.Name, methodName, StringComparison.OrdinalIgnoreCase)
                            && mi.GetParameters().Length == 1
                            && IsSupportedIntParameter(mi.GetParameters()[0].ParameterType));
                    if (method == null)
                        continue;

                    var paramType = method.GetParameters()[0].ParameterType;
                    var argValue = Convert.ChangeType(arg, paramType);
                    return method.Invoke(obj, new[] { argValue });
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool IsSupportedIntParameter(Type type)
        {
            return type == typeof(int)
                || type == typeof(short)
                || type == typeof(byte)
                || type == typeof(long);
        }

        private bool? IsActionStep(int step)
        {
            if (step <= 0)
                return null;

            if (IsMulliganStep(step))
                return false;

            var stepType = _ctx.AsmCSharp.GetType("TAG_STEP")
                ?? _ctx.AsmCSharp.GetType("STEP")
                ?? _ctx.AsmCSharp.GetType("Step");

            var mainAction = GetEnumValue(stepType, "MAIN_ACTION");
            var mainStart = GetEnumValue(stepType, "MAIN_START");
            var mainReady = GetEnumValue(stepType, "MAIN_READY");
            var mainCombat = GetEnumValue(stepType, "MAIN_COMBAT");
            var mainEnd = GetEnumValue(stepType, "MAIN_END");

            if (mainAction.HasValue || mainStart.HasValue || mainReady.HasValue)
            {
                return step == mainAction
                    || step == mainStart
                    || step == mainReady
                    || step == mainCombat
                    || step == mainEnd;
            }

            return step >= 5 && step <= 9;
        }

        private bool IsMulliganStep(int step)
        {
            if (step <= 0)
                return false;

            var stepType = _ctx.AsmCSharp.GetType("TAG_STEP")
                ?? _ctx.AsmCSharp.GetType("STEP")
                ?? _ctx.AsmCSharp.GetType("Step");

            var mulliganStep = GetEnumValue(stepType, "BEGIN_MULLIGAN");
            if (mulliganStep.HasValue)
                return step == mulliganStep.Value;

            return step == 4;
        }

        private bool IsFinalStep(int step)
        {
            if (step <= 0)
                return false;

            var stepType = _ctx.AsmCSharp.GetType("TAG_STEP")
                ?? _ctx.AsmCSharp.GetType("STEP")
                ?? _ctx.AsmCSharp.GetType("Step");

            var finalWrapup = GetEnumValue(stepType, "FINAL_WRAPUP");
            var finalGameover = GetEnumValue(stepType, "FINAL_GAMEOVER");

            if (finalWrapup.HasValue || finalGameover.HasValue)
                return step == finalWrapup || step == finalGameover;

            return step == 14 || step == 15;
        }

        private bool IsMulliganActive(int mulliganState)
        {
            // MULLIGAN_STATE: INPUT=1, DEALING=2, WAITING=3, DONE=4
            return mulliganState > 0 && mulliganState != 4;
        }

        private static int? GetEnumValue(Type enumType, string name)
        {
            if (enumType == null || string.IsNullOrWhiteSpace(name))
                return null;

            try
            {
                if (!enumType.IsEnum)
                    return null;

                if (!Enum.IsDefined(enumType, name))
                    return null;

                return (int)Enum.Parse(enumType, name, ignoreCase: false);
            }
            catch
            {
                return null;
            }
        }

        private static bool ReadBool(object value)
        {
            if (value is bool b)
                return b;

            try
            {
                return value != null && Convert.ToBoolean(value);
            }
            catch
            {
                return false;
            }
        }

        private static int CountNonNull(System.Collections.IEnumerable values)
        {
            if (values == null)
                return 0;

            var count = 0;
            foreach (var value in values)
            {
                if (value != null)
                    count++;
            }

            return count;
        }

        private static int GetDefaultUpgradeCost(int currentTier)
        {
            switch (currentTier)
            {
                case 1: return 5;  // 1→2
                case 2: return 7;  // 2→3
                case 3: return 8;  // 3→4
                case 4: return 9;  // 4→5
                case 5: return 11; // 5→6
                default: return 0;
            }
        }
    }
}
