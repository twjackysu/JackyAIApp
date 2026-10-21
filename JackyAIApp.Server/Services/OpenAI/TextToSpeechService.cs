using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using JackyAIApp.Server.Services.AiUsage;
using JackyAIApp.Server.Services.Speech;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.OpenAI
{
    /// <summary>
    /// OpenAI-backed TTS. Registered only when no Azure Speech key is configured — see
    /// <see cref="AzureSpeechTextToSpeechService"/> for why that is now the preferred provider.
    /// </summary>
    public class TextToSpeechService(
        IOpenAIService openAIService,
        ILogger<TextToSpeechService> logger,
        IAiUsageLogger usageLogger,
        IMemoryCache memoryCache) : ITextToSpeechService
    {
        public bool TryGetCached(string text, string voice, string model, float speed, out byte[]? audio) =>
            TtsCache.TryGet(memoryCache, text, voice, model, speed, out audio);

        public async Task<TtsResult> SynthesizeAsync(string text, string voice, string model, float speed, string? userId)
        {
            var response = await openAIService.Audio.CreateSpeech<byte[]>(new AudioCreateSpeechRequest
            {
                Model = model,
                Voice = voice,
                Input = text,
                ResponseFormat = "mp3",
                Speed = speed
            });

            await usageLogger.LogTtsAsync(model, text.Length, response.Successful, userId);

            if (!response.Successful || response.Data == null)
            {
                logger.LogError("OpenAI TTS failed: {Error}", response.Error?.Message);
                return new TtsResult(false, null);
            }

            TtsCache.Set(memoryCache, text, voice, model, speed, response.Data);
            return new TtsResult(true, response.Data);
        }
    }
}
