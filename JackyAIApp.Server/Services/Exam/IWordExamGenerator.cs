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
        /// </summary>
        Task<ExamResult> GenerateAsync(SQL.Word word);
    }
}
