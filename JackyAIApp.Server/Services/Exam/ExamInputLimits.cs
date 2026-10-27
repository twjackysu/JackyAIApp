using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// How much of what a reader writes an AI-graded exam sends to the model (D-165). Each of these endpoints charges a fixed
    /// amount of the usage budget while the model bills by the token, and the request went into the prompt whole, so one
    /// grading could carry megabytes. Each is measured as the JSON the prompt carries (<see cref="AiJson.Serialize"/>), so every
    /// field counts, and refused before the model is asked: a 400, so the charge is refunded. Far above what the pages send.
    /// </summary>
    public static class ExamInputLimits
    {
        /// <summary>The request body of every endpoint here (<c>[RequestSizeLimit]</c>): Kestrel otherwise reads about 28 MB
        /// before the action can refuse.</summary>
        public const int MaxRequestBytes = 128 * 1024;

        /// <summary>A translation or a sentence with its question: a sentence or two.</summary>
        public const int MaxShortAnswerChars = 4_000;

        /// <summary>A piece of writing with its topic and words: several hundred words with room to spare.</summary>
        public const int MaxWritingChars = 20_000;

        /// <summary>A conversation's scenario, context and history: dozens of turns.</summary>
        public const int MaxConversationChars = 40_000;

        public const string TooLongMessage = "內容太長了，請縮短後再送出。";

        /// <summary>A BadRequest when <paramref name="request"/>, as the prompt would carry it, is over <paramref name="maxChars"/>.</summary>
        public static ServiceFailure? TooLong<T>(T request, int maxChars) =>
            AiJson.Serialize(request).Length > maxChars
                ? ServiceResult.Fail(ErrorCodes.BadRequest, TooLongMessage)
                : null;
    }
}
