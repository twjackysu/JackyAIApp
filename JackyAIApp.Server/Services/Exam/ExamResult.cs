using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>The three vocabulary-based exam types that share a generate/cache flow.</summary>
    public enum ExamType
    {
        Cloze,
        Translation,
        Sentence
    }

    /// <summary>
    /// Result of an exam operation: a payload DTO on success, or an error code + message.
    /// </summary>
    public sealed record ExamResult(object? Data, ErrorCodes? Error, string? ErrorMessage)
    {
        public bool IsSuccess => Error is null;

        public static ExamResult Ok(object data) => new(data, null, null);

        public static ExamResult Fail(ErrorCodes error, string message) => new(null, error, message);
    }
}
