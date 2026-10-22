using JackyAIApp.Server.Common;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// Strategy for generating a single vocabulary-based exam question of one type (cloze /
    /// translation / sentence) for a given word, as <typeparamref name="TQuestion"/>. Implementations
    /// either return an existing stored test (when enough are cached) or generate a new one via
    /// OpenAI and persist it.
    /// </summary>
    public interface IWordExamGenerator<TQuestion>
    {
        /// <summary>
        /// A question about <paramref name="word"/>. The word is expected to be loaded together with
        /// its related test collections.
        /// </summary>
        Task<ServiceResult<TQuestion>> GenerateAsync(SQL.Word word);
    }
}
