using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.SpellChecker;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Dictionary
{
    /// <summary>A dictionary word that has a public page, and when it last changed.</summary>
    public sealed record PublicWord(string WordText, DateTime LastUpdated);

    /// <summary>
    /// The dictionary words that have a public page — every word not marked invalid, and not a regular
    /// inflection (a row stored for "establishing" before inflections were shown on their root's entry:
    /// that page is "establish"'s now) — for the sitemap (<c>SitemapController</c>).
    /// </summary>
    public interface IPublicWordList
    {
        Task<List<PublicWord>> ListAsync(CancellationToken ct);
    }

    public class PublicWordList(AzureSQLDBContext dbContext, ISpellChecker spellChecker) : IPublicWordList
    {
        // Cheap by design: a single projection over the indexed WordText column, no per-word
        // AI calls.
        public async Task<List<PublicWord>> ListAsync(CancellationToken ct)
        {
            var words = await dbContext.Words
                .Where(w => !w.DataInvalid.HasValue || !w.DataInvalid.Value)
                .Select(w => new PublicWord(w.WordText, w.LastUpdated))
                .ToListAsync(ct);
            return words.Where(w => spellChecker.RootOf(w.WordText) == null).ToList();
        }
    }
}
