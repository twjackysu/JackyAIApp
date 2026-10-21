using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Xml.Linq;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Services.AiUsage;
using JackyAIApp.Server.Services.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Speech
{
    /// <summary>
    /// The Azure TTS path has to be indistinguishable from the OpenAI one to its callers:
    /// AudioController charges a credit on a cache miss and then plays whatever bytes come back,
    /// so a failure that returns "success" is heard as silence the user paid for.
    ///
    /// These tests cover the translation layer (OpenAI-flavoured arguments -> SSML) and the
    /// failure classification. They deliberately do not call Azure — the request that reaches the
    /// handler is asserted instead, which is also what pins the SSML escaping.
    /// </summary>
    public class AzureSpeechTextToSpeechServiceTests
    {
        private const string Text = "Hello there.";
        private static readonly byte[] Mp3 = [0xFF, 0xFB, 0x90, 0x00];

        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly Mock<IAiUsageLogger> _usage = new();
        private readonly Mock<ILogger<AzureSpeechTextToSpeechService>> _logger = new();
        private readonly CapturingHandler _handler = new();

        // ── Success path ──────────────────────────────────────────────────────

        [Fact]
        public async Task Synthesize_ReturnsTheAudioAndCachesItUnderTheCallersModel()
        {
            var result = await Create().SynthesizeAsync(Text, "alloy", "tts-1", 1.0f, "user-1");

            Assert.True(result.Success);
            Assert.Equal(Mp3, result.Audio);
            // The caller's model — not this provider's — keeps the key, because AudioController
            // computes the same key via TryGetCached to decide whether to bill.
            Assert.Equal(1, _cache.Count);
            Assert.Equal(Mp3, _cache.Get<byte[]>(TtsCache.Key(Text, "alloy", "tts-1", 1.0f)));
        }

        [Fact]
        public async Task Synthesize_PostsSsmlToTheConfiguredRegionWithTheKey()
        {
            await Create().SynthesizeAsync(Text, "alloy", "tts-1", 1.0f, null);

            var request = _handler.LastRequest!;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "https://japaneast.tts.speech.microsoft.com/cognitiveservices/v1",
                request.RequestUri!.ToString());
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("Ocp-Apim-Subscription-Key")));
            // Without this header Azure returns a WAV stream, which the <audio> element the
            // frontend uses is served as audio/mpeg — a format the browser would then misread.
            Assert.Contains("mp3", Assert.Single(request.Headers.GetValues("X-Microsoft-OutputFormat")));
            Assert.Equal("application/ssml+xml", request.Content!.Headers.ContentType!.MediaType);
        }

        [Theory]
        [InlineData("alloy", "en-US-AlloyTurboMultilingualNeural")]
        [InlineData("ALLOY", "en-US-AlloyTurboMultilingualNeural")]
        [InlineData("shimmer", "en-US-ShimmerTurboMultilingualNeural")]
        [InlineData("onyx", "en-US-OnyxTurboMultilingualNeural")]
        // Not an OpenAI voice name and not an Azure one either: fall back rather than send a name
        // Azure will reject, which would turn a typo in a query string into a failed playback.
        [InlineData("nonsense", "en-US-AlloyTurboMultilingualNeural")]
        // Already an Azure voice: passed through, so a caller can use one with no OpenAI analogue.
        [InlineData("en-US-JennyNeural", "en-US-JennyNeural")]
        public async Task Synthesize_MapsTheOpenAiVoiceNameOntoAnAzureVoice(string requested, string expected)
        {
            await Create().SynthesizeAsync(Text, requested, "tts-1", 1.0f, null);

            Assert.Equal(expected, VoiceOf(_handler.LastBody!));
        }

        [Theory]
        // OpenAI's speed is a multiplier; Azure's prosody rate is a delta from the natural rate.
        [InlineData(1.0f, "0%")]
        [InlineData(0.5f, "-50%")] // AudioController's /slow endpoint
        [InlineData(1.25f, "+25%")]
        public async Task Synthesize_TranslatesSpeedIntoAProsodyRate(float speed, string expectedRate)
        {
            await Create().SynthesizeAsync(Text, "alloy", "tts-1", speed, null);

            Assert.Equal(expectedRate, RateOf(_handler.LastBody!));
        }

        [Fact]
        public async Task Synthesize_EscapesTheTextSoPunctuationDoesNotBreakTheSsml()
        {
            // AudioController's ?text= is anonymous and unfiltered, so "R&D" or "a < b" arrives
            // verbatim. Unescaped, the request is malformed XML and Azure answers 400.
            const string awkward = "Tom & Jerry <b> \"quoted\" 'single'";

            var result = await Create().SynthesizeAsync(awkward, "alloy", "tts-1", 1.0f, null);

            Assert.True(result.Success);
            Assert.Equal(awkward, TextOf(_handler.LastBody!)); // round-trips through a real XML parse
        }

        // ── Failure paths ─────────────────────────────────────────────────────

        [Fact]
        public async Task Synthesize_NonSuccessStatus_ReportsFailureAndCachesNothing()
        {
            _handler.Respond(HttpStatusCode.BadRequest, "Invalid voice name");

            var result = await Create().SynthesizeAsync(Text, "alloy", "tts-1", 1.0f, null);

            Assert.False(result.Success);
            Assert.Null(result.Audio);
            Assert.Equal(0, _cache.Count);
        }

        [Fact]
        public async Task Synthesize_EmptyBodyOnA200_IsTreatedAsFailure()
        {
            // Would otherwise reach the browser as a zero-length clip: no error anywhere, just a
            // pronunciation button that does nothing.
            _handler.Respond(HttpStatusCode.OK, string.Empty);

            Assert.False((await Create().SynthesizeAsync(Text, "alloy", "tts-1", 1.0f, null)).Success);
        }

        [Fact]
        public async Task Synthesize_TransportFailure_IsContainedRatherThanThrown()
        {
            // Callers return a 500-shaped error envelope on a false result; an exception escaping
            // here would instead surface as an unhandled 500 with no usage row written.
            _handler.Throw(new HttpRequestException("socket closed"));

            Assert.False((await Create().SynthesizeAsync(Text, "alloy", "tts-1", 1.0f, null)).Success);
        }

        [Fact]
        public async Task Synthesize_MissingConfiguration_FailsWithoutCallingAzure()
        {
            var service = Create(new AzureSpeechOptions { Key = null, Region = "japaneast" });

            Assert.False((await service.SynthesizeAsync(Text, "alloy", "tts-1", 1.0f, null)).Success);
            Assert.Null(_handler.LastRequest);
        }

        // ── Usage logging ─────────────────────────────────────────────────────

        [Fact]
        public async Task Synthesize_LogsUsageUnderAzuresOwnModelId()
        {
            // Logging the caller's "tts-1" would price free F0 synthesis at OpenAI's rate on the
            // usage dashboard.
            await Create().SynthesizeAsync(Text, "alloy", "tts-1", 1.0f, "user-1");

            _usage.Verify(x => x.LogTtsAsync("azure-neural-tts", Text.Length, true, "user-1"), Times.Once);
        }

        [Fact]
        public async Task Synthesize_LogsAFailedCallToo()
        {
            // The usage table is the only record of a provider outage; dropping failures is how
            // the previous OpenAI credit exhaustion stayed invisible.
            _handler.Respond(HttpStatusCode.TooManyRequests, "quota");

            await Create().SynthesizeAsync(Text, "alloy", "tts-1", 1.0f, "user-1");

            _usage.Verify(x => x.LogTtsAsync("azure-neural-tts", Text.Length, false, "user-1"), Times.Once);
        }

        // ── Cache lookup ──────────────────────────────────────────────────────

        [Fact]
        public void TryGetCached_HitReturnsTheStoredAudioWithoutCallingAzure()
        {
            _cache.Set(TtsCache.Key(Text, "alloy", "tts-1", 1.0f), Mp3);

            Assert.True(Create().TryGetCached(Text, "alloy", "tts-1", 1.0f, out var audio));
            Assert.Equal(Mp3, audio);
            Assert.Null(_handler.LastRequest);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private AzureSpeechTextToSpeechService Create(AzureSpeechOptions? options = null)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient(AzureSpeechTextToSpeechService.HttpClientName))
                .Returns(() => new HttpClient(_handler));

            return new AzureSpeechTextToSpeechService(
                factory.Object,
                Options.Create(options ?? new AzureSpeechOptions { Key = "test-key", Region = "japaneast" }),
                _logger.Object,
                _usage.Object,
                _cache);
        }

        private static XElement ProsodyOf(string ssml) =>
            XDocument.Parse(ssml).Descendants().Single(e => e.Name.LocalName == "prosody");

        private static string VoiceOf(string ssml) =>
            XDocument.Parse(ssml).Descendants().Single(e => e.Name.LocalName == "voice").Attribute("name")!.Value;

        private static string RateOf(string ssml) => ProsodyOf(ssml).Attribute("rate")!.Value;

        private static string TextOf(string ssml) => ProsodyOf(ssml).Value;

        /// <summary>
        /// Records the request the service actually built. Returning mp3 bytes by default keeps
        /// every test that is not about failure on the success path.
        /// </summary>
        private sealed class CapturingHandler : HttpMessageHandler
        {
            private HttpStatusCode _status = HttpStatusCode.OK;
            private byte[] _body = Mp3;
            private Exception? _throw;

            public HttpRequestMessage? LastRequest { get; private set; }
            public string? LastBody { get; private set; }

            public void Respond(HttpStatusCode status, string body)
            {
                _status = status;
                _body = System.Text.Encoding.UTF8.GetBytes(body);
            }

            public void Throw(Exception ex) => _throw = ex;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

                if (_throw != null) throw _throw;

                return new HttpResponseMessage(_status) { Content = new ByteArrayContent(_body) };
            }
        }
    }
}
