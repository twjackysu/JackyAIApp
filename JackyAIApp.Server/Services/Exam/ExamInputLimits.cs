using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;

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

        /// <summary>
        /// Drops a conversation's oldest turns from <paramref name="history"/> (part of <paramref name="request"/>) until the
        /// request fits <paramref name="maxChars"/>, as a long chat forgets its start: the page sends the whole history every
        /// turn and has no end, so a long practice keeps going rather than being refused. Each turn's own length is measured once.
        /// What is left can still be too long (one huge message): check it with <see cref="TooLong{T}"/> after.
        /// </summary>
        public static void TrimOldestTurns<T>(T request, List<ConversationTurn>? history, int maxChars)
        {
            if (history is null) return;
            var excess = AiJson.Serialize(request).Length - maxChars;
            var drop = 0;
            while (excess > 0 && drop < history.Count)
            {
                // The turn and the comma that separated it from the next.
                excess -= AiJson.Serialize(history[drop]).Length + 1;
                drop++;
            }
            history.RemoveRange(0, drop);
        }

        /// <summary>A BadRequest when <paramref name="request"/>, as the prompt would carry it, is over <paramref name="maxChars"/>.</summary>
        public static ServiceFailure? TooLong<T>(T request, int maxChars) =>
            AiJson.Serialize(request).Length > maxChars
                ? ServiceResult.Fail(ErrorCodes.BadRequest, TooLongMessage)
                : null;
    }
}
