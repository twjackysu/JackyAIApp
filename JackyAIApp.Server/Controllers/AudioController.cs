using Microsoft.AspNetCore.Mvc;
using Betalgo.Ranul.OpenAI.ObjectModels;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.OpenAI;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AudioController(
        ITextToSpeechService ttsService,
        ILogger<AudioController> logger,
        IMyResponseFactory responseFactory,
        IUserService userService,
        ICreditService creditService) : ControllerBase
    {
        private readonly ITextToSpeechService _ttsService = ttsService;
        private readonly ILogger<AudioController> _logger = logger;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IUserService _userService = userService;
        private readonly ICreditService _creditService = creditService;

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

            if (_ttsService.TryGetCached(text, resolvedVoice, model, speed, out var cachedAudio) && cachedAudio != null)
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

            var result = await _ttsService.SynthesizeAsync(text, resolvedVoice, model, speed, userId);

            if (!result.Success || result.Audio == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to generate speech");
            }

            return File(result.Audio, "audio/mpeg");
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
