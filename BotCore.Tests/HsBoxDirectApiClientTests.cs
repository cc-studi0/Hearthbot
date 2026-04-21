using System;
using System.Collections.Generic;
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
        public void Post_ConstructedEndpointSendsUtf8JsonAndParsesEnvelope()
        {
            var handler = new RecordingHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"status\":2,\"error\":\"\",\"turnNum\":9,\"data\":[{\"actionName\":\"end_turn\"}]}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                });
            var client = new HsBoxDirectApiClient(new HttpClient(handler));

            var result = client.Post(
                new HsBoxDirectApiRequest(
                    HsBoxDirectApiKind.StandardSubstep,
                    JObject.Parse("{\"sid\":\"S\",\"turns\":[{\"turn\":1}]}"),
                    "unit_test"));

            Assert.True(result.Success, result.Detail);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(new Uri("https://hs-api.lushi.163.com/hs/predict/standard_substep"), handler.RequestUri);
            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal("application/json", handler.ContentType);
            Assert.Contains("\"sid\":\"S\"", handler.Body);
            Assert.NotNull(result.State);
            Assert.Equal("direct_api:standard_substep", result.State.SourceCallback);
            Assert.Equal(2, result.State.Envelope.Status);
            Assert.Equal("end_turn", Assert.Single(result.State.Envelope.Data).ActionName);
            Assert.True(result.State.UpdatedAtMs > 0);
            Assert.False(string.IsNullOrWhiteSpace(result.State.PayloadSignature));
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
        public void DefaultDirectPayloadProvider_DoesNotUseHsBoxNativeCallbackBridge()
        {
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(null));
            var field = typeof(HsBoxGameRecommendationProvider).GetField(
                "_directPayloadProvider",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(field);
            var payloadProvider = field.GetValue(provider);

            Assert.Equal("HsBoxDirectBoardPayloadProvider", payloadProvider?.GetType().Name);
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

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestUri = request.RequestUri;
                Method = request.Method;
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
