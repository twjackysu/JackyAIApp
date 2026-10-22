using Betalgo.Ranul.OpenAI.ObjectModels;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Speech;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// The spoken audio of an exam's text — a listening option or prompt, a dictation word — in the
    /// exams' one voice: from the TTS cache when it is there, synthesized otherwise. Unbilled: the
    /// exam's own flat charge covers the clips the user must listen to just to answer.
    /// </summary>
    public interface IExamSpeech
    {
        Task<ServiceResult<byte[]>> SpeakAsync(string text);
    }

    public class ExamSpeech(ITextToSpeechService ttsService, ICurrentUser currentUser) : IExamSpeech
    {
        private const string Voice = "alloy";
        private const float Speed = 1.0f;
        private static readonly string Model = Models.Tts_1;

        public async Task<ServiceResult<byte[]>> SpeakAsync(string text)
        {
            if (ttsService.TryGetCached(text, Voice, Model, Speed, out var cachedAudio) && cachedAudio != null)
            {
                return ServiceResult.Ok(cachedAudio);
            }

            var result = await ttsService.SynthesizeAsync(text, Voice, Model, Speed, currentUser.Id);
            if (!result.Success || result.Audio == null)
            {
                return ServiceResult.Fail(ErrorCodes.ExternalApiError, "Failed to generate speech");
            }

            return ServiceResult.Ok(result.Audio);
        }
    }
}
