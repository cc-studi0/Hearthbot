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

                // STEP tag 判断阶段
                var step = _ctx.GetTagValue(gameEntity, "STEP");

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
                if (hero == null) return;

                var playstate = _ctx.GetTagValue(hero, "PLAYSTATE");
                // PLAYSTATE: PLAYING=1, WINNING=2, LOSING=3, WON=4, LOST=5, TIED=6, DISCONNECTED=7, CONCEDED=8
                if (playstate == 4) { data.IsGameOver = true; data.GameResult = "WIN"; }
                else if (playstate == 5 || playstate == 7 || playstate == 8) { data.IsGameOver = true; data.GameResult = "LOSS"; }
                else if (playstate == 6) { data.IsGameOver = true; data.GameResult = "TIE"; }

                // 也检查 GameState.IsGameOver()
                if (!data.IsGameOver)
                {
                    var isGameOverObj = _ctx.CallAny(gameState, "IsGameOver", "get_IsGameOver");
                    if (isGameOverObj is bool b && b)
                        data.IsGameOver = true;
                }
            }
            catch { }
        }

        // ── 名次 ──

        private void ReadPlacement(object friendly, BattlegroundStateData data)
        {
            try
            {
                var playerEntity = GetPlayerEntity(friendly);
                if (playerEntity == null) return;

                data.Placement = _ctx.GetTagValue(playerEntity, "PLAYER_LEADERBOARD_PLACE");
                if (data.Placement <= 0)
                    data.Placement = _ctx.GetTagValue(playerEntity, "BACON_PLAYER_RESULT_PLACE");

                // 剩余玩家数
                data.PlayerCount = _ctx.GetTagValue(playerEntity, "BACON_PLAYER_NUM_REMAINING");
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
                    Cost = _ctx.GetTagValue(entity, "COST")
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

                var exhausted = TryGetTagValueSafe(entity, "EXHAUSTED");
                var resolvedIndex = TryGetTagValueSafe(entity, "ADDITIONAL_HERO_POWER_INDEX");
                if (resolvedIndex <= 0)
                    resolvedIndex = fallbackIndex;

                data.HeroPowers.Add(new BgHeroPowerData
                {
                    EntityId = entityId,
                    CardId = cardId,
                    Cost = TryGetTagValueSafe(entity, "COST"),
                    IsAvailable = exhausted == 0 && data.Phase == "RECRUIT",
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
