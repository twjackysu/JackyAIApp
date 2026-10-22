using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Dictionary
{
    /// <summary>A dictionary word that has a public page, and when it last changed.</summary>
    public sealed record PublicWord(string WordText, DateTime LastUpdated);

    /// <summary>
    /// The dictionary words that have a public page — every word not marked invalid — for the
    /// sitemap (<c>SitemapController</c>).
    /// </summary>
    public interface IPublicWordList
    {
        Task<List<PublicWord>> ListAsync(CancellationToken ct);
    }

    public class PublicWordList(AzureSQLDBContext dbContext) : IPublicWordList
    {
        // Cheap by design: a single projection over the indexed WordText column, no per-word
        // AI calls.
        public Task<List<PublicWord>> ListAsync(CancellationToken ct) =>
            dbContext.Words
                .Where(w => !w.DataInvalid.HasValue || !w.DataInvalid.Value)
                .Select(w => new PublicWord(w.WordText, w.LastUpdated))
                .ToListAsync(ct);
    }
}
