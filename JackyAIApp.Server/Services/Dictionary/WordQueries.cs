using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Dictionary
{
    internal static class WordQueries
    {
        /// <summary>A stored word with everything its page shows, or null.</summary>
        public static Task<SQL.Word?> LoadAsync(AzureSQLDBContext db, string lowerWord, CancellationToken ct) =>
            db.Words
                .Where(x => x.WordText == lowerWord)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Definitions)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.ExampleSentences)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Tags)
                .SingleOrDefaultAsync(ct);
    }
}
