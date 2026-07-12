using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Services.AiUsage;

namespace JackyAIApp.Server.Services.OpenAI
{
    public class TextToSpeechService(
        IOpenAIService openAIService,
        ILogger<TextToSpeechService> logger,
        IAiUsageLogger usageLogger,
        IExtendedMemoryCache memoryCache) : ITextToSpeechService
    {
        // Same text always produces the same audio for a given (model, voice, speed) — see
        // AudioController for the full cache-lifetime rationale. Shared here so every caller
        // (billed dictionary/chat playback, unbilled Listening-exam playback) hits one cache.
        private static readonly TimeSpan TtsCacheTtl = TimeSpan.FromDays(30);

        private static string CacheKey(string text, string voice, string model, float speed) =>
            $"tts_{model}_{voice}_{speed}_{text}";

        public bool TryGetCached(string text, string voice, string model, float speed, out byte[]? audio)
        {
            if (memoryCache.TryGetValue(CacheKey(text, voice, model, speed), out object? cached) && cached is byte[] cachedAudio)
            {
                audio = cachedAudio;
                return true;
            }
            audio = null;
            return false;
        }

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

            memoryCache.Set(CacheKey(text, voice, model, speed), response.Data, TtsCacheTtl);
            return new TtsResult(true, response.Data);
        }
    }
}
