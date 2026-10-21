using JackyAIApp.Server.Common;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// Strategy for generating a single vocabulary-based exam (cloze / translation /
    /// sentence) for a given word. Implementations either return an existing stored
    /// test (when enough are cached) or generate a new one via OpenAI and persist it.
    /// </summary>
    public interface IWordExamGenerator
    {
        /// <summary>The exam type this generator handles.</summary>
        ExamType Type { get; }

        /// <summary>
        /// Returns an exam DTO for <paramref name="word"/>. The word is expected to be
        /// loaded together with its related test collections.
        /// The payload is <c>object</c> because each exam type has its own DTO and the caller picks
        /// the generator by <see cref="Type"/>; serialized as <c>object</c>, the response still
        /// carries the DTO's own fields.
        /// </summary>
        Task<ServiceResult<object>> GenerateAsync(SQL.Word word);
    }
}
