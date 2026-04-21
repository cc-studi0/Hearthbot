using System;
using System.Collections.Generic;
using System.IO;
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
                ["zone_position"] = position,
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

    internal sealed class HsBoxDirectApiClient : IHsBoxDirectApiClient
    {
        private static readonly HttpClient SharedHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(4)
        };

        private readonly HttpClient _http;

        public HsBoxDirectApiClient()
            : this(SharedHttp)
        {
        }

        internal HsBoxDirectApiClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
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

        public HsBoxDirectApiResponse Post(HsBoxDirectApiRequest request)
        {
            if (request == null)
                return new HsBoxDirectApiResponse { Detail = "request_null" };

            var endpoint = BuildEndpoint(request.Kind);
            var payloadText = request.Payload.ToString(Formatting.None);
            var result = new HsBoxDirectApiResponse
            {
                Detail = "not_sent"
            };

            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                message.Headers.UserAgent.ParseAdd("Hearthbot-HsBoxDirect/1.0");
                message.Headers.Referrer = new Uri("https://hs-web-embed.lushi.163.com/");
                message.Headers.TryAddWithoutValidation("Origin", "https://hs-web-embed.lushi.163.com");
                message.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                message.Content = new StringContent(payloadText, Encoding.UTF8, "application/json");

                using var response = _http.SendAsync(message).GetAwaiter().GetResult();
                result.StatusCode = response.StatusCode;
                result.ResponseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                result.ResponseJson = TryParseJson(result.ResponseText);

                if (!response.IsSuccessStatusCode)
                {
                    result.Detail = $"http_{(int)response.StatusCode}";
                    HsBoxDirectApiDiagnostics.TrySave(request, result);
                    return result;
                }

                if (!TryBuildRecommendationState(request.Kind, result.ResponseJson, result.ResponseText, out var state, out var parseDetail))
                {
                    result.Detail = "parse_failed:" + parseDetail;
                    HsBoxDirectApiDiagnostics.TrySave(request, result);
                    return result;
                }

                result.Success = true;
                result.State = state;
                result.Detail = $"ok endpoint={GetEndpointName(request.Kind)}, source={SanitizeDetail(request.Source)}";
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
            state = null;
            detail = "json_null";

            var envelopeToken = SelectEnvelopeToken(responseJson);
            if (envelopeToken == null)
            {
                detail = "envelope_missing";
                return false;
            }

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

        private static JToken SelectEnvelopeToken(JToken responseJson)
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
            }

            return null;
        }

        private static bool LooksLikeRecommendationEnvelope(JToken token)
        {
            if (token is not JObject obj)
                return false;

            return obj["data"] is JArray
                || obj["status"] != null
                || obj["error"] != null;
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
        private readonly HsBoxNativeBridgeClient _nativeBridge;

        public HsBoxDirectNativePayloadProvider()
            : this(new HsBoxNativeBridgeClient())
        {
        }

        internal HsBoxDirectNativePayloadProvider(HsBoxNativeBridgeClient nativeBridge)
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
            TryAddString(payload, "uuid", "get_info_token");

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

    internal sealed class HsBoxNativeBridgeClient
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

            var wsUrl = GetDebuggerUrl(out detail);
            if (string.IsNullOrWhiteSpace(wsUrl))
                return false;

            var json = EvaluateOnPage(wsUrl, BuildInvokeScript(method));
            if (string.IsNullOrWhiteSpace(json))
            {
                detail = "eval_empty";
                return false;
            }

            JObject outer;
            try
            {
                outer = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                detail = "eval_parse:" + ex.Message;
                return false;
            }

            if (!outer.Value<bool>("ok"))
            {
                detail = outer.Value<string>("reason") ?? "native_not_ok";
                return false;
            }

            reply = outer["reply"] as JObject;
            if (reply == null)
            {
                detail = "reply_missing";
                return false;
            }

            detail = "native_ok:" + method;
            return true;
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

        private static string GetDebuggerUrl(out string detail)
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

                var target = PickTarget(targets);
                var wsUrl = target?["webSocketDebuggerUrl"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(wsUrl))
                {
                    detail = "cdp_ws_missing";
                    return null;
                }

                detail = "cdp_target:" + (target["url"]?.Value<string>() ?? string.Empty);
                return wsUrl;
            }
            catch (Exception ex)
            {
                detail = "cdp_exception:" + ex.Message;
                return null;
            }
        }

        private static JObject PickTarget(IReadOnlyList<JObject> targets)
        {
            string UrlOf(JObject obj) => obj["url"]?.Value<string>() ?? string.Empty;
            bool Has(JObject obj, string needle) => UrlOf(obj).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

            return targets.FirstOrDefault(obj => Has(obj, "/client-home/"))
                ?? targets.FirstOrDefault(obj => Has(obj, "/client-jipaiqi/"))
                ?? targets.FirstOrDefault(obj => Has(obj, "/client-wargame/"))
                ?? targets.FirstOrDefault(obj => Has(obj, "hs-web"));
        }

        private static string EvaluateOnPage(string webSocketDebuggerUrl, string expression)
        {
            using (var socket = new ClientWebSocket())
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            {
                try
                {
                    socket.ConnectAsync(new Uri(webSocketDebuggerUrl), cts.Token).GetAwaiter().GetResult();

                    var request = new JObject
                    {
                        ["id"] = 1,
                        ["method"] = "Runtime.evaluate",
                        ["params"] = new JObject
                        {
                            ["expression"] = expression,
                            ["returnByValue"] = true,
                            ["awaitPromise"] = true
                        }
                    };

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

                            return response["result"]?["result"]?["value"]?.Value<string>();
                        }
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        private static string BuildInvokeScript(string method)
        {
            var methodLiteral = JsonConvert.SerializeObject(method ?? string.Empty);
            return @"(() => new Promise(resolve => {
  const finish = (obj) => resolve(JSON.stringify(obj || {}));
  try {
    if (!window.app || typeof window.app.sendMessage !== 'function' || typeof window.app.setMessageCallback !== 'function') {
      finish({ ok: false, reason: 'app_bridge_missing' });
      return;
    }
    const channel = 'doQuery_hsbox';
    const callback = 'hb_direct_' + Date.now() + '_' + Math.floor(Math.random() * 1000000);
    let done = false;
    const finishOnce = (obj) => {
      if (done) return;
      done = true;
      try { clearTimeout(timer); } catch (_) {}
      finish(obj);
    };
    const timer = setTimeout(() => finishOnce({ ok: false, reason: 'native_timeout' }), 1800);
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
    window.app.sendMessage(channel, [JSON.stringify({ callback, method: " + methodLiteral + @", param: undefined })]);
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
