using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartBot.Plugins.API;

namespace BotMain
{
    internal enum HsBoxDirectApiMode
    {
        CefCallback = 0,
        DirectApiShadow = 1,
        DirectApiPrimaryWithCefFallback = 2
    }

    internal enum HsBoxDirectApiKind
    {
        StandardSubstep,
        BattlegroundSubstep,
        BattlegroundRearrange,
        ArenaDraft,
        ArenaSubstep
    }

    internal sealed class HsBoxDirectApiRequest
    {
        public HsBoxDirectApiRequest(HsBoxDirectApiKind kind, JToken payload, string source)
        {
            Kind = kind;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            Source = source ?? string.Empty;
        }

        public HsBoxDirectApiKind Kind { get; }
        public JToken Payload { get; }
        public string Source { get; }
    }

    internal sealed class HsBoxDirectApiResponse
    {
        public bool Success { get; set; }
        public string Detail { get; set; } = string.Empty;
        public HttpStatusCode StatusCode { get; set; }
        public string ResponseText { get; set; } = string.Empty;
        public JToken ResponseJson { get; set; }
        public HsBoxRecommendationState State { get; set; }
    }

    internal interface IHsBoxDirectApiClient
    {
        HsBoxDirectApiResponse Post(HsBoxDirectApiRequest request);
    }

    internal interface IHsBoxDirectApiPayloadProvider
    {
        bool TryCreateConstructedActionRequest(
            ActionRecommendationRequest request,
            out HsBoxDirectApiRequest apiRequest,
            out string detail);

        bool TryCreateBattlegroundActionRequest(
            string bgStateData,
            out HsBoxDirectApiRequest apiRequest,
            out string detail);
    }

    internal interface IHsBoxNativeBridgeClient
    {
        bool TryGetCurrentBattleInfo(out JToken battleInfo, out string detail);
        bool TryInvoke(string method, out JObject reply, out string detail);
    }

    internal sealed class HsBoxDirectDisabledPayloadProvider : IHsBoxDirectApiPayloadProvider
    {
        private const string DisabledDetail = "disabled:no_safe_hsbox_request_payload_provider";

        public bool TryCreateConstructedActionRequest(
            ActionRecommendationRequest request,
            out HsBoxDirectApiRequest apiRequest,
            out string detail)
        {
            apiRequest = null;
            detail = DisabledDetail;
            return false;
        }

        public bool TryCreateBattlegroundActionRequest(
            string bgStateData,
            out HsBoxDirectApiRequest apiRequest,
            out string detail)
        {
            apiRequest = null;
            detail = DisabledDetail;
            return false;
        }
    }

    internal sealed class HsBoxDirectBoardPayloadProvider : IHsBoxDirectApiPayloadProvider
    {
        private const string Source = "board_state";

        public bool TryCreateConstructedActionRequest(
            ActionRecommendationRequest request,
            out HsBoxDirectApiRequest apiRequest,
            out string detail)
        {
            apiRequest = null;

            if (request == null)
            {
                detail = "request_null";
                return false;
            }

            if (request.PlanningBoard == null)
            {
                detail = "planning_board_null";
                return false;
            }

            var payload = BuildConstructedPayload(request);
            apiRequest = new HsBoxDirectApiRequest(HsBoxDirectApiKind.StandardSubstep, payload, Source);
            detail = Source;
            return true;
        }

        public bool TryCreateBattlegroundActionRequest(
            string bgStateData,
            out HsBoxDirectApiRequest apiRequest,
            out string detail)
        {
            apiRequest = null;
            detail = "bg_payload_not_implemented";
            return false;
        }

        private static JObject BuildConstructedPayload(ActionRecommendationRequest request)
        {
            var board = request.PlanningBoard;
            var turn = BuildTurnObject(request, board);
            var requestId = BuildRequestId(request);
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return new JObject
            {
                ["type"] = "normal",
                ["source"] = "hearthbot",
                ["requestId"] = requestId,
                ["uuid"] = requestId,
                ["btag"] = "hearthbot",
                ["recommend_version"] = "1.1.1",
                ["version"] = "hearthbot-direct",
                ["createdAt"] = nowMs,
                ["seed"] = request.Seed ?? string.Empty,
                ["deckName"] = request.DeckName ?? string.Empty,
                ["deck_name"] = request.DeckName ?? string.Empty,
                ["deckSignature"] = request.DeckSignature ?? string.Empty,
                ["deck_signature"] = request.DeckSignature ?? string.Empty,
                ["boardFingerprint"] = request.BoardFingerprint ?? string.Empty,
                ["board_fingerprint"] = request.BoardFingerprint ?? string.Empty,
                ["matchId"] = request.MatchContext?.MatchId ?? string.Empty,
                ["turnNum"] = request.MatchContext?.TurnCount > 0
                    ? request.MatchContext.TurnCount
                    : board.TurnCount,
                ["turn"] = turn.DeepClone(),
                ["turns"] = new JArray
                {
                    new JObject
                    {
                        ["turn"] = turn
                    }
                }
            };
        }

        private static JObject BuildTurnObject(ActionRecommendationRequest request, Board board)
        {
            var hand = BuildCardArray(board.Hand, "hand");
            var friendlyBoard = BuildCardArray(board.MinionFriend, "friendly_board");
            var enemyBoard = BuildCardArray(board.MinionEnemy, "enemy_board");
            var deck = BuildDeckArray(request.DeckCards);
            var remainingDeck = BuildDeckArray(request.RemainingDeckCards);

            return new JObject
            {
                ["turn"] = board.TurnCount,
                ["turnNum"] = board.TurnCount,
                ["mana"] = board.ManaAvailable,
                ["maxMana"] = board.MaxMana,
                ["max_mana"] = board.MaxMana,
                ["enemyMaxMana"] = board.EnemyMaxMana,
                ["enemy_max_mana"] = board.EnemyMaxMana,
                ["cardsPlayedThisTurn"] = board.CardsPlayedThisTurn,
                ["cards_played_this_turn"] = board.CardsPlayedThisTurn,
                ["friendClass"] = board.FriendClass.ToString(),
                ["friend_class"] = board.FriendClass.ToString(),
                ["enemyClass"] = board.EnemyClass.ToString(),
                ["enemy_class"] = board.EnemyClass.ToString(),
                ["hero"] = BuildCardObject(board.HeroFriend, "hero", 0),
                ["enemyHero"] = BuildCardObject(board.HeroEnemy, "enemy_hero", 0),
                ["enemy_hero"] = BuildCardObject(board.HeroEnemy, "enemy_hero", 0),
                ["heroPower"] = BuildCardObject(board.Ability, "hero_power", 0),
                ["hero_power"] = BuildCardObject(board.Ability, "hero_power", 0),
                ["enemyHeroPower"] = BuildCardObject(board.EnemyAbility, "enemy_hero_power", 0),
                ["enemy_hero_power"] = BuildCardObject(board.EnemyAbility, "enemy_hero_power", 0),
                ["weapon"] = BuildCardObject(board.WeaponFriend, "weapon", 0),
                ["enemyWeapon"] = BuildCardObject(board.WeaponEnemy, "enemy_weapon", 0),
                ["enemy_weapon"] = BuildCardObject(board.WeaponEnemy, "enemy_weapon", 0),
                ["hand"] = hand,
                ["friendlyBoard"] = friendlyBoard,
                ["friendly_board"] = friendlyBoard.DeepClone(),
                ["enemyBoard"] = enemyBoard,
                ["enemy_board"] = enemyBoard.DeepClone(),
                ["deck"] = deck,
                ["deck_cards"] = deck.DeepClone(),
                ["remainingDeck"] = remainingDeck,
                ["remaining_deck"] = remainingDeck.DeepClone(),
                ["secrets"] = BuildDeckArray(board.Secret),
                ["enemySecretCount"] = board.SecretEnemyCount,
                ["enemy_secret_count"] = board.SecretEnemyCount
            };
        }

        private static JArray BuildCardArray(IEnumerable<Card> cards, string zoneName)
        {
            var array = new JArray();
            if (cards == null)
                return array;

            var index = 0;
            foreach (var card in cards)
            {
                if (card == null)
                    continue;

                array.Add(BuildCardObject(card, zoneName, ++index));
            }

            return array;
        }

        private static JObject BuildCardObject(Card card, string zoneName, int fallbackPosition)
        {
            if (card == null)
                return new JObject();

            var cardId = GetCardId(card);
            var cardName = GetCardName(card);
            var position = card.Index > 0 ? card.Index : fallbackPosition;

            return new JObject
            {
                ["entityId"] = card.Id,
                ["id"] = card.Id,
                ["cardId"] = cardId,
                ["card_id"] = cardId,
                ["cardName"] = cardName,
                ["card_name"] = cardName,
                ["name"] = cardName,
                ["cost"] = card.CurrentCost,
                ["atk"] = card.CurrentAtk,
                ["attack"] = card.CurrentAtk,
                ["tempAtk"] = card.TempAtk,
                ["health"] = card.CurrentHealth,
                ["maxHealth"] = card.MaxHealth,
                ["armor"] = card.CurrentArmor,
                ["durability"] = card.CurrentDurability,
                ["type"] = card.Type.ToString(),
                ["race"] = card.Race.ToString(),
                ["zone"] = zoneName,
                ["zoneName"] = zoneName,
                ["zone_name"] = zoneName,
                ["zonePosition"] = position,
                ["ZONE_POSITION"] = position,
                ["isFriend"] = card.IsFriend,
                ["isGenerated"] = card.IsGenerated,
                ["turnsInPlay"] = card.NumTurnsInPlay,
                ["attackCount"] = card.CountAttack,
                ["canAttack"] = card.CanAttack,
                ["isTargetable"] = card.IsTargetable,
                ["tags"] = BuildTags(card)
            };
        }

        private static JObject BuildTags(Card card)
        {
            return new JObject
            {
                ["TAUNT"] = card.IsTaunt,
                ["CHARGE"] = card.IsCharge,
                ["DIVINE_SHIELD"] = card.IsDivineShield,
                ["WINDFURY"] = card.IsWindfury,
                ["STEALTH"] = card.IsStealth,
                ["EXHAUSTED"] = card.IsTired,
                ["FROZEN"] = card.IsFrozen,
                ["ENRAGED"] = card.IsEnraged,
                ["SILENCED"] = card.IsSilenced,
                ["IMMUNE"] = card.IsImmune,
                ["POISONOUS"] = card.HasPoison,
                ["DEATHRATTLE"] = card.HasDeathRattle,
                ["SPELLPOWER"] = card.SpellPower,
                ["LIFESTEAL"] = card.IsLifeSteal,
                ["RUSH"] = card.HasRush,
                ["REBORN"] = card.HasReborn
            };
        }

