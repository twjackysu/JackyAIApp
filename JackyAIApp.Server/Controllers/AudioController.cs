using Microsoft.AspNetCore.Mvc;
using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
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
        IUserService userService) : ControllerBase
    {
        private readonly IOpenAIService _openAIService = openAIService;
        private readonly ILogger<AudioController> _logger = logger;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IAiUsageLogger _usageLogger = usageLogger;
        private readonly IUserService _userService = userService;

        [HttpGet("normal")]
        public async Task<IActionResult> Normal([FromQuery] string text, [FromQuery] string? voice = "alloy")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return BadRequest("Text parameter is required");
            }

            var response = await _openAIService.Audio.CreateSpeech<byte[]>(new AudioCreateSpeechRequest
            {
                Model = Models.Tts_1,
                Voice = GetVoiceType(voice),
                Input = text,
                ResponseFormat = "mp3",
                Speed = 1.0f
            });

            await _usageLogger.LogTtsAsync(Models.Tts_1, text.Length, response.Successful, _userService.GetUserId());

            if (!response.Successful || response.Data == null)
            {
                _logger.LogError("OpenAI TTS failed: {Error}", response.Error?.Message);
                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to generate speech");
            }

            return File(response.Data, "audio/mpeg");
        }

        [HttpGet("slow")]
        public async Task<IActionResult> Slow([FromQuery] string text, [FromQuery] string? voice = "alloy")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return BadRequest("Text parameter is required");
            }

            var response = await _openAIService.Audio.CreateSpeech<byte[]>(new AudioCreateSpeechRequest
            {
                Model = Models.Tts_1,
                Voice = GetVoiceType(voice),
                Input = text,
                ResponseFormat = "mp3",
                Speed = 0.5f
            });

            await _usageLogger.LogTtsAsync(Models.Tts_1, text.Length, response.Successful, _userService.GetUserId());

            if (!response.Successful || response.Data == null)
            {
                _logger.LogError("OpenAI TTS failed: {Error}", response.Error?.Message);
                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to generate speech");
            }

            return File(response.Data, "audio/mpeg");
        }

        [HttpGet("hd")]
        public async Task<IActionResult> HighDefinition([FromQuery] string text, [FromQuery] string? voice = "alloy")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return BadRequest("Text parameter is required");
            }

            var response = await _openAIService.Audio.CreateSpeech<byte[]>(new AudioCreateSpeechRequest
            {
                Model = Models.Tts_1_hd,
                Voice = GetVoiceType(voice),
                Input = text,
                ResponseFormat = "mp3",
                Speed = 1.0f
            });

            await _usageLogger.LogTtsAsync(Models.Tts_1_hd, text.Length, response.Successful, _userService.GetUserId());

            if (!response.Successful || response.Data == null)
            {
                _logger.LogError("OpenAI TTS HD failed: {Error}", response.Error?.Message);
                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to generate high-definition speech");
            }

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
