using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BotMain;
using Newtonsoft.Json.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;
using Xunit;

namespace BotCore.Tests
{
    public sealed class HsBoxDirectApiClientTests
    {
        private const string SampleRequestJson =
            "{\"sid\":\"SID-1\",\"uuid\":\"UUID-1\",\"version\":\"4.0.4.314\",\"rank\":73864,\"data\":{\"turns\":[{\"processes\":[{\"state\":{\"entities\":{\"my_hands\":[{\"ENTITY_ID\":10,\"ZONE_POSITION\":1,\"_CARD_ID\":\"CORE_CS2_231\",\"CARDTYPE\":\"MINION\"}],\"my_lineup\":[{\"ENTITY_ID\":20,\"ZONE_POSITION\":1,\"_CARD_ID\":\"TLC_100t2\",\"CARDTYPE\":\"LOCATION\"}],\"opp_hero\":[{\"ENTITY_ID\":64,\"_CARD_ID\":\"HERO_05\",\"CARDTYPE\":\"HERO\"}]},\"options\":[{\"id\":2,\"type\":\"POWER\",\"entity_id\":20,\"error\":\"NONE\"}]}}]}]}}";

        private const string SampleEncryptedResponse =
            "hsZPUfj_WQmIwvSIBplp9MLEp3Uk652TAfb6-MUOhkXNb1q-TU-VfQdHTQcLfG56hDA6tF__Gz70CHeltd2FMftjU1XC6Rww6SDuvUg2Kwj3G5ymDCGvdJ_LT00eqR7jj3eYKQORg7C0VxsZjVIm-H62DjjrIgxkrVdBaqxkaoK8N-X2TgqGBHpzx1B0xk6ioT9fqLQ46PZUUBYomUWjJ9HXzRuGKoWKvI9-uko95DBv9TzrnNr5EThORZwDlsaFZ1RGhXYEn70VMrVih4DscXGsPmYm";

        [Fact]
        public void BuildEndpoint_ReturnsKnownHsBoxPredictUris()
        {
            Assert.Equal(
                "https://hs-api.lushi.163.com/hs/predict/standard_substep",
                HsBoxDirectApiClient.BuildEndpoint(HsBoxDirectApiKind.StandardSubstep).ToString());
            Assert.Equal(
                "https://hs-api.lushi.163.com/hs/predict/zq_substep",
                HsBoxDirectApiClient.BuildEndpoint(HsBoxDirectApiKind.BattlegroundSubstep).ToString());
            Assert.Equal(
                "https://hs-api.lushi.163.com/hs/predict/zq_rearrange",
                HsBoxDirectApiClient.BuildEndpoint(HsBoxDirectApiKind.BattlegroundRearrange).ToString());
        }

        [Fact]
        public void Protocol_EncodesRequestAndDecodesEncryptedResponse()
        {
            var token = HsBoxDirectApiProtocol.EncodeRequestPayload(JObject.Parse(SampleRequestJson));
            var responseJson = HsBoxDirectApiProtocol.DecodeResponseText(SampleEncryptedResponse);

            Assert.Matches("^[A-Za-z0-9_-]+=*$", token);
            Assert.DoesNotContain("{\"sid\"", token, StringComparison.Ordinal);
            Assert.Equal(
                "{\"status\":true,\"msg\":\"ok\",\"result\":{\"status\":200,\"rec\":[{\"option_id\":2,\"option_entity_id\":20,\"sub_option_id\":-1,\"sub_option_entity_id\":0,\"target_entity_id\":64,\"position\":0,\"magnetic_target_entity_id\":0}]},\"ret\":0}",
                responseJson);
        }

