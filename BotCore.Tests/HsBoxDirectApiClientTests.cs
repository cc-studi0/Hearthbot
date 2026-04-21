using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BotMain;
using Newtonsoft.Json.Linq;
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