        private static JArray BuildDeckArray(IEnumerable<Card.Cards> cards)
        {
            var array = new JArray();
            if (cards == null)
                return array;

            foreach (var card in cards)
            {
                var cardId = NormalizeCardId(card.ToString());
                if (string.IsNullOrWhiteSpace(cardId))
                    continue;

                array.Add(new JObject
                {
                    ["cardId"] = cardId,
                    ["card_id"] = cardId
                });
            }

            return array;
        }

        private static string GetCardId(Card card)
        {
            if (card?.Template == null)
                return string.Empty;

            return NormalizeCardId(card.Template.Id.ToString());
        }

        private static string GetCardName(Card card)
        {
            if (card?.Template == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(card.Template.NameCN))
                return card.Template.NameCN;

            return card.Template.Name ?? string.Empty;
        }

        private static string NormalizeCardId(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId)
                || string.Equals(cardId, "None", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cardId, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return cardId;
        }

        private static string BuildRequestId(ActionRecommendationRequest request)
        {
            var matchId = request.MatchContext?.MatchId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(matchId))
                return "hearthbot_" + SanitizeToken(matchId);

            var basis = string.Join("|",
                request.Seed ?? string.Empty,
                request.DeckSignature ?? string.Empty,
                request.BoardFingerprint ?? string.Empty,
                request.PlanningBoard?.TurnCount.ToString() ?? string.Empty);
            var hash = unchecked((uint)basis.GetHashCode()).ToString("X8");
            return "hearthbot_" + DateTime.UtcNow.ToString("yyyyMMdd") + "_" + hash;
        }

