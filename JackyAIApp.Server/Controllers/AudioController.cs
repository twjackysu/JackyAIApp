using Microsoft.AspNetCore.Mvc;
using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.AiUsage;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AudioController(
        IOpenAIService openAIService,
        ILogger<AudioController> logger,
        IMyResponseFactory responseFactory,
        IAiUsageLogger usageLogger,
        IUserService userService,
        ICreditService creditService,
        IExtendedMemoryCache memoryCache) : ControllerBase
    {
        private readonly IOpenAIService _openAIService = openAIService;
        private readonly ILogger<AudioController> _logger = logger;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IAiUsageLogger _usageLogger = usageLogger;
        private readonly IUserService _userService = userService;
        private readonly ICreditService _creditService = creditService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;

        // Same text always produces the same audio for a given (model, voice, speed) — TTS output
        // is deterministic. The hottest inputs (dictionary example sentences, daily-word audio) are
        // re-played by every user who looks the word up, so an unbounded number of distinct
        // listeners can otherwise each trigger a fresh, separately-billed OpenAI call for content
        // that never changes. Cached indefinitely-ish (30 days) since there's nothing to invalidate.
        private static readonly TimeSpan TtsCacheTtl = TimeSpan.FromDays(30);

        // TTS is billed per input character — without a cap this endpoint (anonymous, no other
        // gate) could be used to run up an unbounded bill via a single very long `text` value.
        // Comfortably covers a paragraph; far above any real dictionary sentence or chat message.
        private const int MaxTextLength = 1000;

        [HttpGet("normal")]
        public Task<IActionResult> Normal([FromQuery] string text, [FromQuery] string? voice = "alloy") =>
            SynthesizeAsync(text, voice, Models.Tts_1, speed: 1.0f);

        [HttpGet("slow")]
        public Task<IActionResult> Slow([FromQuery] string text, [FromQuery] string? voice = "alloy") =>
            SynthesizeAsync(text, voice, Models.Tts_1, speed: 0.5f);

        private async Task<IActionResult> SynthesizeAsync(string text, string? voice, string model, float speed)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return BadRequest("Text parameter is required");
            }
            if (text.Length > MaxTextLength)
            {
                return BadRequest($"Text parameter exceeds the {MaxTextLength}-character limit");
            }

            var resolvedVoice = GetVoiceType(voice);
            var cacheKey = $"tts_{model}_{resolvedVoice}_{speed}_{text}";

            if (_memoryCache.TryGetValue(cacheKey, out object? cached) && cached is byte[] cachedAudio)
            {
                return File(cachedAudio, "audio/mpeg");
            }

            // Only charge on an actual cache miss, and only for logged-in users — anonymous
            // dictionary pronunciation stays free (length cap + cache are the anti-abuse gate
            // there), matching the pattern used for the NL-summarize and market-digest endpoints.
            var userId = _userService.GetUserId();
            if (!string.IsNullOrEmpty(userId) &&
                !await _creditService.ConsumeCreditsAsync(userId, CreditCosts.TextToSpeech, "tts", "Text-to-speech synthesis"))
            {
                return _responseFactory.CreateErrorResponse(
                    ErrorCodes.Forbidden, $"Insufficient credits. Required: {CreditCosts.TextToSpeech}.");
            }

            var response = await _openAIService.Audio.CreateSpeech<byte[]>(new AudioCreateSpeechRequest
            {
                Model = model,
                Voice = resolvedVoice,
                Input = text,
                ResponseFormat = "mp3",
                Speed = speed
            });

            await _usageLogger.LogTtsAsync(model, text.Length, response.Successful, userId);

            if (!response.Successful || response.Data == null)
            {
                _logger.LogError("OpenAI TTS failed: {Error}", response.Error?.Message);
                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to generate speech");
            }

            _memoryCache.Set(cacheKey, response.Data, TtsCacheTtl);
            return File(response.Data, "audio/mpeg");
        }

        private static string GetVoiceType(string? voice)
        {
            return voice?.ToLower() switch
            {
                "alloy" => "alloy",
                "echo" => "echo",
                "fable" => "fable",
                "onyx" => "onyx",
                "nova" => "nova",
                "shimmer" => "shimmer",
                _ => "alloy"
            };
        }
    }
}
