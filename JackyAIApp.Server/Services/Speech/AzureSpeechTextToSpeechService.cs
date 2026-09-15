using System.Globalization;
using System.Net.Http.Headers;
using System.Security;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Services.AiUsage;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services.Speech
{
    /// <summary>
    /// Text-to-speech backed by Azure AI Speech instead of OpenAI.
    ///
    /// Why it exists: OpenAI TTS is billed per character against the same credit balance as every
    /// other OpenAI call, so an exhausted balance silences the whole app — dictionary playback,
    /// the Listening exam, conversation replies. Azure's F0 tier gives 500,000 characters a month
    /// free, against measured usage of roughly 2,000 characters a month.
    ///
    /// Why REST and not the Speech SDK: the SDK (Microsoft.CognitiveServices.Speech) ships native
    /// per-platform binaries, while synthesis here is a single POST that returns mp3 bytes —
    /// exactly what <c>File(bytes, "audio/mpeg")</c> wants. Nothing else in the SDK is used.
    ///
    /// Callers are unchanged: this takes the same OpenAI-flavoured (voice, model, speed) triple as
    /// <c>OpenAI.TextToSpeechService</c>, maps it onto SSML, and shares <see cref="TtsCache"/>.
    /// </summary>
    public class AzureSpeechTextToSpeechService(
        IHttpClientFactory httpClientFactory,
        IOptions<AzureSpeechOptions> options,
        ILogger<AzureSpeechTextToSpeechService> logger,
        IAiUsageLogger usageLogger,
        IExtendedMemoryCache memoryCache) : ITextToSpeechService
    {
        public const string HttpClientName = "AzureSpeech";

        /// <summary>
        /// Recorded in <c>AiUsageLog.Model</c> instead of the caller's OpenAI model id. The caller
        /// still passes "tts-1" (it is part of the cache key and predates this provider), but
        /// logging that would price free F0 synthesis at OpenAI's $15/1M characters on the usage
        /// dashboard. <see cref="Common.AiRates"/> maps this id to zero.
        /// </summary>
        private const string UsageModelId = "azure-neural-tts";

        // 24 kHz mono mp3: the smallest Azure format that is still mp3 (the browser <audio>
        // element plays it directly, and the frontend sends no Accept negotiation).
        private const string OutputFormat = "audio-24khz-48kbitrate-mono-mp3";

        /// <summary>
        /// OpenAI voice name -> Azure voice name. Azure publishes deliberate counterparts to
        /// OpenAI's six voices, so the mapping is one-to-one and the audio stays recognisably the
        /// same to a user; the public <c>?voice=</c> API and the cache keys keep the OpenAI names.
        /// All six verified present and synthesizing in japaneast.
        /// </summary>
        private static readonly Dictionary<string, string> VoiceMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["alloy"] = "en-US-AlloyTurboMultilingualNeural",
            ["echo"] = "en-US-EchoTurboMultilingualNeural",
            ["fable"] = "en-US-FableTurboMultilingualNeural",
            ["onyx"] = "en-US-OnyxTurboMultilingualNeural",
            ["nova"] = "en-US-NovaTurboMultilingualNeural",
            ["shimmer"] = "en-US-ShimmerTurboMultilingualNeural",
        };

        private const string DefaultAzureVoice = "en-US-AlloyTurboMultilingualNeural";

        public bool TryGetCached(string text, string voice, string model, float speed, out byte[]? audio) =>
            TtsCache.TryGet(memoryCache, text, voice, model, speed, out audio);

        public async Task<TtsResult> SynthesizeAsync(string text, string voice, string model, float speed, string? userId)
        {
            var region = options.Value.Region;
            var key = options.Value.Key;
            if (string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(key))
            {
                // Unreachable through DI (this implementation is only registered when both are
                // set), but a misconfigured Key Vault should fail loudly rather than as a 401.
                logger.LogError("Azure Speech is not configured (Region or Key missing).");
                return new TtsResult(false, null);
            }

            byte[]? audio = null;
            var success = false;
            try
            {
                var client = httpClientFactory.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1");
                request.Headers.Add("Ocp-Apim-Subscription-Key", key);
                request.Headers.Add("X-Microsoft-OutputFormat", OutputFormat);
                request.Content = new StringContent(BuildSsml(text, voice, speed));
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/ssml+xml")
                {
                    CharSet = "utf-8"
                };

                using var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    audio = await response.Content.ReadAsByteArrayAsync();
                    // A 200 with an empty body would otherwise reach the browser as a silent clip
                    // that looks like a working feature.
                    success = audio.Length > 0;
                    if (!success)
                    {
                        logger.LogError("Azure Speech returned 200 with an empty body for voice {Voice}.", voice);
                    }
                }
                else
                {
                    // Azure puts the actual reason (bad voice name, malformed SSML, quota) in the
                    // body; the status alone does not distinguish them.
                    var body = await response.Content.ReadAsStringAsync();
                    logger.LogError(
                        "Azure Speech TTS failed: {Status} {Reason}. Voice={Voice} Body={Body}",
                        (int)response.StatusCode, response.ReasonPhrase, voice, body);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Azure Speech TTS call threw for voice {Voice}.", voice);
            }

            await usageLogger.LogTtsAsync(UsageModelId, text.Length, success, userId);

            if (!success || audio == null)
            {
                return new TtsResult(false, null);
            }

            TtsCache.Set(memoryCache, text, voice, model, speed, audio);
            return new TtsResult(true, audio);
        }

        /// <summary>
        /// Wraps the text in the SSML Azure's REST endpoint expects. <paramref name="text"/> is
        /// caller-supplied (AudioController's <c>?text=</c> is anonymous), so it is XML-escaped —
        /// an unescaped "&amp;" or "&lt;" would otherwise turn a valid request into a 400.
        /// </summary>
        internal static string BuildSsml(string text, string voice, float speed) =>
            $"<speak version='1.0' xml:lang='en-US'><voice name='{AzureVoiceFor(voice)}'>" +
            $"<prosody rate='{RateFor(speed)}'>{SecurityElement.Escape(text)}</prosody></voice></speak>";

        /// <summary>
        /// Azure expresses speed as a percentage delta from the voice's natural rate, where OpenAI
        /// uses a multiplier: 1.0 -> "0%", 0.5 -> "-50%".
        /// </summary>
        internal static string RateFor(float speed)
        {
            var percent = Math.Round((speed - 1f) * 100f);
            return percent == 0
                ? "0%"
                : percent.ToString("+0;-0", CultureInfo.InvariantCulture) + "%";
        }

        internal static string AzureVoiceFor(string voice)
        {
            if (VoiceMap.TryGetValue(voice, out var mapped)) return mapped;
            // Already an Azure voice name (locale-prefixed) — pass it through so a future caller
            // can ask for a voice that has no OpenAI counterpart without changing this map.
            if (voice.StartsWith("en-", StringComparison.OrdinalIgnoreCase)) return voice;
            return DefaultAzureVoice;
        }
    }
}
