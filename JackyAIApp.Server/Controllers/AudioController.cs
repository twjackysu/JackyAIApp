using Microsoft.AspNetCore.Mvc;
using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AudioController : ControllerBase
    {
        private readonly IOpenAIService _openAIService;
        private readonly ILogger<AudioController> _logger;
        private readonly IMyResponseFactory _responseFactory;

        public AudioController(
            IOpenAIService openAIService,
            ILogger<AudioController> logger,
            IMyResponseFactory responseFactory)
        {
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        }

        [HttpGet("normal")]
        public async Task<IActionResult> Normal([FromQuery] string text, [FromQuery] string? voice = "alloy")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return BadRequest("Text parameter is required");
            }

            try
            {
                var response = await _openAIService.Audio.CreateSpeech<byte[]>(new AudioCreateSpeechRequest
                {
                    Model = Models.Tts_1,
                    Voice = GetVoiceType(voice),
                    Input = text,
                    ResponseFormat = "mp3",
                    Speed = 1.0f
                });

                if (!response.Successful)
                {
                    _logger.LogError("OpenAI TTS failed: {error}", response.Error?.Message);
                    return StatusCode(500, "Failed to generate speech");
                }

                return File(response.Data, "audio/mpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate TTS audio for text: {text}", text);
                return StatusCode(500, "Internal Server Error");
            }
        }

        [HttpGet("slow")]
        public async Task<IActionResult> Slow([FromQuery] string text, [FromQuery] string? voice = "alloy")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return BadRequest("Text parameter is required");
            }

            try
            {
                var response = await _openAIService.Audio.CreateSpeech<byte[]>(new AudioCreateSpeechRequest
                {
                    Model = Models.Tts_1,
                    Voice = GetVoiceType(voice),
                    Input = text,
                    ResponseFormat = "mp3",
                    Speed = 0.5f // Slower speed for "slow" endpoint
                });

                if (!response.Successful)
                {
                    _logger.LogError("OpenAI TTS failed: {error}", response.Error?.Message);
                    return StatusCode(500, "Failed to generate speech");
                }

                return File(response.Data, "audio/mpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate slow TTS audio for text: {text}", text);
                return StatusCode(500, "Internal Server Error");
            }
        }

        [HttpGet("hd")]
        public async Task<IActionResult> HighDefinition([FromQuery] string text, [FromQuery] string? voice = "alloy")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return BadRequest("Text parameter is required");
            }

            try
            {
                var response = await _openAIService.Audio.CreateSpeech<byte[]>(new AudioCreateSpeechRequest
                {
                    Model = Models.Tts_1_hd, // High-definition model
                    Voice = GetVoiceType(voice),
                    Input = text,
                    ResponseFormat = "mp3",
                    Speed = 1.0f
                });

                if (!response.Successful)
                {
                    _logger.LogError("OpenAI TTS HD failed: {error}", response.Error?.Message);
                    return StatusCode(500, "Failed to generate high-definition speech");
                }

                return File(response.Data, "audio/mpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate HD TTS audio for text: {text}", text);
                return StatusCode(500, "Internal Server Error");
            }
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
                _ => "alloy" // Default voice
            };
        }
    }
}