        [Fact]
        public void Post_ConstructedEndpointSendsHsAngEncryptedFormAndParsesRec()
        {
            var handler = new RecordingHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleEncryptedResponse)
                });
            var client = new HsBoxDirectApiClient(new HttpClient(handler));
            var expectedToken = HsBoxDirectApiProtocol.EncodeRequestPayload(JObject.Parse(SampleRequestJson));

            var result = client.Post(
                new HsBoxDirectApiRequest(
                    HsBoxDirectApiKind.StandardSubstep,
                    JObject.Parse(SampleRequestJson),
                    "unit_test"));

            Assert.True(result.Success, result.Detail);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(
                "https://hs-api.lushi.163.com/hs/predict/standard_substep?sid=SID-1&compress=1&rank=73864&client_type=win&version=4.0.4.314",
                handler.RequestUri.ToString());
            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
            Assert.Equal("data=" + expectedToken, handler.Body);
            Assert.Contains("HSAng/4.0.4.314/UUID-1", handler.Headers["User-Agent"]);
            Assert.Equal("ls_session_id=SID-1", handler.Headers["Cookie"]);
            Assert.NotNull(result.State);
            Assert.Equal("direct_api:standard_substep", result.State.SourceCallback);
            var step = Assert.Single(result.State.Envelope.Data);
            Assert.Equal("location_power", step.ActionName);
            Assert.Equal("TLC_100t2", step.GetPrimaryCard()?.CardId);
            Assert.NotNull(step.OppTargetHero);
            Assert.True(result.State.UpdatedAtMs > 0);
            Assert.False(string.IsNullOrWhiteSpace(result.State.PayloadSignature));
        }

        [Fact]
        public void Post_WhenApiReturnsBooleanStatusError_ReportsApiErrorWithoutEnvelopeParse()
        {
            var handler = new RecordingHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"status\":false,\"msg\":\"key null\",\"ret\":-1}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                });
            var client = new HsBoxDirectApiClient(new HttpClient(handler));

            var result = client.Post(
                new HsBoxDirectApiRequest(
                    HsBoxDirectApiKind.StandardSubstep,
                    JObject.Parse("{\"sid\":\"SID-1\",\"uuid\":\"UUID-1\",\"version\":\"4.0.4.314\",\"turns\":[{\"turn\":1}]}"),
                    "unit_test"));

            Assert.False(result.Success);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Contains("api_error:key null", result.Detail);
            Assert.DoesNotContain("parse_failed", result.Detail);
            Assert.DoesNotContain("envelope_parse", result.Detail);
        }

        [Fact]
        public void TryBuildRecommendationState_NormalizesBooleanEnvelopeStatus()
        {
            var ok = HsBoxDirectApiClient.TryBuildRecommendationState(
                HsBoxDirectApiKind.StandardSubstep,
                JObject.Parse("{\"status\":true,\"error\":\"\",\"data\":[{\"actionName\":\"end_turn\"}]}"),
                "{\"status\":true}",
                out var state,
                out var detail);

            Assert.True(ok, detail);
            Assert.Equal(1, state.Envelope.Status);
            Assert.Equal("end_turn", Assert.Single(state.Envelope.Data).ActionName);
        }

        [Fact]
        public void DirectPrimaryFallsBackToCefBridge_WhenPayloadIsUnavailable()
        {
            var fallbackState = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 1,
                UpdatedAtMs = 1234,
                SourceCallback = "unit_fallback",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Status = 2,
                    Data = new List<HsBoxActionStep>
                    {
                        new HsBoxActionStep { ActionName = "end_turn" }
                    }
                }
            };
            var provider = new HsBoxGameRecommendationProvider(
                new FakeBridge(fallbackState),
                actionWaitTimeoutMs: 20,
                actionPollIntervalMs: 1);

            provider.SetDirectApiMode(HsBoxDirectApiMode.DirectApiPrimaryWithCefFallback);
            provider.SetDirectApiPayloadProvider(new EmptyDirectPayloadProvider());
            provider.SetDirectApiClient(new ThrowingDirectApiClient());

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", null, null, null));

            Assert.Equal(new[] { "END_TURN" }, result.Actions);
            Assert.Contains("hsbox", result.Detail, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectPrimaryBacksOffTemporarily_AfterKeyNullApiError()
        {
            var fallbackState = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 1,
                UpdatedAtMs = 1234,
                SourceCallback = "unit_fallback",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Status = 2,
                    Data = new List<HsBoxActionStep>
                    {
                        new HsBoxActionStep { ActionName = "end_turn" }
                    }
                }
            };
            var client = new KeyNullDirectApiClient();
            var provider = new HsBoxGameRecommendationProvider(
                new FakeBridge(fallbackState),
                actionWaitTimeoutMs: 20,
                actionPollIntervalMs: 1);

            provider.SetDirectApiMode(HsBoxDirectApiMode.DirectApiPrimaryWithCefFallback);
            provider.SetDirectApiPayloadProvider(new StaticDirectPayloadProvider());
            provider.SetDirectApiClient(client);

            var first = provider.RecommendActions(new ActionRecommendationRequest("seed", null, null, null));
            var second = provider.RecommendActions(new ActionRecommendationRequest("seed", null, null, null));

            Assert.Equal(new[] { "END_TURN" }, first.Actions);
            Assert.Equal(new[] { "END_TURN" }, second.Actions);
            Assert.Equal(1, client.CallCount);
        }

        [Fact]
        public void DirectPrimaryBacksOffTemporarily_AfterNativeBattleInfoTimeout()
        {
            var fallbackState = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 1,
                UpdatedAtMs = 1234,
                SourceCallback = "unit_fallback",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Status = 2,
                    Data = new List<HsBoxActionStep>
                    {
                        new HsBoxActionStep { ActionName = "end_turn" }
                    }
                }
            };
            var payloadProvider = new NativeTimeoutPayloadProvider();
            var provider = new HsBoxGameRecommendationProvider(
                new FakeBridge(fallbackState),
                actionWaitTimeoutMs: 20,
                actionPollIntervalMs: 1);

            provider.SetDirectApiMode(HsBoxDirectApiMode.DirectApiPrimaryWithCefFallback);
            provider.SetDirectApiPayloadProvider(payloadProvider);
            provider.SetDirectApiClient(new ThrowingDirectApiClient());

            var first = provider.RecommendActions(new ActionRecommendationRequest("seed", null, null, null));
            var second = provider.RecommendActions(new ActionRecommendationRequest("seed", null, null, null));

            Assert.Equal(new[] { "END_TURN" }, first.Actions);
            Assert.Equal(new[] { "END_TURN" }, second.Actions);
            Assert.Equal(1, payloadProvider.CallCount);
        }

        [Fact]
        public void DefaultDirectPayloadProvider_UsesHsAngBoardStateForDirectApi()
        {
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(null));
            var field = typeof(HsBoxGameRecommendationProvider).GetField(
                "_directPayloadProvider",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(field);
            var payloadProvider = field.GetValue(provider);

            Assert.Equal("HsBoxDirectHsAngPayloadProvider", payloadProvider?.GetType().Name);
        }

        [Fact]
        public void HsAngPayloadBuilder_BuildsPredictShape_FromPlanningBoardAndBattleInfo()
        {
            var board = new Board
            {
                TurnCount = 5,
                ManaAvailable = 3,
                MaxMana = 5,
                EnemyMaxMana = 4,
                CardsPlayedThisTurn = 1,
                HeroFriend = CreateCard(64, Card.Cards.HERO_03, "友方英雄", "Friendly Hero"),
                HeroEnemy = CreateCard(66, Card.Cards.HERO_05, "敌方英雄", "Enemy Hero"),
                Ability = CreateCard(65, Card.Cards.HERO_03bp, "英雄技能", "Hero Power"),
                EnemyAbility = CreateCard(67, Card.Cards.CS2_034_H2, "敌方技能", "Enemy Power"),
                Hand = new List<Card>
                {
                    CreateCard(11, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(12, Card.Cards.AT_037, "测试牌", "Test Card")
                },
                MinionFriend = new List<Card>
                {
                    CreateCard(21, Card.Cards.CORE_CS2_231, "友方随从", "Friendly Minion")
                },
                MinionEnemy = new List<Card>
                {
                    CreateCard(31, Card.Cards.AT_037, "敌方随从", "Enemy Minion")
                }
            };
            var request = new ActionRecommendationRequest(
                "seed-A",
                board,
                null,
                new[] { Card.Cards.CORE_CS2_231, Card.Cards.CORE_CS2_231, Card.Cards.AT_037 },
                deckName: "测试卡组",
                remainingDeckCards: new[] { Card.Cards.CORE_CS2_231 });
            var battleInfo = JObject.Parse("{\"mybtag\":\"Tester#1234\",\"uuid\":\"battle-uuid\",\"mode\":\"ranked\",\"game_type\":\"standard\"}");
            var boxParams = JObject.Parse("{\"sid\":\"SID-1\",\"client_uuid\":\"CLIENT-1\",\"version\":\"4.0.4.314\"}");

            var ok = HsBoxDirectHsAngPayloadBuilder.TryBuildConstructedPayload(
                request,
                battleInfo,
                boxParams,
                out var payload,
                out var detail);

            Assert.True(ok, detail);
            Assert.Equal("Tester#1234", payload.Value<string>("btag"));
            Assert.Equal("battle-uuid", payload.Value<string>("uuid"));
            Assert.Equal("SID-1", payload.Value<string>("sid"));
            Assert.Equal("CLIENT-1", payload.Value<string>("client_uuid"));
            Assert.Equal("CORE_CS2_231:2,AT_037:1", payload["data"]?["extra_infos"]?.Value<string>("cardgroup"));

            var state = Assert.IsType<JObject>(payload["data"]?["turns"]?[0]?["processes"]?[0]?["state"]);
            Assert.Equal("FT_STANDARD", state["entities"]?["game"]?[0]?.Value<string>("FormatType"));
            Assert.Equal(11, state["entities"]?["my_hands"]?[0]?.Value<int>("ENTITY_ID"));
            Assert.Equal("CORE_CS2_231", state["entities"]?["my_hands"]?[0]?.Value<string>("_CARD_ID"));
            Assert.Equal(1, state["entities"]?["my_hands"]?[0]?.Value<int>("ZONE_POSITION"));
            Assert.Equal(21, state["entities"]?["my_lineup"]?[0]?.Value<int>("ENTITY_ID"));
            Assert.Equal(66, state["entities"]?["opp_hero"]?[0]?.Value<int>("ENTITY_ID"));
            Assert.Contains(
                state["options"].OfType<JObject>(),
                option => option.Value<int>("entity_id") == 11 && option.Value<string>("error") == "NONE");
            Assert.Contains(
                state["options"].OfType<JObject>(),
                option => option.Value<string>("type") == "END_TURN");
        }

        [Fact]
        public void HsAngPayloadBuilder_BuildsCardGroupFromVisibleFriendlyCards_WhenDeckListsAreEmpty()
        {
            var board = new Board
            {
                TurnCount = 3,
                ManaAvailable = 2,
                MaxMana = 3,
                EnemyMaxMana = 3,
                HeroFriend = CreateCard(64, Card.Cards.HERO_03, "友方英雄", "Friendly Hero"),
                HeroEnemy = CreateCard(66, Card.Cards.HERO_05, "敌方英雄", "Enemy Hero"),
                Hand = new List<Card>
                {
                    CreateCard(11, Card.Cards.CORE_CS2_231, "小精灵", "Wisp")
                },
                MinionFriend = new List<Card>
                {
                    CreateCard(21, Card.Cards.AT_037, "友方随从", "Friendly Minion")
                }
            };
            var request = new ActionRecommendationRequest("seed-A", board, null, Array.Empty<Card.Cards>());
            var boxParams = JObject.Parse("{\"sid\":\"SID-1\",\"client_uuid\":\"CLIENT-1\",\"version\":\"4.0.4.314\"}");

            var ok = HsBoxDirectHsAngPayloadBuilder.TryBuildConstructedPayload(
                request,
                new JObject(),
                boxParams,
                out var payload,
                out var detail);

            Assert.True(ok, detail);
            Assert.Equal(
                "CORE_CS2_231:1,AT_037:1",
                payload["data"]?["extra_infos"]?.Value<string>("cardgroup"));
        }

        [Fact]
        public void HsAngPayloadProvider_DoesNotRequireBattleInfo_ForConstructedRequest()
        {
            var board = new Board
            {
                TurnCount = 2,
                ManaAvailable = 1,
                MaxMana = 2,
                EnemyMaxMana = 2,
                HeroFriend = CreateCard(64, Card.Cards.HERO_03, "友方英雄", "Friendly Hero"),
                HeroEnemy = CreateCard(66, Card.Cards.HERO_05, "敌方英雄", "Enemy Hero"),
                Hand = new List<Card>
                {
                    CreateCard(11, Card.Cards.CORE_CS2_231, "小精灵", "Wisp")
                }
            };
            var request = new ActionRecommendationRequest(
                "seed-A",
                board,
                null,
                new[] { Card.Cards.CORE_CS2_231 });
            var nativeBridge = new SidOnlyNativeBridge();
            var provider = new HsBoxDirectHsAngPayloadProvider(nativeBridge);

            var ok = provider.TryCreateConstructedActionRequest(request, out var apiRequest, out var detail);

            Assert.True(ok, detail);
            Assert.Equal(0, nativeBridge.BattleInfoCalls);
            Assert.Equal(HsBoxDirectApiKind.StandardSubstep, apiRequest.Kind);
            var payload = Assert.IsType<JObject>(apiRequest.Payload);
            Assert.Equal("SID-1", payload.Value<string>("sid"));
            Assert.Equal("CLIENT-1", payload.Value<string>("client_uuid"));
            Assert.False(string.IsNullOrWhiteSpace(payload.Value<string>("btag")));
            Assert.False(string.IsNullOrWhiteSpace(payload.Value<string>("uuid")));
            Assert.Contains("getSid=ok", detail);
        }

        [Fact]
        public void NativeBridgeTargetSelection_PrefersJipaiqiBattlePagesOverClientHome()
        {
            var targets = new List<JObject>
            {
                JObject.Parse("{\"url\":\"https://hs-web.lushi.163.com/client-home/\",\"webSocketDebuggerUrl\":\"ws://home\"}"),
                JObject.Parse("{\"url\":\"https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp\",\"webSocketDebuggerUrl\":\"ws://opp\"}"),
                JObject.Parse("{\"url\":\"https://hs-web-cef.lushi.163.com/client-jipaiqi/ceframe\",\"webSocketDebuggerUrl\":\"ws://ceframe\"}")
            };
            var method = typeof(HsBoxNativeBridgeClient).GetMethod(
                "PickTarget",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var selected = Assert.IsType<JObject>(method.Invoke(null, new object[] { targets }));

            Assert.Equal("ws://ceframe", selected.Value<string>("webSocketDebuggerUrl"));
        }

        [Fact]
        public void NativeBridgeTargetSelection_KeepsFallbackTargetsAfterPreferred()
        {
            var targets = new List<JObject>
            {
                JObject.Parse("{\"url\":\"https://hs-web.lushi.163.com/client-home/\",\"webSocketDebuggerUrl\":\"ws://home\"}"),
                JObject.Parse("{\"url\":\"https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp\",\"webSocketDebuggerUrl\":\"ws://opp\"}"),
                JObject.Parse("{\"url\":\"https://hs-web-cef.lushi.163.com/client-jipaiqi/ceframe\",\"webSocketDebuggerUrl\":\"ws://ceframe\"}")
            };
            var method = typeof(HsBoxNativeBridgeClient).GetMethod(
                "PickTargets",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var selected = Assert.IsAssignableFrom<IEnumerable<JObject>>(method.Invoke(null, new object[] { targets }));
            var wsUrls = selected
                .Select(target => target.Value<string>("webSocketDebuggerUrl"))
                .ToArray();

            Assert.Equal(new[] { "ws://ceframe", "ws://opp", "ws://home" }, wsUrls);
        }

        [Fact]
        public void NativeBridgeGetSidScript_CanFallbackToCookie()
        {
            var method = typeof(HsBoxNativeBridgeClient).GetMethod(
                "BuildInvokeScript",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var script = Assert.IsType<string>(method.Invoke(null, new object[] { "getSid" }));

            Assert.Contains("ls_session_id", script);
            Assert.Contains("cookie_native_timeout", script);
        }

        [Fact]
        public void NativeBridgeCookieParser_ReadsLsSessionIdFromCdpCookies()
        {
            var method = typeof(HsBoxNativeBridgeClient).GetMethod(
                "TryExtractCookieValue",
                BindingFlags.Static | BindingFlags.NonPublic);
            var response = JObject.Parse(
                "{\"result\":{\"cookies\":[{\"name\":\"other\",\"value\":\"x\"},{\"name\":\"ls_session_id\",\"value\":\"SID-COOKIE\",\"httpOnly\":true}]}}");
            var args = new object[] { response, "ls_session_id", null };

            Assert.NotNull(method);
            var ok = Assert.IsType<bool>(method.Invoke(null, args));

            Assert.True(ok);
            Assert.Equal("SID-COOKIE", args[2]);
        }

        [Fact]
        public void BoardPayloadProvider_BuildsStandardSubstepRequest_FromPlanningBoard()
        {
            var board = new Board
            {
                TurnCount = 4,
                ManaAvailable = 3,
                MaxMana = 5,
                EnemyMaxMana = 4,
                CardsPlayedThisTurn = 2,
                HeroFriend = CreateCard(1, Card.Cards.EX1_164, "友方英雄", "Friendly Hero"),
                HeroEnemy = CreateCard(2, Card.Cards.AT_037, "敌方英雄", "Enemy Hero"),
                Ability = CreateCard(3, Card.Cards.EX1_164, "英雄技能", "Hero Power"),
                Hand = new List<Card>
                {
                    CreateCard(11, Card.Cards.CORE_CS2_231, "小精灵", "Wisp")
                },
                MinionFriend = new List<Card>
                {
                    CreateCard(21, Card.Cards.CORE_CS2_231, "友方随从", "Friendly Minion")
                },
                MinionEnemy = new List<Card>
                {
                    CreateCard(31, Card.Cards.AT_037, "敌方随从", "Enemy Minion")
                }
            };

            var provider = new HsBoxDirectBoardPayloadProvider();
            var request = new ActionRecommendationRequest(
                "seed-A",
                board,
                null,
                new[] { Card.Cards.CORE_CS2_231, Card.Cards.AT_037 },
                deckName: "测试卡组",
                deckSignature: "deck-sig",
                remainingDeckCards: new[] { Card.Cards.EX1_164 },
                matchContext: new MatchContextSnapshot
                {
                    MatchId = "match-1",
                    TurnCount = 4,
                    ObservedAtMs = 123456
                },
                boardFingerprint: "board-fp");

            var ok = provider.TryCreateConstructedActionRequest(request, out var apiRequest, out var detail);

            Assert.True(ok, detail);
            Assert.Equal(HsBoxDirectApiKind.StandardSubstep, apiRequest.Kind);
            Assert.Equal("board_state", apiRequest.Source);

            var payload = Assert.IsType<JObject>(apiRequest.Payload);
            Assert.Equal("normal", payload.Value<string>("type"));
            Assert.Equal("hearthbot", payload.Value<string>("source"));
            Assert.Equal("seed-A", payload.Value<string>("seed"));
            Assert.Equal("测试卡组", payload.Value<string>("deckName"));
            Assert.Equal("deck-sig", payload.Value<string>("deckSignature"));
            Assert.Equal("board-fp", payload.Value<string>("boardFingerprint"));

            var turn = Assert.IsType<JObject>(payload["turns"]?[0]?["turn"]);
            Assert.Equal(4, turn.Value<int>("turn"));
            Assert.Equal(3, turn.Value<int>("mana"));
            Assert.Equal(5, turn.Value<int>("maxMana"));
            Assert.Equal(4, turn.Value<int>("enemyMaxMana"));
            Assert.Equal(2, turn.Value<int>("cardsPlayedThisTurn"));
            Assert.Equal("CORE_CS2_231", turn["hand"]?[0]?.Value<string>("cardId"));
            Assert.Equal(11, turn["hand"]?[0]?.Value<int>("entityId"));
            Assert.Null(turn["hand"]?[0]?["zone_position"]);
            Assert.Equal(1, turn["hand"]?[0]?.Value<int>("ZONE_POSITION"));
            Assert.Equal("CORE_CS2_231", turn["friendlyBoard"]?[0]?.Value<string>("cardId"));
            Assert.Equal("AT_037", turn["enemyBoard"]?[0]?.Value<string>("cardId"));
            Assert.Equal("EX1_164", turn["remainingDeck"]?[0]?.Value<string>("cardId"));
        }

        private static Card CreateCard(int entityId, Card.Cards id, string nameCn, string name)
        {
            return new Card
            {
                Id = entityId,
                Template = CreateTemplate(id, nameCn, name),
                CurrentCost = 1,
                CurrentAtk = 1,
                CurrentHealth = 1,
                MaxHealth = 1,
                IsFriend = true
            };
        }

        private static CardTemplate CreateTemplate(Card.Cards id, string nameCn, string name)
        {
            var template = (CardTemplate)RuntimeHelpers.GetUninitializedObject(typeof(CardTemplate));
            template.Id = id;
            template.NameCN = nameCn;
            template.Name = name;
            return template;
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;

            public RecordingHandler(HttpResponseMessage response)
            {
                _response = response;
            }

            public Uri RequestUri { get; private set; }
            public HttpMethod Method { get; private set; }
            public string ContentType { get; private set; }
            public string Body { get; private set; } = string.Empty;
            public Dictionary<string, string> Headers { get; private set; } = new Dictionary<string, string>();

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestUri = request.RequestUri;
                Method = request.Method;
                Headers = request.Headers.ToDictionary(
                    pair => pair.Key,
                    pair => string.Join(",", pair.Value),
                    StringComparer.OrdinalIgnoreCase);
                ContentType = request.Content?.Headers.ContentType?.MediaType;
                Body = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                return _response;
            }
        }

        private sealed class EmptyDirectPayloadProvider : IHsBoxDirectApiPayloadProvider
        {
            public bool TryCreateConstructedActionRequest(
                ActionRecommendationRequest request,
                out HsBoxDirectApiRequest apiRequest,
                out string detail)
            {
                apiRequest = null;
                detail = "unit_payload_missing";
                return false;
            }

            public bool TryCreateBattlegroundActionRequest(
                string bgStateData,
                out HsBoxDirectApiRequest apiRequest,
                out string detail)
            {
                apiRequest = null;
                detail = "unit_payload_missing";
                return false;
            }
        }

        private sealed class NativeTimeoutPayloadProvider : IHsBoxDirectApiPayloadProvider
        {
            public int CallCount { get; private set; }

            public bool TryCreateConstructedActionRequest(
                ActionRecommendationRequest request,
                out HsBoxDirectApiRequest apiRequest,
                out string detail)
            {
                CallCount++;
                apiRequest = null;
                detail = "battle_info_unavailable:native_timeout";
                return false;
            }

            public bool TryCreateBattlegroundActionRequest(
                string bgStateData,
                out HsBoxDirectApiRequest apiRequest,
                out string detail)
            {
                apiRequest = null;
                detail = "unused";
                return false;
            }
        }

        private sealed class StaticDirectPayloadProvider : IHsBoxDirectApiPayloadProvider
        {
            public bool TryCreateConstructedActionRequest(
                ActionRecommendationRequest request,
                out HsBoxDirectApiRequest apiRequest,
                out string detail)
            {
                apiRequest = new HsBoxDirectApiRequest(
                    HsBoxDirectApiKind.StandardSubstep,
                    JObject.Parse("{\"turns\":[{\"turn\":1}]}"),
                    "unit");
                detail = "unit";
                return true;
            }

            public bool TryCreateBattlegroundActionRequest(
                string bgStateData,
                out HsBoxDirectApiRequest apiRequest,
                out string detail)
            {
                apiRequest = null;
                detail = "unused";
                return false;
            }
        }

        private sealed class SidOnlyNativeBridge : IHsBoxNativeBridgeClient
        {
            public int BattleInfoCalls { get; private set; }

            public bool TryGetCurrentBattleInfo(out JToken battleInfo, out string detail)
            {
                BattleInfoCalls++;
                battleInfo = null;
                detail = "native_timeout";
                return false;
            }

            public bool TryInvoke(string method, out JObject reply, out string detail)
            {
                reply = null;
                detail = "native_ok:" + method;

                switch (method)
                {
                    case "getSid":
                        reply = JObject.Parse("{\"data\":\"SID-1\"}");
                        return true;
                    case "get_info_token":
                        reply = JObject.Parse("{\"data\":\"CLIENT-1\"}");
                        return true;
                    case "get_version":
                        reply = JObject.Parse("{\"data\":\"4.0.4.314\"}");
                        return true;
                    default:
                        detail = "unexpected_method:" + method;
                        return false;
                }
            }
        }

        private sealed class KeyNullDirectApiClient : IHsBoxDirectApiClient
        {
            public int CallCount { get; private set; }

            public HsBoxDirectApiResponse Post(HsBoxDirectApiRequest request)
            {
                CallCount++;
                return new HsBoxDirectApiResponse
                {
                    Success = false,
                    Detail = "api_error:key null,ret=-1,status=false"
                };
            }
        }

        private sealed class ThrowingDirectApiClient : IHsBoxDirectApiClient
        {
            public HsBoxDirectApiResponse Post(HsBoxDirectApiRequest request)
            {
                throw new InvalidOperationException("direct client must not be called without payload");
            }
        }

        private sealed class FakeBridge : IHsBoxRecommendationBridge
        {
            private readonly HsBoxRecommendationState _state;

            public FakeBridge(HsBoxRecommendationState state)
            {
                _state = state;
            }

            public bool TryReadState(out HsBoxRecommendationState state, out string detail)
            {
                state = _state;
                detail = "ready_callback";
                return true;
            }
        }
    }
}
