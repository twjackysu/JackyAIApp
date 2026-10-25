using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Assistant.Memory
{
    /// <summary>What came of keeping a note (<see cref="IAssistantMemoryService.AddAsync"/>).</summary>
    public enum MemoryAddOutcome
    {
        /// <summary>Kept as a new memory.</summary>
        Added,

        /// <summary>The same words were kept already: nothing new.</summary>
        AlreadyKept,

        /// <summary>No words, once trimmed.</summary>
        Empty,

        /// <summary>Longer than <see cref="AssistantMemoryService.MaxLength"/>.</summary>
        TooLong,

        /// <summary>The reader keeps <see cref="AssistantMemoryService.Limit"/> already.</summary>
        Full,
    }

    /// <summary>
    /// What came of keeping a note: the memory (the new one, or the one with the same words), and how many the reader
    /// keeps now.
    /// </summary>
    public sealed record MemoryAddResult(MemoryAddOutcome Outcome, AssistantMemoryDto? Memory, int Count);

    /// <summary>
    /// What a reader asked Studio AI to remember about them (docs/ai-native/design/08): short notes in their own words,
    /// all of which go into Studio AI's prompt. Every method names the reader, since a chat tool runs for the
    /// conversation's reader rather than for whoever a request's cookie names.
    /// </summary>
    public interface IAssistantMemoryService
    {
        /// <summary>The reader's memories, newest first.</summary>
        Task<IReadOnlyList<AssistantMemoryDto>> ListAsync(string userId, CancellationToken ct);

        /// <summary>Keeps <paramref name="text"/> (trimmed, on one line) as a memory of the reader's.</summary>
        Task<MemoryAddResult> AddAsync(string userId, string text, CancellationToken ct);

        /// <summary>Forgets one of the reader's memories: the one forgotten, or null for none of theirs by that id.</summary>
        Task<AssistantMemoryDto?> RemoveAsync(string userId, string id, CancellationToken ct);

        /// <summary>
        /// Forgets the reader's memory with these words (as kept: one line, any case), which is how Studio AI names one:
        /// its confirmation card then shows exactly what goes. The one forgotten, or null for none with those words.
        /// </summary>
        Task<AssistantMemoryDto?> RemoveTextAsync(string userId, string text, CancellationToken ct);
    }

    public sealed class AssistantMemoryService(AzureSQLDBContext db, TimeProvider time) : IAssistantMemoryService
    {
        /// <summary>The most memories one reader keeps: all of them go into every Studio AI prompt.</summary>
        public const int Limit = 30;

        /// <summary>The longest memory, in characters (the column's size).</summary>
        public const int MaxLength = 200;

        public async Task<IReadOnlyList<AssistantMemoryDto>> ListAsync(string userId, CancellationToken ct)
        {
            var rows = await db.AssistantMemories.AsNoTracking()
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync(ct);
            return rows.Select(Dto).ToList();
        }

        public async Task<MemoryAddResult> AddAsync(string userId, string text, CancellationToken ct)
        {
            var note = Normalize(text);
            if (note.Length == 0) return new MemoryAddResult(MemoryAddOutcome.Empty, null, 0);
            if (note.Length > MaxLength) return new MemoryAddResult(MemoryAddOutcome.TooLong, null, 0);

            var kept = await db.AssistantMemories.Where(m => m.UserId == userId).ToListAsync(ct);
            var same = kept.FirstOrDefault(m => string.Equals(m.Text, note, StringComparison.OrdinalIgnoreCase));
            if (same != null) return new MemoryAddResult(MemoryAddOutcome.AlreadyKept, Dto(same), kept.Count);
            if (kept.Count >= Limit) return new MemoryAddResult(MemoryAddOutcome.Full, null, kept.Count);

            var row = new SQL.AssistantMemory { UserId = userId, Text = note, CreatedAt = time.GetUtcNow().UtcDateTime };
            db.AssistantMemories.Add(row);
            await db.SaveChangesAsync(ct);
            return new MemoryAddResult(MemoryAddOutcome.Added, Dto(row), kept.Count + 1);
        }

        public async Task<AssistantMemoryDto?> RemoveAsync(string userId, string id, CancellationToken ct)
        {
            var row = await db.AssistantMemories.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId, ct);
            if (row == null) return null;
            db.AssistantMemories.Remove(row);
            await db.SaveChangesAsync(ct);
            return Dto(row);
        }

        public async Task<AssistantMemoryDto?> RemoveTextAsync(string userId, string text, CancellationToken ct)
        {
            var note = Normalize(text);
            if (note.Length == 0) return null;
            var kept = await db.AssistantMemories.Where(m => m.UserId == userId).ToListAsync(ct);
            var row = kept.FirstOrDefault(m => string.Equals(m.Text, note, StringComparison.OrdinalIgnoreCase));
            if (row == null) return null;
            db.AssistantMemories.Remove(row);
            await db.SaveChangesAsync(ct);
            return Dto(row);
        }

        /// <summary>
        /// A note as it is kept: trimmed, its line breaks and runs of white space made one space, so each memory is one
        /// line of the prompt's list.
        /// </summary>
        internal static string Normalize(string? text) =>
            string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        private static AssistantMemoryDto Dto(SQL.AssistantMemory m) => new(m.Id, m.Text, m.CreatedAt);
    }
}