        private static string SanitizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'
                    ? ch
                    : '_');
            }

            return builder.ToString();
        }
    }

    internal sealed class HsBoxDirectHsAngPayloadProvider : IHsBoxDirectApiPayloadProvider
    {
        private const string Source = "hsang_board_state";

        private readonly IHsBoxNativeBridgeClient _nativeBridge;
        private readonly HsBoxDirectNativePayloadProvider _nativeFallbackProvider;

        public HsBoxDirectHsAngPayloadProvider()
            : this(new HsBoxNativeBridgeClient())
        {
        }

        internal HsBoxDirectHsAngPayloadProvider(IHsBoxNativeBridgeClient nativeBridge)
        {
            _nativeBridge = nativeBridge ?? throw new ArgumentNullException(nameof(nativeBridge));
            _nativeFallbackProvider = new HsBoxDirectNativePayloadProvider(_nativeBridge);
        }

        public bool TryCreateConstructedActionRequest(
            ActionRecommendationRequest request,
            out HsBoxDirectApiRequest apiRequest,
            out string detail)
        {
            apiRequest = null;

            if (request == null)
            {
                detail = "request_null";
                return false;
            }

            if (request.PlanningBoard == null)
            {
                detail = "planning_board_null";
                return false;
            }

            var boxParams = BuildBoxParams(out var boxDetail);
            if (!HsBoxDirectHsAngPayloadBuilder.TryBuildConstructedPayload(
                    request,
                    new JObject(),
                    boxParams,
                    out var payload,
                    out var buildDetail))
            {
                detail = buildDetail + "; box=" + boxDetail;
                return false;
            }

            apiRequest = new HsBoxDirectApiRequest(HsBoxDirectApiKind.StandardSubstep, payload, Source);
            detail = Source + "; box=" + boxDetail;
            return true;
        }

        public bool TryCreateBattlegroundActionRequest(
            string bgStateData,
            out HsBoxDirectApiRequest apiRequest,
            out string detail)
        {
            return _nativeFallbackProvider.TryCreateBattlegroundActionRequest(bgStateData, out apiRequest, out detail);
        }

        private JObject BuildBoxParams(out string detail)
        {
            var payload = new JObject();
            var parts = new List<string>();

            if (!TryAddString(payload, "sid", "getSid", parts))
            {
                detail = string.Join(",", parts);
                return payload;
            }

            TryAddString(payload, "client_uuid", "get_info_token", parts);
            TryAddString(payload, "version", "get_version", parts);

            detail = parts.Count > 0 ? string.Join(",", parts) : "box_params_empty";
            return payload;
        }

        private bool TryAddString(JObject payload, string propertyName, string nativeMethod, List<string> parts)
        {
            if (!_nativeBridge.TryInvoke(nativeMethod, out var reply, out var nativeDetail))
            {
                parts.Add(nativeMethod + "=" + nativeDetail);
                return false;
            }

            var value = HsBoxNativeBridgeClient.ExtractDataString(reply);
            if (string.IsNullOrWhiteSpace(value))
            {
                parts.Add(nativeMethod + "=empty");
                return false;
            }

            payload[propertyName] = value;
            parts.Add(nativeMethod + "=ok");
            return true;
        }
    }

    internal static class HsBoxDirectHsAngPayloadBuilder
    {
        private const string DefaultVersion = "4.0.4.314";

        public static bool TryBuildConstructedPayload(
            ActionRecommendationRequest request,
            JObject battleInfo,
            JObject boxParams,
            out JObject payload,
            out string detail)
        {
            payload = null;

            if (request == null)
            {
                detail = "request_null";
                return false;
            }

            var board = request.PlanningBoard;
            if (board == null)
            {
                detail = "planning_board_null";
                return false;
            }

            battleInfo ??= new JObject();
            boxParams ??= new JObject();

            var cardGroup = BuildCardGroup(request.DeckCards);
            if (string.IsNullOrWhiteSpace(cardGroup))
                cardGroup = BuildCardGroup(request.RemainingDeckCards);
            if (string.IsNullOrWhiteSpace(cardGroup))
                cardGroup = BuildVisibleFriendlyCardGroup(board);
            if (string.IsNullOrWhiteSpace(cardGroup))
            {
                detail = "cardgroup_empty";
                return false;
            }

            var sid = ReadString(boxParams, "sid", "ls_session_id")
                ?? ReadString(battleInfo, "sid", "ls_session_id");
            if (string.IsNullOrWhiteSpace(sid))
            {
                detail = "sid_missing";
                return false;
            }

            var btag = ReadString(battleInfo, "mybtag", "btag")
                ?? ReadString(boxParams, "btag")
                ?? "hearthbot";
            var uuid = ReadString(battleInfo, "uuid")
                ?? ReadString(boxParams, "uuid")
                ?? BuildRequestId(request);
            var clientUuid = ReadString(boxParams, "client_uuid", "info_token", "deviceid", "device_id", "uuid")
                ?? uuid;
            var version = ReadString(boxParams, "version", "recommend_version")
                ?? ReadString(battleInfo, "version", "recommend_version")
                ?? DefaultVersion;
            var rank = ReadString(boxParams, "rank", "ladder_rank")
                ?? ReadString(battleInfo, "rank", "ladder_rank");

            var state = BuildState(request, battleInfo);
            payload = new JObject
            {
                ["btag"] = btag,
                ["data"] = new JObject
                {
                    ["extra_infos"] = new JObject
                    {
                        ["cardgroup"] = cardGroup
                    },
                    ["turns"] = new JArray
                    {
                        new JObject
                        {
                            ["processes"] = new JArray
                            {
                                new JObject
                                {
                                    ["state"] = state
                                }
                            }
                        }
                    }
                },
                ["type"] = "netreloaded",
                ["uuid"] = uuid,
                ["sid"] = sid,
                ["client_uuid"] = clientUuid,
                ["version"] = version,
                ["recommend_version"] = version
            };

            var deviceId = ReadString(boxParams, "deviceid", "device_id");
            if (!string.IsNullOrWhiteSpace(deviceId))
                payload["deviceid"] = deviceId;
            if (!string.IsNullOrWhiteSpace(rank))
                payload["rank"] = rank;

            detail = "hsang_payload_ready";
            return true;
        }

        private static JObject BuildState(ActionRecommendationRequest request, JObject battleInfo)
        {
            var board = request.PlanningBoard;
            var reservedEntityIds = new HashSet<int> { 1, 2, 3 };
            var myHero = BuildSingleCardEntity(board.HeroFriend, "PLAY", 1, GetClassName(board.FriendClass), "HERO", 0, reservedEntityIds);
            var oppHero = BuildSingleCardEntity(board.HeroEnemy, "PLAY", 2, GetClassName(board.EnemyClass), "HERO", 0, reservedEntityIds);
            var myHeroPower = BuildSingleCardEntity(board.Ability, "PLAY", 1, GetClassName(board.FriendClass), "HERO_POWER", 0, reservedEntityIds);
            var oppHeroPower = BuildSingleCardEntity(board.EnemyAbility, "PLAY", 2, GetClassName(board.EnemyClass), "HERO_POWER", 0, reservedEntityIds);
            var myLineup = BuildCardEntities(board.MinionFriend, "PLAY", 1, GetClassName(board.FriendClass), "MINION", reservedEntityIds);
            var oppLineup = BuildCardEntities(board.MinionEnemy, "PLAY", 2, GetClassName(board.EnemyClass), "MINION", reservedEntityIds);
            var myHands = BuildCardEntities(board.Hand, "HAND", 1, GetClassName(board.FriendClass), "MINION", reservedEntityIds);
            var emittedEntityIds = CollectEntityIds(myHands, myLineup, oppLineup, myHero, oppHero, myHeroPower, oppHeroPower);
            var emittedNonHandEntityIds = CollectEntityIds(myLineup, oppLineup, myHero, oppHero, myHeroPower, oppHeroPower);
            var entities = new JObject
            {
                ["attachments"] = new JArray(),
                ["game"] = new JArray(BuildGameEntity(board, battleInfo)),
                ["my_deck"] = new JArray(),
                ["opp_deck"] = new JArray(),
                ["my_graveyard"] = new JArray(),
                ["opp_graveyard"] = new JArray(),
                ["opp_hands"] = new JArray(),
                ["my_hands"] = myHands,
                ["my_lineup"] = myLineup,
                ["opp_lineup"] = oppLineup,
                ["my_hero"] = myHero,
                ["opp_hero"] = oppHero,
                ["my_hero_power"] = myHeroPower,
                ["opp_hero_power"] = oppHeroPower,
                ["my_player"] = new JArray(BuildPlayerEntity(board, true)),
                ["opp_player"] = new JArray(BuildPlayerEntity(board, false))
            };
            var options = BuildOptions(board, emittedEntityIds, emittedNonHandEntityIds);

            return new JObject
            {
                ["deck_card_counter"] = new JObject
                {
                    ["my_deck"] = new JObject
                    {
                        ["pop"] = BuildCardCounter(request.RemainingDeckCards),
                        ["push"] = new JObject()
                    },
                    ["opp_deck"] = new JObject
                    {
                        ["pop"] = new JObject(),
                        ["push"] = new JObject()
                    }
                },
                ["entities"] = entities,
                ["options"] = options,
                ["options_id"] = Math.Max(1, board.TurnCount * 10 + options.Count),
                ["played_cards"] = new JObject
                {
                    ["my"] = new JArray(),
                    ["opp"] = new JArray()
                }
            };
        }

        private static JObject BuildGameEntity(Board board, JObject battleInfo)
        {
            return new JObject
            {
                ["CARDTYPE"] = "GAME",
                ["ENTITY_ID"] = 1,
                ["FormatType"] = ResolveFormatType(battleInfo),
                ["GAME_TYPE"] = ResolveGameType(battleInfo),
                ["NEXT_STEP"] = "MAIN_END",
                ["NUM_MINIONS_KILLED_THIS_TURN"] = 0,
                ["PROPOSED_ATTACKER"] = 0,
                ["PROPOSED_DEFENDER"] = 0,
                ["STATE"] = "RUNNING",
                ["STEP"] = "MAIN_ACTION",
                ["TURN"] = Math.Max(0, board.TurnCount),
                ["ZONE"] = "PLAY"
            };
        }

        private static JObject BuildPlayerEntity(Board board, bool friendly)
        {
            var hero = friendly ? board.HeroFriend : board.HeroEnemy;
            var heroPower = friendly ? board.Ability : board.EnemyAbility;
            var maxMana = Math.Max(0, friendly ? board.MaxMana : board.EnemyMaxMana);
            var availableMana = friendly ? Math.Max(0, board.ManaAvailable) : 0;
            var resourcesUsed = friendly ? Math.Max(0, maxMana - availableMana) : 0;
            var playerId = friendly ? 1 : 2;

            return new JObject
            {
                ["CARDTYPE"] = "PLAYER",
                ["CONTROLLER"] = playerId,
                ["CURRENT_PLAYER"] = friendly ? 1 : 0,
                ["ENTITY_ID"] = friendly ? 2 : 3,
                ["HERO_ENTITY"] = hero?.Id ?? 0,
                ["HERO_POWER"] = heroPower?.Id ?? 0,
                ["MAXHANDSIZE"] = 10,
                ["MAXRESOURCES"] = 10,
                ["MULLIGAN_STATE"] = "DONE",
                ["NUM_CARDS_PLAYED_THIS_TURN"] = friendly ? Math.Max(0, board.CardsPlayedThisTurn) : 0,
                ["PLAYER_ID"] = playerId,
                ["PLAYSTATE"] = "PLAYING",
                ["RESOURCES"] = maxMana,
                ["RESOURCES_USED"] = resourcesUsed,
                ["TEMP_RESOURCES"] = 0,
                ["TURN"] = Math.Max(0, board.TurnCount),
                ["ZONE"] = "PLAY"
            };
        }

        private static JArray BuildSingleCardEntity(
            Card card,
            string zone,
            int controller,
            string className,
            string cardType,
            int fallbackPosition,
            HashSet<int> reservedEntityIds = null)
        {
            var array = new JArray();
            if (card != null && TryReserveEntityId(card, reservedEntityIds))
                array.Add(BuildCardEntity(card, zone, controller, className, cardType, fallbackPosition));
            return array;
        }

        private static JArray BuildCardEntities(
            IEnumerable<Card> cards,
            string zone,
            int controller,
            string className,
            string fallbackType,
            HashSet<int> reservedEntityIds = null)
        {
            var array = new JArray();
            if (cards == null)
                return array;

            var indexed = cards
                .Select((card, index) => new
                {
                    Card = card,
                    OriginalIndex = index,
                    Position = GetZonePosition(card, index + 1)
                })
                .Where(item => item.Card != null)
                .OrderBy(item => item.Position <= 0 ? int.MaxValue : item.Position)
                .ThenBy(item => item.OriginalIndex);

            foreach (var item in indexed)
            {
                if (!TryReserveEntityId(item.Card, reservedEntityIds))
                    continue;
                array.Add(BuildCardEntity(item.Card, zone, controller, className, fallbackType, item.Position));
            }

            return array;
        }

        private static bool TryReserveEntityId(Card card, HashSet<int> reservedEntityIds)
        {
            if (card == null)
                return false;
            if (reservedEntityIds == null)
                return true;

            var entityId = card.Id;
            return entityId > 0 && reservedEntityIds.Add(entityId);
        }

        private static HashSet<int> CollectEntityIds(params JArray[] groups)
        {
            var ids = new HashSet<int>();
            if (groups == null)
                return ids;

            foreach (var group in groups)
            {
                if (group == null)
                    continue;

                foreach (var entity in group.OfType<JObject>())
                {
                    var entityId = entity.Value<int>("ENTITY_ID");
                    if (entityId > 0)
                        ids.Add(entityId);
                }
            }

            return ids;
        }

        private static JObject BuildCardEntity(
            Card card,
            string zone,
            int controller,
            string className,
            string fallbackType,
            int fallbackPosition)
        {
            var maxHealth = Math.Max(0, card.MaxHealth);
            var currentHealth = Math.Max(0, card.CurrentHealth);
            if (maxHealth <= 0)
                maxHealth = currentHealth;
            var damage = Math.Max(0, maxHealth - currentHealth);
            var position = GetZonePosition(card, fallbackPosition);
            var cardType = GetCardTypeName(card, fallbackType);

            var entity = new JObject
            {
                ["ENTITY_ID"] = Math.Max(0, card.Id),
                ["_CARD_ID"] = GetCardId(card),
                ["ZONE"] = zone,
                ["CONTROLLER"] = controller,
                ["CARDTYPE"] = cardType,
                ["CLASS"] = className,
                ["ATK"] = Math.Max(0, card.CurrentAtk),
                ["COST"] = Math.Max(0, card.CurrentCost),
                ["DAMAGE"] = damage,
                ["EXHAUSTED"] = card.IsTired ? 1 : 0,
                ["FROZEN"] = card.IsFrozen ? 1 : 0,
                ["HAS_ACTIVATE_POWER"] = HasActivatePower(card, cardType) ? 1 : 0,
                ["HEALTH"] = maxHealth,
                ["NUM_ATTACKS_THIS_TURN"] = Math.Max(0, card.CountAttack),
                ["NUM_TURNS_IN_PLAY"] = Math.Max(0, card.NumTurnsInPlay),
                ["PREMIUM"] = 0,
                ["TAG_LAST_KNOWN_COST_IN_HAND"] = Math.Max(0, card.CurrentCost)
            };

            if (position > 0)
                entity["ZONE_POSITION"] = position;
            if (card.CurrentArmor > 0)
                entity["ARMOR"] = card.CurrentArmor;
            if (card.CurrentDurability > 0)
                entity["DURABILITY"] = card.CurrentDurability;

            AddBoolTag(entity, "TAUNT", card.IsTaunt);
            AddBoolTag(entity, "CHARGE", card.IsCharge);
            AddBoolTag(entity, "DIVINE_SHIELD", card.IsDivineShield);
            AddBoolTag(entity, "WINDFURY", card.IsWindfury);
            AddBoolTag(entity, "STEALTH", card.IsStealth);
            AddBoolTag(entity, "ENRAGED", card.IsEnraged);
            AddBoolTag(entity, "SILENCED", card.IsSilenced);
            AddBoolTag(entity, "IMMUNE", card.IsImmune);
            AddBoolTag(entity, "POISONOUS", card.HasPoison);
            AddBoolTag(entity, "DEATHRATTLE", card.HasDeathRattle);
            AddBoolTag(entity, "LIFESTEAL", card.IsLifeSteal);
            AddBoolTag(entity, "RUSH", card.HasRush);
            AddBoolTag(entity, "REBORN", card.HasReborn);
            if (card.SpellPower > 0)
                entity["SPELLPOWER"] = card.SpellPower;

            return entity;
        }

        private static JArray BuildOptions(
            Board board,
            ISet<int> emittedEntityIds,
            ISet<int> emittedNonHandEntityIds)
        {
            var options = new JArray
            {
                new JObject
                {
                    ["error"] = "INVALID",
                    ["id"] = 0,
                    ["type"] = "END_TURN"
                }
            };
            var nextId = 1;
            var usedOptionEntityIds = new HashSet<int>();

            AddPowerOptions(options, board.Hand, 1, ref nextId, card => GetHandPowerError(card, board), emittedEntityIds, usedOptionEntityIds, emittedNonHandEntityIds);
            AddPowerOption(options, board.Ability, 1, ref nextId, card => GetHeroPowerError(card, board.ManaAvailable), emittedEntityIds, usedOptionEntityIds);
            AddPowerOptions(options, board.MinionFriend, 1, ref nextId, GetBoardPowerError, emittedEntityIds, usedOptionEntityIds);
            AddPowerOption(options, board.HeroFriend, 1, ref nextId, GetBoardPowerError, emittedEntityIds, usedOptionEntityIds);

            AddPowerOption(options, board.HeroEnemy, 2, ref nextId, _ => "REQ_YOUR_TURN", emittedEntityIds, usedOptionEntityIds);
            AddPowerOption(options, board.EnemyAbility, 2, ref nextId, _ => "REQ_YOUR_TURN", emittedEntityIds, usedOptionEntityIds);
            AddPowerOptions(options, board.MinionEnemy, 2, ref nextId, _ => "REQ_YOUR_TURN", emittedEntityIds, usedOptionEntityIds);

            return options;
        }

        private static void AddPowerOptions(
            JArray options,
            IEnumerable<Card> cards,
            int playerId,
            ref int nextId,
            Func<Card, string> errorFactory,
            ISet<int> allowedEntityIds,
            HashSet<int> usedOptionEntityIds,
            ISet<int> excludedEntityIds = null)
        {
            if (cards == null)
                return;

            foreach (var card in cards.Where(card => card != null))
                AddPowerOption(options, card, playerId, ref nextId, errorFactory, allowedEntityIds, usedOptionEntityIds, excludedEntityIds);
        }

        private static void AddPowerOption(
            JArray options,
            Card card,
            int playerId,
            ref int nextId,
            Func<Card, string> errorFactory,
            ISet<int> allowedEntityIds,
            HashSet<int> usedOptionEntityIds,
            ISet<int> excludedEntityIds = null)
        {
            if (options == null || card == null || card.Id <= 0)
                return;
            if (allowedEntityIds != null && !allowedEntityIds.Contains(card.Id))
                return;
            if (excludedEntityIds != null && excludedEntityIds.Contains(card.Id))
                return;
            if (usedOptionEntityIds != null && !usedOptionEntityIds.Add(card.Id))
                return;

            options.Add(new JObject
            {
                ["entity_id"] = card.Id,
                ["error"] = errorFactory?.Invoke(card) ?? "INVALID",
                ["id"] = nextId++,
                ["player_id"] = playerId,
                ["type"] = "POWER"
            });
        }

        private static string GetHandPowerError(Card card, Board board)
        {
            if (card == null)
                return "INVALID";
            if (HasAnyGameTag(card, Card.GAME_TAG.CANT_PLAY, Card.GAME_TAG.LITERALLY_UNPLAYABLE))
                return "CANT_PLAY";
            if (card.CurrentCost > Math.Max(0, board?.ManaAvailable ?? 0))
                return "REQ_ENOUGH_MANA";
            if (RequiresExplicitTarget(card) && (card.Targets == null || card.Targets.Count == 0))
                return "REQ_TARGET_TO_PLAY";

            return "NONE";
        }

        private static string GetHeroPowerError(Card card, int manaAvailable)
        {
            if (card == null)
                return "INVALID";
            if (card.IsTired)
                return "EXHAUSTED";
            return card.CurrentCost <= Math.Max(0, manaAvailable) ? "NONE" : "REQ_ENOUGH_MANA";
        }

        private static string GetBoardPowerError(Card card)
        {
            if (card == null)
                return "INVALID";
            if (card.Type == Card.CType.LOCATION)
                return card.IsTired ? "EXHAUSTED" : "NONE";
            if (card.IsFrozen || card.IsTired)
                return "EXHAUSTED";
            if (!card.CanAttack || card.CurrentAtk <= 0)
                return "REQ_ATTACK_GREATER_THAN_0";
            return "NONE";
        }

        private static bool RequiresExplicitTarget(Card card)
        {
            if (card == null)
                return false;

            return HasAnyGameTag(
                card,
                Card.GAME_TAG.CARD_TARGET,
                Card.GAME_TAG.TARGETING_ARROW_TEXT,
                Card.GAME_TAG.TARGETING_ARROW_TYPE);
        }

        private static bool HasAnyGameTag(Card card, params Card.GAME_TAG[] tags)
        {
            if (card?.tags == null || tags == null)
                return false;

            foreach (var tag in tags)
            {
                if (card.tags.TryGetValue(tag, out var value) && value != 0)
                    return true;
            }

            return false;
        }

        private static JObject BuildCardCounter(IEnumerable<Card.Cards> cards)
        {
            var counter = new JObject();
            var counts = CountCards(cards, out var order);
            foreach (var cardId in order)
                counter[cardId] = counts[cardId];
            return counter;
        }

        private static string BuildCardGroup(IEnumerable<Card.Cards> cards)
        {
            var counts = CountCards(cards, out var order);
            return string.Join(",", order.Select(cardId => cardId + ":" + counts[cardId]));
        }

        private static string BuildVisibleFriendlyCardGroup(Board board)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            AddVisibleCards(board?.Hand, counts, order);
            AddVisibleCards(board?.MinionFriend, counts, order);
            return string.Join(",", order.Select(cardId => cardId + ":" + counts[cardId]));
        }

        private static void AddVisibleCards(
            IEnumerable<Card> cards,
            Dictionary<string, int> counts,
            List<string> order)
        {
            if (cards == null || counts == null || order == null)
                return;

            foreach (var card in cards)
            {
                var cardId = GetCardId(card);
                if (string.IsNullOrWhiteSpace(cardId))
                    continue;

                if (!counts.ContainsKey(cardId))
                {
                    counts[cardId] = 0;
                    order.Add(cardId);
                }

                counts[cardId]++;
            }
        }

        private static Dictionary<string, int> CountCards(IEnumerable<Card.Cards> cards, out List<string> order)
        {
            order = new List<string>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (cards == null)
                return counts;

            foreach (var card in cards)
            {
                var cardId = NormalizeCardId(card.ToString());
                if (string.IsNullOrWhiteSpace(cardId))
                    continue;

                if (!counts.ContainsKey(cardId))
                {
                    counts[cardId] = 0;
                    order.Add(cardId);
                }

                counts[cardId]++;
            }

            return counts;
        }

        private static string ResolveFormatType(JObject battleInfo)
        {
            var gameType = (ReadString(battleInfo, "game_type") ?? string.Empty).Trim();
            var wild = ReadString(battleInfo, "wild");
            if (string.Equals(gameType, "wild", StringComparison.OrdinalIgnoreCase)
                || string.Equals(wild, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(wild, "true", StringComparison.OrdinalIgnoreCase))
            {
                return "FT_WILD";
            }

            return "FT_STANDARD";
        }

        private static string ResolveGameType(JObject battleInfo)
        {
            var mode = (ReadString(battleInfo, "mode") ?? string.Empty).Trim().ToLowerInvariant();
            switch (mode)
            {
                case "ranked":
                case "ladder":
                    return "GT_RANKED";
                case "casual":
                    return "GT_CASUAL";
                case "arena":
                    return "GT_ARENA";
                case "friendly":
                    return "GT_FRIENDLY";
                case "practice":
                    return "GT_VS_AI";
                default:
                    return "GT_RANKED";
            }
        }

        private static int GetZonePosition(Card card, int fallbackPosition)
        {
            if (card == null)
                return fallbackPosition;
            return card.Index > 0 ? card.Index : Math.Max(0, fallbackPosition);
        }

        private static string GetCardTypeName(Card card, string fallbackType)
        {
            if (!string.IsNullOrWhiteSpace(fallbackType)
                && (string.Equals(fallbackType, "HERO", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fallbackType, "HERO_POWER", StringComparison.OrdinalIgnoreCase)))
            {
                return fallbackType.ToUpperInvariant();
            }

            var type = card?.Type.ToString();
            if (string.IsNullOrWhiteSpace(type)
                || string.Equals(type, "INVALID", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "NONE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "0", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(fallbackType) ? "MINION" : fallbackType.ToUpperInvariant();
            }

            return type.ToUpperInvariant();
        }

        private static bool HasActivatePower(Card card, string cardType)
        {
            if (card == null)
                return false;
            if (string.Equals(cardType, "HERO", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cardType, "HERO_POWER", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cardType, "LOCATION", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return card.Type == Card.CType.MINION || card.Type == Card.CType.SPELL || card.Type == Card.CType.WEAPON;
        }

        private static string GetCardId(Card card)
        {
            if (card?.Template == null)
                return string.Empty;

            return NormalizeCardId(card.Template.Id.ToString());
        }

        private static string NormalizeCardId(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId)
                || string.Equals(cardId, "None", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cardId, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return cardId;
        }

        private static string GetClassName(Card.CClass cardClass)
        {
            var text = cardClass.ToString();
            return string.IsNullOrWhiteSpace(text) ? "NEUTRAL" : text.ToUpperInvariant();
        }

        private static void AddBoolTag(JObject obj, string name, bool value)
        {
            if (value)
                obj[name] = 1;
        }

        private static string ReadString(JObject obj, params string[] names)
        {
            if (obj == null || names == null)
                return null;

            foreach (var name in names)
            {
                if (!obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var value))
                    continue;

                var text = TokenToString(value);
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }

        private static string TokenToString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return string.Empty;

            return token.Type == JTokenType.String
                ? token.Value<string>() ?? string.Empty
                : token.ToString(Formatting.None);
        }

        private static string BuildRequestId(ActionRecommendationRequest request)
        {
            var matchId = request.MatchContext?.MatchId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(matchId))
                return "hearthbot_" + SanitizeToken(matchId);

            var basis = string.Join("|",
                request.Seed ?? string.Empty,
                request.DeckSignature ?? string.Empty,
                request.BoardFingerprint ?? string.Empty,
                request.PlanningBoard?.TurnCount.ToString() ?? string.Empty);
            var hash = unchecked((uint)basis.GetHashCode()).ToString("X8");
            return "hearthbot_" + DateTime.UtcNow.ToString("yyyyMMdd") + "_" + hash;
        }

        private static string SanitizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'
                    ? ch
                    : '_');
            }

            return builder.ToString();
        }
    }

    internal static class HsBoxDirectApiProtocol
    {
        private static readonly byte[] Rc4Key =
        {
            0x65, 0xef, 0x90, 0x9b, 0xda, 0x8e, 0x30, 0xe4,
            0xe2, 0xbc, 0x3c, 0xe8, 0x28, 0xed, 0x87, 0x0e
        };

        public static string EncodeRequestPayload(JToken payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            var json = payload.ToString(Formatting.None);
            var compressed = CompressZlib(Encoding.UTF8.GetBytes(json));
            var encrypted = Rc4(compressed);
            return ToUrlSafeBase64(encrypted);
        }

        public static string DecodeResponseText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text ?? string.Empty;

            var trimmed = text.Trim();
            if (LooksLikeJson(trimmed))
                return trimmed;

            try
            {
                var encrypted = FromUrlSafeBase64(trimmed);
                var decrypted = Rc4(encrypted);
                var decoded = Encoding.UTF8.GetString(decrypted);
                return LooksLikeJson(decoded) ? decoded : trimmed;
            }
            catch
            {
                return trimmed;
            }
        }

        private static byte[] CompressZlib(byte[] bytes)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(bytes, 0, bytes.Length);
            }

            return output.ToArray();
        }

        private static byte[] Rc4(byte[] input)
        {
            var s = new byte[256];
            for (var i = 0; i < s.Length; i++)
                s[i] = (byte)i;

            var j = 0;
            for (var i = 0; i < s.Length; i++)
            {
                j = (j + s[i] + Rc4Key[i % Rc4Key.Length]) & 0xff;
                (s[i], s[j]) = (s[j], s[i]);
            }

            var output = new byte[input.Length];
            var x = 0;
            j = 0;
            for (var index = 0; index < input.Length; index++)
            {
                x = (x + 1) & 0xff;
                j = (j + s[x]) & 0xff;
                (s[x], s[j]) = (s[j], s[x]);
                var keyByte = s[(s[x] + s[j]) & 0xff];
                output[index] = (byte)(input[index] ^ keyByte);
            }

            return output;
        }

        private static string ToUrlSafeBase64(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] FromUrlSafeBase64(string text)
        {
            var base64 = text.Replace('-', '+').Replace('_', '/');
            var remainder = base64.Length % 4;
            if (remainder > 0)
                base64 = base64.PadRight(base64.Length + (4 - remainder), '=');
            return Convert.FromBase64String(base64);
        }

        private static bool LooksLikeJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.TrimStart();
            return trimmed.StartsWith("{", StringComparison.Ordinal)
                || trimmed.StartsWith("[", StringComparison.Ordinal);
        }
    }

    internal sealed class HsBoxDirectApiClient : IHsBoxDirectApiClient
    {
        private static readonly HttpClient SharedHttp = CreateSharedHttpClient();

        private readonly HttpClient _http;

        public HsBoxDirectApiClient()
            : this(SharedHttp)
        {
        }

        internal HsBoxDirectApiClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        private static HttpClient CreateSharedHttpClient()
        {
            return new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            {
                Timeout = TimeSpan.FromSeconds(4)
            };
        }

        public static Uri BuildEndpoint(HsBoxDirectApiKind kind)
        {
            var path = kind switch
            {
                HsBoxDirectApiKind.StandardSubstep => "standard_substep",
                HsBoxDirectApiKind.BattlegroundSubstep => "zq_substep",
                HsBoxDirectApiKind.BattlegroundRearrange => "zq_rearrange",
                HsBoxDirectApiKind.ArenaDraft => "arena_draft",
                HsBoxDirectApiKind.ArenaSubstep => "arena_substep",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };

            return new Uri("https://hs-api.lushi.163.com/hs/predict/" + path);
        }

        public static string GetEndpointName(HsBoxDirectApiKind kind)
        {
            return kind switch
            {
                HsBoxDirectApiKind.StandardSubstep => "standard_substep",
                HsBoxDirectApiKind.BattlegroundSubstep => "zq_substep",
                HsBoxDirectApiKind.BattlegroundRearrange => "zq_rearrange",
                HsBoxDirectApiKind.ArenaDraft => "arena_draft",
                HsBoxDirectApiKind.ArenaSubstep => "arena_substep",
                _ => "unknown"
            };
        }

        private static Uri BuildEndpoint(HsBoxDirectApiKind kind, HsBoxDirectApiProtocolMetadata metadata)
        {
            var query = string.Join("&",
                "sid=" + Uri.EscapeDataString(metadata.Sid),
                "compress=1",
                "rank=" + Uri.EscapeDataString(metadata.Rank),
                "client_type=win",
                "version=" + Uri.EscapeDataString(metadata.Version));

            return new Uri(BuildEndpoint(kind) + "?" + query);
        }

        private static bool TryBuildProtocolMetadata(
            JToken payload,
            out HsBoxDirectApiProtocolMetadata metadata,
            out string detail)
        {
            metadata = null;

            var sid = ReadTopLevelString(payload, "sid", "ls_session_id");
            if (string.IsNullOrWhiteSpace(sid))
            {
                detail = "sid";
                return false;
            }

            var version = ReadTopLevelString(payload, "version", "recommend_version");
            if (string.IsNullOrWhiteSpace(version))
                version = "4.0.4.314";

            var rank = ReadTopLevelString(payload, "rank", "ladder_rank");
            if (string.IsNullOrWhiteSpace(rank))
                rank = "0";

            var clientToken = ReadTopLevelString(payload, "client_uuid", "info_token", "deviceid", "device_id", "uuid");
            if (string.IsNullOrWhiteSpace(clientToken))
                clientToken = "hearthbot";

            metadata = new HsBoxDirectApiProtocolMetadata
            {
                Sid = sid,
                Version = version,
                Rank = rank,
                ClientToken = clientToken
            };
            detail = "ok";
            return true;
        }

        private static string ReadTopLevelString(JToken token, params string[] names)
        {
            if (token is not JObject obj || names == null)
                return string.Empty;

            foreach (var name in names)
            {
                if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var value))
                {
                    var text = TokenToString(value);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return string.Empty;
        }

        private static string TokenToString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return string.Empty;

            return token.Type == JTokenType.String
                ? token.Value<string>() ?? string.Empty
                : token.ToString(Formatting.None);
        }

        public HsBoxDirectApiResponse Post(HsBoxDirectApiRequest request)
        {
            if (request == null)
                return new HsBoxDirectApiResponse { Detail = "request_null" };

            var result = new HsBoxDirectApiResponse
            {
                Detail = "not_sent"
            };

            if (!TryBuildProtocolMetadata(request.Payload, out var metadata, out var metadataDetail))
            {
                result.Detail = "metadata_missing:" + metadataDetail;
                HsBoxDirectApiDiagnostics.TrySave(request, result);
                return result;
            }

            var endpoint = BuildEndpoint(request.Kind, metadata);
            var dataToken = HsBoxDirectApiProtocol.EncodeRequestPayload(request.Payload);

            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
                message.Headers.TryAddWithoutValidation("User-Agent", $"Mozilla/5.0 HSAng/{metadata.Version}/{metadata.ClientToken}");
                message.Headers.Referrer = new Uri("https://hs-web-embed.lushi.163.com/");
                message.Headers.TryAddWithoutValidation("Origin", "https://hs-web-embed.lushi.163.com");
                message.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
                message.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,en,*");
                message.Headers.TryAddWithoutValidation("Cookie", "ls_session_id=" + metadata.Sid);
                message.Content = new StringContent("data=" + dataToken, Encoding.UTF8, "application/x-www-form-urlencoded");

                using var response = _http.SendAsync(message).GetAwaiter().GetResult();
                result.StatusCode = response.StatusCode;
                var rawResponseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                result.ResponseText = HsBoxDirectApiProtocol.DecodeResponseText(rawResponseText);
                result.ResponseJson = TryParseJson(result.ResponseText);

                if (!response.IsSuccessStatusCode)
                {
                    result.Detail = $"http_{(int)response.StatusCode}";
                    HsBoxDirectApiDiagnostics.TrySave(request, result);
                    return result;
                }

                if (!TryBuildRecommendationState(request.Kind, request.Payload, result.ResponseJson, result.ResponseText, out var state, out var parseDetail))
                {
                    result.Detail = parseDetail.StartsWith("api_error:", StringComparison.Ordinal)
                        ? parseDetail
                        : "parse_failed:" + parseDetail;
                    HsBoxDirectApiDiagnostics.TrySave(request, result);
                    return result;
                }

                result.Success = true;
                result.State = state;
                result.Detail = $"ok endpoint={GetEndpointName(request.Kind)}, source={SanitizeDetail(request.Source)}, protocol=hsang_form";
                HsBoxDirectApiDiagnostics.TrySave(request, result);
                return result;
            }
            catch (Exception ex)
            {
                result.Detail = "exception:" + ex.Message;
                HsBoxDirectApiDiagnostics.TrySave(request, result);
                return result;
            }
        }

        private static JToken TryParseJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                return JToken.Parse(text);
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryBuildRecommendationState(
            HsBoxDirectApiKind kind,
            JToken responseJson,
            string responseText,
            out HsBoxRecommendationState state,
            out string detail)
        {
            return TryBuildRecommendationState(kind, null, responseJson, responseText, out state, out detail);
        }

        internal static bool TryBuildRecommendationState(
            HsBoxDirectApiKind kind,
            JToken requestPayload,
            JToken responseJson,
            string responseText,
            out HsBoxRecommendationState state,
            out string detail)
        {
            state = null;
            detail = "json_null";

            var envelopeToken = SelectEnvelopeToken(kind, requestPayload, responseJson);
            if (envelopeToken == null)
            {
                detail = TryDescribeApiError(responseJson, out var apiError)
                    ? apiError
                    : "envelope_missing";
                return false;
            }

            envelopeToken = NormalizeEnvelopeToken(envelopeToken);

            HsBoxRecommendationEnvelope envelope;
            try
            {
                envelope = envelopeToken.ToObject<HsBoxRecommendationEnvelope>();
            }
            catch (Exception ex)
            {
                detail = "envelope_parse:" + ex.Message;
                return false;
            }

            if (envelope == null)
            {
                detail = "envelope_null";
                return false;
            }

            state = new HsBoxRecommendationState
            {
                Ok = string.IsNullOrWhiteSpace(envelope.Error),
                Count = envelope.Data?.Count ?? 0,
                UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Raw = envelopeToken.ToString(Formatting.None),
                RawToken = envelopeToken.DeepClone(),
                BodyText = string.Empty,
                Reason = string.IsNullOrWhiteSpace(envelope.Error) ? "direct_api" : envelope.Error,
                SourceCallback = "direct_api:" + GetEndpointName(kind),
                Title = string.Empty,
                Envelope = envelope
            };

            detail = "ok";
            return true;
        }

        private static JToken SelectEnvelopeToken(HsBoxDirectApiKind kind, JToken requestPayload, JToken responseJson)
        {
            if (responseJson == null)
                return null;

            if (LooksLikeRecommendationEnvelope(responseJson))
                return responseJson;

            if (responseJson is JObject obj)
            {
                var directData = obj["data"];
                if (LooksLikeRecommendationEnvelope(directData))
                    return directData;

                var result = obj["result"];
                if (LooksLikeRecommendationEnvelope(result))
                    return result;

                var recommendation = obj["recommendation"];
                if (LooksLikeRecommendationEnvelope(recommendation))
                    return recommendation;

                if (TryBuildEnvelopeFromDirectRec(kind, requestPayload, obj, out var directRecEnvelope))
                    return directRecEnvelope;
            }

            return null;
        }

        private static bool TryBuildEnvelopeFromDirectRec(
            HsBoxDirectApiKind kind,
            JToken requestPayload,
            JObject responseObj,
            out JToken envelope)
        {
            envelope = null;

            var resultObj = responseObj["result"] as JObject ?? responseObj;
            var rec = resultObj["rec"] as JArray;
            if (rec == null)
                return false;

            var context = HsBoxDirectRecContext.FromPayload(requestPayload);
            var data = new JArray();
            foreach (var entry in rec.OfType<JObject>())
                data.Add(context.BuildStep(entry));

            envelope = new JObject
            {
                ["status"] = 2,
                ["error"] = string.Empty,
                ["data"] = data,
                ["directResultStatus"] = resultObj["status"]?.DeepClone()
            };
            return true;
        }

        private sealed class HsBoxDirectApiProtocolMetadata
        {
            public string Sid { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public string Rank { get; set; } = string.Empty;
            public string ClientToken { get; set; } = string.Empty;
        }

        private sealed class HsBoxDirectRecContext
        {
            private readonly Dictionary<int, HsBoxDirectEntityRef> _entities;
            private readonly Dictionary<int, JObject> _options;

            private HsBoxDirectRecContext(
                Dictionary<int, HsBoxDirectEntityRef> entities,
                Dictionary<int, JObject> options)
            {
                _entities = entities;
                _options = options;
            }

            public static HsBoxDirectRecContext FromPayload(JToken payload)
            {
                var entities = new Dictionary<int, HsBoxDirectEntityRef>();
                var options = new Dictionary<int, JObject>();
                var state = FindLatestState(payload);

                if (state?["entities"] is JObject entityGroups)
                {
                    foreach (var group in entityGroups.Properties())
                    {
                        if (group.Value is not JArray array)
                            continue;

                        foreach (var entityObj in array.OfType<JObject>())
                        {
                            var entityId = ReadInt(entityObj, "ENTITY_ID", "entity_id", "id");
                            if (entityId <= 0)
                                continue;

                            entities[entityId] = new HsBoxDirectEntityRef(entityId, group.Name, entityObj);
                        }
                    }
                }

                if (state?["options"] is JArray optionArray)
                {
                    foreach (var optionObj in optionArray.OfType<JObject>())
                    {
                        var optionId = ReadInt(optionObj, "id", "option_id");
                        if (optionId >= 0)
                            options[optionId] = optionObj;
                    }
                }

                return new HsBoxDirectRecContext(entities, options);
            }

            public JObject BuildStep(JObject rec)
            {
                var optionId = ReadInt(rec, "option_id", "id");
                _options.TryGetValue(optionId, out var option);

                var sourceId = ReadInt(rec, "option_entity_id", "entity_id");
                if (sourceId <= 0)
                    sourceId = ReadInt(option, "entity_id", "ENTITY_ID");

                var targetId = ReadInt(rec, "target_entity_id", "targetEntityId");
                if (targetId <= 0)
                    targetId = ReadInt(rec, "magnetic_target_entity_id", "magneticTargetEntityId");

                var subOptionEntityId = ReadInt(rec, "sub_option_entity_id", "subOptionEntityId");
                var source = GetEntity(sourceId);
                var target = GetEntity(targetId);
                var subOption = GetEntity(subOptionEntityId);
                var actionName = ResolveActionName(optionId, option, source, target, rec);
                var position = Math.Max(0, ReadInt(rec, "position"));

                var step = new JObject
                {
                    ["actionName"] = actionName,
                    ["position"] = position,
                    ["entityId"] = sourceId,
                    ["targetEntityId"] = targetId,
                    ["optionId"] = optionId,
                    ["directRec"] = rec.DeepClone()
                };

                var sourceCard = BuildCardRef(source);
                if (sourceCard != null)
                    step["card"] = sourceCard;

                AddTarget(step, target);

                var subOptionCard = BuildCardRef(subOption);
                if (subOptionCard != null)
                    step["subOption"] = subOptionCard;

                return step;
            }

            private HsBoxDirectEntityRef GetEntity(int entityId)
            {
                return entityId > 0 && _entities.TryGetValue(entityId, out var entity)
                    ? entity
                    : null;
            }

            private static JObject FindLatestState(JToken payload)
            {
                try
                {
                    return payload?.SelectTokens("$..state", false).OfType<JObject>().LastOrDefault();
                }
                catch
                {
                    return null;
                }
            }

            private static string ResolveActionName(
                int optionId,
                JObject option,
                HsBoxDirectEntityRef source,
                HsBoxDirectEntityRef target,
                JObject rec)
            {
                var optionType = ReadString(option, "type").Trim().ToUpperInvariant();
                if (optionId == 0 || string.Equals(optionType, "END_TURN", StringComparison.OrdinalIgnoreCase))
                    return "end_turn";

                var subOptionId = ReadNullableInt(rec, "sub_option_id");
                var hasSubOption = (subOptionId.HasValue && subOptionId.Value >= 0)
                    || ReadInt(rec, "sub_option_entity_id", "subOptionEntityId") > 0;

                if (source == null)
                    return "common_action";

                if (source.IsMyHeroPower)
                    return "hero_skill";

                if (source.IsMyHero && target != null)
                    return "hero_attack";

                if (source.IsMyHand)
                    return ResolvePlayActionName(source.CardType);

                if (source.IsMyLineup)
                {
                    if (string.Equals(source.CardType, "LOCATION", StringComparison.OrdinalIgnoreCase))
                        return "location_power";

                    if (!hasSubOption && target != null && target.IsOpponent)
                        return "minion_attack";

                    return "common_action";
                }

                return "common_action";
            }

            private static string ResolvePlayActionName(string cardType)
            {
                switch ((cardType ?? string.Empty).Trim().ToUpperInvariant())
                {
                    case "MINION":
                        return "play_minion";
                    case "LOCATION":
                        return "play_location";
                    case "WEAPON":
                        return "play_weapon";
                    case "HERO":
                        return "play_hero";
                    default:
                        return "play_special";
                }
            }

            private static void AddTarget(JObject step, HsBoxDirectEntityRef target)
            {
                if (step == null || target == null)
                    return;

                var targetRef = BuildCardRef(target) ?? new JObject();
                if (target.IsOppHero)
                    step["oppTargetHero"] = targetRef;
                else if (target.IsMyHero)
                    step["targetHero"] = targetRef;
                else if (target.IsOppLineup)
                    step["oppTarget"] = targetRef;
                else
                    step["target"] = targetRef;
            }

            private static JObject BuildCardRef(HsBoxDirectEntityRef entity)
            {
                if (entity == null)
                    return null;

                var card = new JObject();
                if (!string.IsNullOrWhiteSpace(entity.CardId))
                    card["cardId"] = entity.CardId;
                if (!string.IsNullOrWhiteSpace(entity.ZoneName))
                    card["zoneName"] = entity.ZoneName;
                if (entity.ZonePosition > 0)
                {
                    card["ZONE_POSITION"] = entity.ZonePosition;
                    card["position"] = entity.ZonePosition;
                }

                return card.HasValues ? card : new JObject();
            }

            private static int ReadInt(JObject obj, params string[] names)
            {
                return ReadNullableInt(obj, names) ?? 0;
            }

            private static int? ReadNullableInt(JObject obj, params string[] names)
            {
                if (obj == null || names == null)
                    return null;

                foreach (var name in names)
                {
                    if (!obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
                        continue;

                    if (token.Type == JTokenType.Integer)
                        return token.Value<int>();

                    if (int.TryParse(TokenToString(token), out var value))
                        return value;
                }

                return null;
            }

            private static string ReadString(JObject obj, params string[] names)
            {
                if (obj == null || names == null)
                    return string.Empty;

                foreach (var name in names)
                {
                    if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
                    {
                        var text = TokenToString(token);
                        if (!string.IsNullOrWhiteSpace(text))
                            return text;
                    }
                }

                return string.Empty;
            }
        }

        private sealed class HsBoxDirectEntityRef
        {
            public HsBoxDirectEntityRef(int entityId, string groupName, JObject token)
            {
                EntityId = entityId;
                GroupName = groupName ?? string.Empty;
                Token = token;
                CardId = ReadString(token, "_CARD_ID", "cardId", "CARD_ID");
                CardType = ReadString(token, "CARDTYPE", "cardType");
                ZonePosition = ReadInt(token, "ZONE_POSITION", "position");
                ZoneName = ResolveZoneName(GroupName, token);
            }

            public int EntityId { get; }
            public string GroupName { get; }
            public JObject Token { get; }
            public string CardId { get; }
            public string CardType { get; }
            public int ZonePosition { get; }
            public string ZoneName { get; }

            public bool IsMyHand => GroupName.IndexOf("my_hands", StringComparison.OrdinalIgnoreCase) >= 0;
            public bool IsMyLineup => GroupName.IndexOf("my_lineup", StringComparison.OrdinalIgnoreCase) >= 0;
            public bool IsMyHeroPower => GroupName.IndexOf("my_hero_power", StringComparison.OrdinalIgnoreCase) >= 0;
            public bool IsMyHero => GroupName.Equals("my_hero", StringComparison.OrdinalIgnoreCase);
            public bool IsOppLineup => GroupName.IndexOf("opp_lineup", StringComparison.OrdinalIgnoreCase) >= 0;
            public bool IsOppHero => GroupName.Equals("opp_hero", StringComparison.OrdinalIgnoreCase);
            public bool IsOpponent => GroupName.StartsWith("opp_", StringComparison.OrdinalIgnoreCase);

            private static string ResolveZoneName(string groupName, JObject token)
            {
                if (groupName.IndexOf("hands", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "hand";
                if (groupName.IndexOf("lineup", StringComparison.OrdinalIgnoreCase) >= 0
                    || groupName.IndexOf("hero", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "play";
                }

                var zone = ReadString(token, "ZONE", "zoneName");
                return string.IsNullOrWhiteSpace(zone)
                    ? string.Empty
                    : zone.ToLowerInvariant();
            }

            private static int ReadInt(JObject obj, params string[] names)
            {
                if (obj == null || names == null)
                    return 0;

                foreach (var name in names)
                {
                    if (!obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
                        continue;

                    if (token.Type == JTokenType.Integer)
                        return token.Value<int>();

                    if (int.TryParse(TokenToString(token), out var value))
                        return value;
                }

                return 0;
            }

            private static string ReadString(JObject obj, params string[] names)
            {
                if (obj == null || names == null)
                    return string.Empty;

                foreach (var name in names)
                {
                    if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
                    {
                        var text = TokenToString(token);
                        if (!string.IsNullOrWhiteSpace(text))
                            return text;
                    }
                }

                return string.Empty;
            }
        }

        private static JToken NormalizeEnvelopeToken(JToken token)
        {
            if (token is not JObject obj)
                return token;

            var status = obj["status"];
            if (status == null || status.Type != JTokenType.Boolean)
                return token;

            var clone = (JObject)obj.DeepClone();
            clone["status"] = status.Value<bool>() ? 1 : 0;
            return clone;
        }

        private static bool LooksLikeRecommendationEnvelope(JToken token)
        {
            if (token is not JObject obj)
                return false;

            return obj["data"] is JArray
                || obj["error"] != null;
        }

        private static bool TryDescribeApiError(JToken responseJson, out string detail)
        {
            detail = string.Empty;

            if (responseJson is not JObject obj)
                return false;

            var msg = obj.Value<string>("msg")
                ?? obj.Value<string>("message")
                ?? obj.Value<string>("error")
                ?? string.Empty;
            var ret = obj["ret"]?.ToString(Formatting.None) ?? string.Empty;
            var status = obj["status"]?.ToString(Formatting.None) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(msg)
                && string.IsNullOrWhiteSpace(ret)
                && string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(msg))
                parts.Add(msg);
            if (!string.IsNullOrWhiteSpace(ret))
                parts.Add("ret=" + ret);
            if (!string.IsNullOrWhiteSpace(status))
                parts.Add("status=" + status);

            detail = "api_error:" + string.Join(",", parts);
            return true;
        }

        private static string SanitizeDetail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return value.Replace("\r", " ").Replace("\n", " ").Replace(";", ",");
        }
    }

    internal sealed class HsBoxDirectNativePayloadProvider : IHsBoxDirectApiPayloadProvider
    {
        private readonly IHsBoxNativeBridgeClient _nativeBridge;

        public HsBoxDirectNativePayloadProvider()
            : this(new HsBoxNativeBridgeClient())
        {
        }

        internal HsBoxDirectNativePayloadProvider(IHsBoxNativeBridgeClient nativeBridge)
        {
            _nativeBridge = nativeBridge ?? throw new ArgumentNullException(nameof(nativeBridge));
        }

        public bool TryCreateConstructedActionRequest(
            ActionRecommendationRequest request,
            out HsBoxDirectApiRequest apiRequest,
            out string detail)
        {
            return TryCreateRequest(HsBoxDirectApiKind.StandardSubstep, out apiRequest, out detail);
        }

        public bool TryCreateBattlegroundActionRequest(
            string bgStateData,
            out HsBoxDirectApiRequest apiRequest,
            out string detail)
        {
            return TryCreateRequest(HsBoxDirectApiKind.BattlegroundSubstep, out apiRequest, out detail);
        }

        private bool TryCreateRequest(HsBoxDirectApiKind kind, out HsBoxDirectApiRequest apiRequest, out string detail)
        {
            apiRequest = null;

            if (!_nativeBridge.TryGetCurrentBattleInfo(out var battleInfo, out var battleDetail))
            {
                detail = "battle_info_unavailable:" + battleDetail;
                return false;
            }

            var payload = battleInfo as JObject;
            if (payload == null)
            {
                detail = "battle_info_not_object";
                return false;
            }

            payload = (JObject)payload.DeepClone();
            EnrichWithBoxParams(payload);
            apiRequest = new HsBoxDirectApiRequest(kind, payload, battleDetail);
            detail = "native_battle_info";
            return true;
        }

        private void EnrichWithBoxParams(JObject payload)
        {
            TryAddString(payload, "btag", "get_btag");
            TryAddString(payload, "deviceid", "get_device_id");
            TryAddString(payload, "sid", "getSid");
            TryAddString(payload, "token", "login_token");
            TryAddString(payload, "urs", "login_user");
            TryAddString(payload, "client_uuid", "get_info_token");

            if (!payload.ContainsKey("version") || !payload.ContainsKey("recommend_version"))
            {
                if (_nativeBridge.TryInvoke("get_version", out var versionReply, out _))
                {
                    var version = HsBoxNativeBridgeClient.ExtractDataString(versionReply);
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        if (!payload.ContainsKey("version"))
                            payload["version"] = version;
                        if (!payload.ContainsKey("recommend_version"))
                            payload["recommend_version"] = version;
                    }
                }
            }
        }

        private void TryAddString(JObject payload, string propertyName, string nativeMethod)
        {
            if (payload.ContainsKey(propertyName))
                return;

            if (!_nativeBridge.TryInvoke(nativeMethod, out var reply, out _))
                return;

            var value = HsBoxNativeBridgeClient.ExtractDataString(reply);
            if (!string.IsNullOrWhiteSpace(value))
                payload[propertyName] = value;
        }
    }

    internal sealed class HsBoxNativeBridgeClient : IHsBoxNativeBridgeClient
    {
        private static readonly HttpClient CdpHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        public bool TryGetCurrentBattleInfo(out JToken battleInfo, out string detail)
        {
            battleInfo = null;

            if (!TryInvoke("get_current_battle_info", out var reply, out detail))
                return false;

            var data = ExtractDataString(reply);
            if (string.IsNullOrWhiteSpace(data))
            {
                detail = "battle_info_empty";
                return false;
            }

            try
            {
                battleInfo = JToken.Parse(data);
                detail = "battle_info_ready";
                return true;
            }
            catch (Exception ex)
            {
                detail = "battle_info_parse:" + ex.Message;
                return false;
            }
        }

        public bool TryInvoke(string method, out JObject reply, out string detail)
        {
            reply = null;
            detail = "not_started";

            var targets = GetDebuggerTargets(out detail);
            if (targets == null || targets.Count == 0)
                return false;

            var expression = BuildInvokeScript(method);
            var failures = new List<string>();
            var selectedTargets = PickTargets(targets);

            if (TryInvokeFromPageState(method, selectedTargets, out reply, out var pageStateDetail))
            {
                detail = "native_ok:" + method + ":" + pageStateDetail;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(pageStateDetail))
                failures.Add(pageStateDetail);

            if (string.Equals(method, "getSid", StringComparison.Ordinal))
            {
                foreach (var target in selectedTargets)
                {
                    var url = target["url"]?.Value<string>() ?? string.Empty;
                    var wsUrl = target["webSocketDebuggerUrl"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(wsUrl))
                        continue;

                    if (TryGetSidFromCdpCookies(wsUrl, url, out var cookieSid, out var cookieDetail))
                    {
                        reply = new JObject
                        {
                            ["method"] = method,
                            ["data"] = cookieSid,
                            ["fallback"] = cookieDetail
                        };
                        detail = "native_ok:" + method + ":" + ShortenTarget(url) + ":cookie";
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(cookieDetail))
                        failures.Add(ShortenTarget(url) + "=" + cookieDetail);
                }
            }

            foreach (var target in PickNativeInvokeTargets(method, selectedTargets))
            {
                var url = target["url"]?.Value<string>() ?? string.Empty;
                var wsUrl = target["webSocketDebuggerUrl"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(wsUrl))
                {
                    continue;
                }

                var json = EvaluateOnPage(wsUrl, expression);
                if (string.IsNullOrWhiteSpace(json))
                {
                    failures.Add(ShortenTarget(url) + "=eval_empty");
                    continue;
                }

                JObject outer;
                try
                {
                    outer = JObject.Parse(json);
                }
                catch (Exception ex)
                {
                    failures.Add(ShortenTarget(url) + "=eval_parse:" + ex.Message);
                    continue;
                }

                if (!outer.Value<bool>("ok"))
                {
                    failures.Add(ShortenTarget(url) + "=" + (outer.Value<string>("reason") ?? "native_not_ok"));
                    continue;
                }

                reply = outer["reply"] as JObject;
                if (reply == null)
                {
                    failures.Add(ShortenTarget(url) + "=reply_missing");
                    continue;
                }

                detail = "native_ok:" + method + ":" + ShortenTarget(url);
                return true;
            }

            detail = failures.Count > 0 ? string.Join("|", failures) : "cdp_targets_no_ws";
            return false;
        }

        public static string ExtractDataString(JObject reply)
        {
            if (reply == null)
                return string.Empty;

            var data = reply["data"];
            if (data == null || data.Type == JTokenType.Null || data.Type == JTokenType.Undefined)
                return string.Empty;

            return data.Type == JTokenType.String
                ? data.Value<string>() ?? string.Empty
                : data.ToString(Formatting.None);
        }

        private static IReadOnlyList<JObject> GetDebuggerTargets(out string detail)
        {
            detail = "cdp_unavailable";
            try
            {
                var json = CdpHttp.GetStringAsync("http://127.0.0.1:9222/json/list").GetAwaiter().GetResult();
                var targets = JArray.Parse(json).OfType<JObject>().ToList();
                if (targets.Count == 0)
                {
                    detail = "cdp_targets_empty";
                    return null;
                }

                detail = "cdp_targets:" + targets.Count;
                return targets;
            }
            catch (Exception ex)
            {
                detail = "cdp_exception:" + ex.Message;
                return null;
            }
        }

        private static JObject PickTarget(IReadOnlyList<JObject> targets)
        {
            return PickTargets(targets).FirstOrDefault();
        }

        private static IReadOnlyList<JObject> PickTargets(IReadOnlyList<JObject> targets)
        {
            var selected = new List<JObject>();
            string UrlOf(JObject obj) => obj["url"]?.Value<string>() ?? string.Empty;
            bool Has(JObject obj, string needle) => UrlOf(obj).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
            void AddFirst(Func<JObject, bool> predicate)
            {
                var target = targets?.FirstOrDefault(obj => predicate(obj) && !selected.Contains(obj));
                if (target == null)
                    return;
                selected.Add(target);
            }

            AddFirst(obj => Has(obj, "/client-jipaiqi/ceframe"));
            AddFirst(obj => Has(obj, "/client-jipaiqi/ladder-opp"));
            AddFirst(obj => Has(obj, "/client-jipaiqi/ladder"));
            AddFirst(obj => Has(obj, "/client-jipaiqi/"));
            AddFirst(obj => Has(obj, "/client-wargame/"));
            AddFirst(obj => Has(obj, "/client-home/"));

            if (targets != null)
            {
                foreach (var target in targets.Where(obj => Has(obj, "hs-web")))
                {
                    if (!selected.Contains(target))
                        selected.Add(target);
                }
            }

            return selected;
        }

        private static IReadOnlyList<JObject> PickNativeInvokeTargets(string method, IReadOnlyList<JObject> selectedTargets)
        {
            if (selectedTargets == null || selectedTargets.Count == 0)
                return Array.Empty<JObject>();

            if (string.Equals(method, "getSid", StringComparison.Ordinal))
                return selectedTargets.Take(1).ToArray();

            return selectedTargets;
        }

        private static bool TryInvokeFromPageState(
            string method,
            IReadOnlyList<JObject> selectedTargets,
            out JObject reply,
            out string detail)
        {
            reply = null;
            detail = string.Empty;

            if (!CanBuildReplyFromPageState(method))
                return false;

            var failures = new List<string>();
            var script = BuildPageStateScript();
            foreach (var target in selectedTargets ?? Array.Empty<JObject>())
            {
                var url = target["url"]?.Value<string>() ?? string.Empty;
                var wsUrl = target["webSocketDebuggerUrl"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(wsUrl))
                    continue;

                var json = EvaluateOnPage(wsUrl, script);
                if (string.IsNullOrWhiteSpace(json))
                {
                    failures.Add(ShortenTarget(url) + "=react_state_eval_empty");
                    continue;
                }

                JObject outer;
                try
                {
                    outer = JObject.Parse(json);
                }
                catch (Exception ex)
                {
                    failures.Add(ShortenTarget(url) + "=react_state_parse:" + ex.Message);
                    continue;
                }

                if (!outer.Value<bool>("ok"))
                {
                    failures.Add(ShortenTarget(url) + "=" + (outer.Value<string>("reason") ?? "react_state_not_ok"));
                    continue;
                }

                var state = outer["state"] as JObject;
                if (state == null)
                {
                    failures.Add(ShortenTarget(url) + "=react_state_missing");
                    continue;
                }

                if (!TryBuildReplyFromPageState(method, state, out reply))
                {
                    failures.Add(ShortenTarget(url) + "=react_state_method_missing");
                    continue;
                }

                detail = ShortenTarget(url) + ":" + (reply.Value<string>("fallback") ?? "react_state");
                return true;
            }

            detail = failures.Count > 0 ? string.Join("|", failures) : string.Empty;
            return false;
        }

        private static bool CanBuildReplyFromPageState(string method)
        {
            switch (method ?? string.Empty)
            {
                case "getSid":
                case "get_device_id":
                case "get_info_token":
                case "get_btag":
                case "get_btag_dz":
                case "login_user":
                case "get_version":
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryBuildReplyFromPageState(string method, JObject state, out JObject reply)
        {
            reply = null;
            if (state == null)
                return false;

            string value = null;
            string source = null;

            switch (method ?? string.Empty)
            {
                case "getSid":
                    value = ReadPageStateString(state, "mySid", "sid", "ls_session_id");
                    source = "mySid";
                    break;
                case "get_device_id":
                    value = ReadPageStateString(state, "myDeviceId", "deviceid", "device_id");
                    source = "myDeviceId";
                    break;
                case "get_info_token":
                    value = ReadPageStateString(state, "myDeviceId", "deviceid", "device_id", "client_uuid", "uuid");
                    source = "myDeviceId";
                    break;
                case "get_btag":
                case "get_btag_dz":
                    value = ReadPageStateString(state, "myBtag", "btag", "mybtag");
                    source = "myBtag";
                    break;
                case "login_user":
                    value = ReadPageStateString(state, "myUrs", "urs", "user");
                    source = "myUrs";
                    break;
                case "get_version":
                    value = ReadPageStateString(state, "version", "recommend_version")
                        ?? ParseHsAngVersion(ReadPageStateString(state, "ua", "userAgent"));
                    source = string.IsNullOrWhiteSpace(ReadPageStateString(state, "version", "recommend_version"))
                        ? "userAgent"
                        : "version";
                    break;
                default:
                    return false;
            }

            if (string.IsNullOrWhiteSpace(value))
                return false;

            reply = new JObject
            {
                ["method"] = method ?? string.Empty,
                ["data"] = value,
                ["fallback"] = "react_state:" + source
            };

            if (string.Equals(method, "get_version", StringComparison.Ordinal))
            {
                var dataSource = ReadPageStateString(state, "dataSource");
                if (!string.IsNullOrWhiteSpace(dataSource))
                    reply["category"] = dataSource;
            }

            return true;
        }

        private static string ReadPageStateString(JObject state, params string[] names)
        {
            if (state == null || names == null)
                return null;

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var token = state[name];
                if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                    continue;

                var value = token.Type == JTokenType.String
                    ? token.Value<string>()
                    : token.ToString(Formatting.None);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        private static string ParseHsAngVersion(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return null;

            const string marker = "HSAng/";
            var index = userAgent.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return null;

            var start = index + marker.Length;
            var end = userAgent.IndexOf('/', start);
            if (end < 0)
                end = userAgent.IndexOf(' ', start);
            if (end < 0)
                end = userAgent.Length;

            return end > start ? userAgent.Substring(start, end - start) : null;
        }

        private static string ShortenTarget(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "unknown";

            try
            {
                var uri = new Uri(url);
                return uri.AbsolutePath.Trim('/').Replace('/', '_');
            }
            catch
            {
                return url.Length <= 64 ? url : url.Substring(0, 64);
            }
        }

        private static string EvaluateOnPage(string webSocketDebuggerUrl, string expression)
        {
            var response = SendCdpCommand(
                webSocketDebuggerUrl,
                "Runtime.evaluate",
                new JObject
                {
                    ["expression"] = expression,
                    ["returnByValue"] = true,
                    ["awaitPromise"] = true
                });

            return response?["result"]?["result"]?["value"]?.Value<string>();
        }

        private static JObject SendCdpCommand(string webSocketDebuggerUrl, string method, JObject parameters)
        {
            if (string.IsNullOrWhiteSpace(webSocketDebuggerUrl) || string.IsNullOrWhiteSpace(method))
                return null;

            using (var socket = new ClientWebSocket())
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            {
                try
                {
                    socket.ConnectAsync(new Uri(webSocketDebuggerUrl), cts.Token).GetAwaiter().GetResult();

                    var request = new JObject
                    {
                        ["id"] = 1,
                        ["method"] = method
                    };
                    if (parameters != null)
                        request["params"] = parameters;

                    var bytes = Encoding.UTF8.GetBytes(request.ToString(Formatting.None));
                    socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token)
                        .GetAwaiter().GetResult();

                    while (true)
                    {
                        var buffer = new byte[32768];
                        using (var stream = new MemoryStream())
                        {
                            WebSocketReceiveResult result;
                            do
                            {
                                result = socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).GetAwaiter().GetResult();
                                if (result.Count > 0)
                                    stream.Write(buffer, 0, result.Count);
                            } while (!result.EndOfMessage);

                            var responseText = Encoding.UTF8.GetString(stream.ToArray());
                            if (string.IsNullOrWhiteSpace(responseText))
                                continue;

                            var response = JObject.Parse(responseText);
                            if (response["id"]?.Value<int>() != 1)
                                continue;

                            return response;
                        }
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        private static string BuildPageStateScript()
        {
            return @"(() => {
  function finish(obj) {
    return JSON.stringify(obj || {});
  }
  function pickState(obj) {
    const state = {};
    const names = ['mySid', 'myDeviceId', 'myBtag', 'myUrs', 'dataSource', 'version', 'recommend_version'];
    for (const name of names) {
      if (Object.prototype.hasOwnProperty.call(obj, name)) state[name] = obj[name];
    }
    state.ua = String(navigator.userAgent || '');
    return state;
  }
  try {
    const seen = new WeakSet();
    function inspectValue(value, depth) {
      if (!value || typeof value !== 'object' || depth > 6) return null;
      if (seen.has(value)) return null;
      seen.add(value);
      let keys = [];
      try { keys = Object.keys(value); } catch (_) { return null; }
      if (Object.prototype.hasOwnProperty.call(value, 'mySid')) return pickState(value);
      for (const key of keys.slice(0, 50)) {
        if (/^__react|^_owner$|^stateNode$/.test(key)) continue;
        try {
          const found = inspectValue(value[key], depth + 1);
          if (found) return found;
        } catch (_) {}
      }
      return null;
    }
    const elements = document.querySelectorAll('*');
    let fiberKey = '';
    for (const element of elements) {
      fiberKey = Object.keys(element).find(key => key.indexOf('__reactFiber') === 0) || fiberKey;
      if (fiberKey) break;
    }
    if (!fiberKey) return finish({ ok: false, reason: 'react_fiber_missing' });
    let visited = 0;
    function walkFiber(node, depth) {
      if (!node || visited++ > 1200 || depth > 50) return null;
      const propsState = inspectValue(node.memoizedProps, 0);
      if (propsState) return propsState;
      let hook = node.memoizedState;
      let hookIndex = 0;
      while (hook && hookIndex < 30) {
        const hookState = inspectValue(hook.memoizedState, 0);
        if (hookState) return hookState;
        hook = hook.next;
        hookIndex++;
      }
      return walkFiber(node.child, depth + 1) || walkFiber(node.sibling, depth);
    }
    for (const element of elements) {
      if (!element[fiberKey]) continue;
      const state = walkFiber(element[fiberKey], 0);
      if (state) return finish({ ok: true, state });
    }
    return finish({ ok: false, reason: 'react_state_missing' });
  } catch (error) {
    return finish({ ok: false, reason: 'react_state_error:' + String(error && error.message ? error.message : error) });
  }
})()";
        }

        private static bool TryGetSidFromCdpCookies(
            string webSocketDebuggerUrl,
            string targetUrl,
            out string sid,
            out string detail)
        {
            sid = string.Empty;
            detail = string.Empty;

            var response = SendCdpCommand(
                webSocketDebuggerUrl,
                "Network.getCookies",
                new JObject
                {
                    ["urls"] = new JArray(BuildCookieUrls(targetUrl))
                });

            if (response == null)
            {
                detail = "cookie_cdp_empty";
                return false;
            }

            if (TryExtractCookieValue(response, "ls_session_id", out sid))
            {
                detail = "cookie_ls_session_id";
                return true;
            }

            detail = "cookie_sid_missing";
            return false;
        }

        private static IEnumerable<string> BuildCookieUrls(string targetUrl)
        {
            if (!string.IsNullOrWhiteSpace(targetUrl)
                && Uri.TryCreate(targetUrl, UriKind.Absolute, out var targetUri)
                && (string.Equals(targetUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(targetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                yield return targetUri.GetLeftPart(UriPartial.Authority);
                yield return targetUrl;
            }

            yield return "https://hs-web-embed.lushi.163.com";
            yield return "https://hs-web-cef.lushi.163.com";
            yield return "https://hs-web.lushi.163.com";
            yield return "https://hs-api.lushi.163.com";
        }

        private static bool TryExtractCookieValue(JObject response, string name, out string value)
        {
            value = string.Empty;
            if (response == null || string.IsNullOrWhiteSpace(name))
                return false;

            var cookies = response["result"]?["cookies"] as JArray
                ?? response["cookies"] as JArray;
            if (cookies == null)
                return false;

            foreach (var cookie in cookies.OfType<JObject>())
            {
                if (!string.Equals(cookie.Value<string>("name"), name, StringComparison.Ordinal))
                    continue;

                value = cookie.Value<string>("value") ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        private static string BuildInvokeScript(string method)
        {
            var methodLiteral = JsonConvert.SerializeObject(method ?? string.Empty);
            return @"(() => new Promise(resolve => {
  const finish = (obj) => resolve(JSON.stringify(obj || {}));
  try {
    const method = " + methodLiteral + @";
    const channel = 'doQuery_hsbox';
    const callback = 'hb_direct_' + Date.now() + '_' + Math.floor(Math.random() * 1000000);
    let done = false;
    const readCookieSid = () => {
      if (method !== 'getSid') return '';
      try {
        const raw = String(document.cookie || '');
        const match = raw.match(/(?:^|;\s*)ls_session_id=([^;]+)/);
        return match && match[1] ? decodeURIComponent(match[1]) : '';
      } catch (_) {
        return '';
      }
    };
    const finishOnce = (obj) => {
      if (done) return;
      done = true;
      try { clearTimeout(timer); } catch (_) {}
      finish(obj);
    };
    const finishSidFromCookie = (reason) => {
      const sid = readCookieSid();
      if (!sid) return false;
      finishOnce({ ok: true, reply: { callback, method, data: sid, fallback: reason } });
      return true;
    };
    const timer = setTimeout(() => {
      if (finishSidFromCookie('cookie_native_timeout')) return;
      finishOnce({ ok: false, reason: 'native_timeout' });
    }, 1800);
    if (!window.app || typeof window.app.sendMessage !== 'function' || typeof window.app.setMessageCallback !== 'function') {
      if (finishSidFromCookie('cookie_app_bridge_missing')) return;
      finishOnce({ ok: false, reason: 'app_bridge_missing' });
      return;
    }
    window.app.setMessageCallback(channel, function(_name, args) {
      try {
        const raw = args && args[0] ? String(args[0]) : '';
        const parsed = raw ? JSON.parse(raw) : {};
        if (parsed.callback !== callback) return;
        finishOnce({ ok: true, reply: parsed });
      } catch (error) {
        finishOnce({ ok: false, reason: 'reply_parse:' + String(error && error.message ? error.message : error) });
      }
    });
    window.app.sendMessage(channel, [JSON.stringify({ callback, method, param: undefined })]);
  } catch (error) {
    finish({ ok: false, reason: String(error && error.message ? error.message : error) });
  }
}))()";
        }
    }

    internal static class HsBoxDirectApiDiagnostics
    {
        private static readonly object Sync = new object();
        private static readonly string RootDirectory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Logs",
            "HsBoxDirectApi");

        public static bool Enabled { get; set; } = true;

        public static void TrySave(HsBoxDirectApiRequest request, HsBoxDirectApiResponse response)
        {
            if (!Enabled || request == null || response == null)
                return;

            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(RootDirectory);
                    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    var endpoint = HsBoxDirectApiClient.GetEndpointName(request.Kind);
                    var path = Path.Combine(RootDirectory, $"{stamp}_{endpoint}.json");
                    var obj = new JObject
                    {
                        ["savedAtLocal"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        ["endpoint"] = endpoint,
                        ["source"] = request.Source ?? string.Empty,
                        ["success"] = response.Success,
                        ["detail"] = response.Detail ?? string.Empty,
                        ["statusCode"] = (int)response.StatusCode,
                        ["request"] = request.Payload.DeepClone(),
                        ["responseText"] = response.ResponseText ?? string.Empty
                    };
                    if (response.ResponseJson != null)
                        obj["responseJson"] = response.ResponseJson.DeepClone();

                    File.WriteAllText(path, obj.ToString(Formatting.Indented), new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }
    }
}
