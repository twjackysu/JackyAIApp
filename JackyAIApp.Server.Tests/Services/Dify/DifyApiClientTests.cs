using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Dify;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;

namespace JackyAIApp.Server.Tests.Services.Dify
{
    /// <summary>
    /// Offline coverage for <see cref="DifyApiClient.GetConversationsAsync"/> — the only Dify
    /// endpoint that's read-only/idempotent enough to even consider hitting live
    /// (THIRD_PARTY_CONTRACT_TESTS_PLAN.md section C3). SendChatStreamAsync triggers a real,
    /// billed LLM completion and DeleteConversationAsync is destructive — neither is exercised
    /// anywhere in this test project, offline or live.
    ///
    /// <para>
    /// This is C3's *only* contract coverage — there is deliberately no
    /// <c>DifyContractTests.cs</c>/live half. Calling GetConversationsAsync for real requires a
    /// live API key, and this repo's real key lives only in the untracked local
    /// <c>appsettings.json</c> (see CLAUDE.md security notes — never committed, no GitHub Secret
    /// exists for it either). A synthetic/never-chatted "contract-test-probe-user" id was the
    /// candidate safe-to-call path per the plan, but actually issuing that call from this sandbox
    /// was blocked by the harness's own credential-leakage guard when the key had to flow through
    /// a shell command to reach the request — which is itself the right outcome: a live Dify
    /// contract test would need that same real key sitting in a GitHub Secret and being read by
    /// CI, for a payload feature (chat) that's explicitly excluded from this test suite's scope.
    /// Given that, offline coverage of the response envelope shape below is the appropriate bar
    /// for C3, not a live test that would need its own secret-handling story to justify.
    /// </para>
    ///
    /// This also doubles as the "envelope shape" test for <see cref="DifyConversationListResponse"/>,
    /// the DTO ChatbotController.GetConversations deserializes GetConversationsAsync's raw body
    /// into.
    /// </summary>
    public class DifyApiClientTests
    {
        private static DifyApiClient CreateClient(
            Func<HttpRequestMessage, HttpResponseMessage> responder, string? apiKey = "test-api-key")
        {
            var httpClient = StubHttpMessageHandler.CreateClient(responder);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var settings = new Settings { Google = new Google(), DifyApiKey = apiKey };
            var optionsMonitor = new Mock<IOptionsMonitor<Settings>>();
            optionsMonitor.Setup(o => o.CurrentValue).Returns(settings);

            return new DifyApiClient(httpClientFactory.Object, NullLogger<DifyApiClient>.Instance, optionsMonitor.Object);
        }

        [Fact]
        public async Task GetConversationsAsync_Success_ReturnsRawBodyThatDeserializesIntoExpectedDto()
        {
            const string json = """
            {"data":[{"id":"conv-1","name":"Test chat","created_at":1751000000,"updated_at":1751000100}],"has_more":false}
            """;
            HttpRequestMessage? capturedRequest = null;
            var client = CreateClient(req => { capturedRequest = req; return StubHttpMessageHandler.JsonResponse(json); });

            var (ok, body) = await client.GetConversationsAsync("user-123");

            Assert.True(ok);
            Assert.NotNull(capturedRequest);
            Assert.Contains("user=user-123", capturedRequest!.RequestUri!.ToString());
            Assert.Contains("/conversations", capturedRequest.RequestUri!.ToString());
            Assert.Equal("Bearer test-api-key", capturedRequest.Headers.Authorization!.ToString());

            var parsed = JsonSerializer.Deserialize<DifyConversationListResponse>(body);
            Assert.NotNull(parsed);
            var item = Assert.Single(parsed!.Data);
            Assert.Equal("conv-1", item.Id);
            Assert.Equal("Test chat", item.Name);
        }

        [Fact]
        public async Task GetConversationsAsync_EmptyDataArray_DeserializesToEmptyListNotNull()
        {
            // The expected shape for a synthetic/never-chatted userId: envelope present, data empty.
            const string json = """{"data":[],"has_more":false}""";
            var client = CreateClient(_ => StubHttpMessageHandler.JsonResponse(json));

            var (ok, body) = await client.GetConversationsAsync("contract-test-probe-user");

            Assert.True(ok);
            var parsed = JsonSerializer.Deserialize<DifyConversationListResponse>(body);
            Assert.NotNull(parsed);
            Assert.Empty(parsed!.Data);
        }

        [Fact]
        public async Task GetConversationsAsync_MissingApiKey_ReturnsFalseWithoutCallingNetwork()
        {
            var called = false;
            var client = CreateClient(_ => { called = true; return StubHttpMessageHandler.JsonResponse("{}"); }, apiKey: null);

            var (ok, body) = await client.GetConversationsAsync("user-123");

            Assert.False(ok);
            Assert.Equal(string.Empty, body);
            Assert.False(called, "should short-circuit before making any HTTP request when no API key is configured");
        }

        [Fact]
        public async Task GetConversationsAsync_NonSuccessStatusCode_ReturnsFalseWithBody()
        {
            const string errorBody = """{"code":"unauthorized","message":"Access token is invalid"}""";
            var client = CreateClient(_ => StubHttpMessageHandler.JsonResponse(errorBody, System.Net.HttpStatusCode.Unauthorized));

            var (ok, body) = await client.GetConversationsAsync("user-123");

            Assert.False(ok);
            Assert.Equal(errorBody, body);
        }

        [Fact]
        public async Task GetConversationsAsync_NetworkException_ReturnsFalseInsteadOfThrowing()
        {
            var client = CreateClient(_ => throw new HttpRequestException("network down"));

            var (ok, body) = await client.GetConversationsAsync("user-123");

            Assert.False(ok);
            Assert.Equal(string.Empty, body);
        }

        [Fact]
        public void DifyConversationListResponse_MissingOptionalFields_DoesNotThrow()
        {
            // A conversation item with only "id" set (name/timestamps absent) should still
            // deserialize — mirrors the "missing/null fields don't throw" bar from the plan.
            const string json = """{"data":[{"id":"conv-minimal"}]}""";

            var parsed = JsonSerializer.Deserialize<DifyConversationListResponse>(json);

            Assert.NotNull(parsed);
            var item = Assert.Single(parsed!.Data);
            Assert.Equal("conv-minimal", item.Id);
            Assert.Null(item.Name);
        }
    }
}
