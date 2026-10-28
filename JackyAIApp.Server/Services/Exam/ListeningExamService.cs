using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// TOEIC Part 1 style "look at the picture, pick the best description" listening questions,
    /// served from the pre-seeded bank (Tools/ListeningBankImporter, or the admin generator). Not
    /// word-scoped, so it doesn't go through <see cref="IWordExamGenerator{TQuestion}"/>.
    /// </summary>
    public interface IListeningExamService
    {
        /// <summary>A random question from the bank, preferring ones the user has not seen
        /// recently (<see cref="IExamBankPicker"/>).</summary>
        Task<ServiceResult<ListeningTestResponse>> GenerateAsync();

        /// <summary>Looks up a listening question option's spoken text, for the unbilled audio
        /// playback endpoint (usage was already charged when the question itself was fetched).</summary>
        Task<ServiceResult<string>> GetOptionTextAsync(string optionId);

        /// <summary>Admin-only: paginated view of the full listening question bank.</summary>
        Task<ServiceResult<ListeningBankPageResponse>> GetBankAsync(int pageNumber, int pageSize);
    }

    public class ListeningExamService(
        AzureSQLDBContext dbContext,
        IExamBankPicker bankPicker) : IListeningExamService
    {
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IExamBankPicker _bankPicker = bankPicker;

        /// <summary>The most questions one page of the admin's bank view holds.</summary>
        public const int MaxBankPageSize = 100;

        public async Task<ServiceResult<ListeningTestResponse>> GenerateAsync()
        {
            var pool = await _bankPicker.OpenAsync(
                _dbContext.ListeningTests, q => q.Include(lt => lt.Options), a => a.ListeningTestId);
            if (pool.TotalCount == 0)
            {
                return ServiceResult.Fail(ErrorCodes.NotFound,
                    "No listening questions are available yet.");
            }

            var test = await pool.PickAsync();

            return ServiceResult.Ok(new ListeningTestResponse
            {
                Id = test.Id,
                ImageUrl = test.ImageUrl,
                Options = test.Options.Select(o => new ListeningTestOptionDto
                {
                    Id = o.Id,
                    Text = o.Text,
                    IsCorrect = o.IsCorrect,
                    Explanation = o.Explanation,
                }).ToList(),
            });
        }

        public async Task<ServiceResult<string>> GetOptionTextAsync(string optionId)
        {
            var option = await _dbContext.ListeningTestOptions
                .SingleOrDefaultAsync(o => o.Id == optionId);

            return option == null
                ? ServiceResult.Fail(ErrorCodes.NotFound, "Listening question option not found.")
                : ServiceResult.Ok(option.Text);
        }

        public async Task<ServiceResult<ListeningBankPageResponse>> GetBankAsync(int pageNumber, int pageSize)
        {
            // A page before the first would be a negative Skip (EF throws: a 500), and an unbounded size the whole bank.
            pageNumber = Math.Max(pageNumber, 1);
            pageSize = Math.Clamp(pageSize, 1, MaxBankPageSize);
            var totalCount = await _dbContext.ListeningTests.CountAsync();
            var items = await _dbContext.ListeningTests
                .Include(lt => lt.Options)
                .OrderByDescending(lt => lt.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return ServiceResult.Ok(new ListeningBankPageResponse
            {
                TotalCount = totalCount,
                Items = items.Select(t => new ListeningTestResponse
                {
                    Id = t.Id,
                    ImageUrl = t.ImageUrl,
                    Options = t.Options.Select(o => new ListeningTestOptionDto
                    {
                        Id = o.Id,
                        Text = o.Text,
                        IsCorrect = o.IsCorrect,
                        Explanation = o.Explanation,
                    }).ToList(),
                }).ToList(),
            });
        }
    }
}
