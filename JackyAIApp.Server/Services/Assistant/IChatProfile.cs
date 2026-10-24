namespace JackyAIApp.Server.Services.Assistant
{
    /// <summary>
    /// What one app's AI assistant brings to the shared chat engine: its system prompt, the
    /// background it adds, and which tools it attaches. The engine (the turn, its stream, its
    /// history, its usage) is the same for every app; only the profile differs
    /// (docs/ai-native/design/04).
    /// </summary>
    public interface IChatProfile
    {
        /// <summary>The profile's name, as the client and a conversation name it ("finance").</summary>
        string Key { get; }

        /// <summary>The feature a turn's tokens are logged under in the AI usage log.</summary>
        string UsageLogFeature { get; }

        /// <summary>
        /// What a turn starts from today. Never fails for a background that cannot be had: the turn
        /// goes ahead without it.
        /// </summary>
        Task<ChatProfileSetup> SetupAsync(DateOnly taipeiToday, CancellationToken ct);
    }

    /// <summary>What a turn of one profile is sent with, ahead of the conversation.</summary>
    /// <param name="SystemPrompt">The profile's instructions; changes at most once a day (it carries
    /// the date), so the prompt cache holds across a day's turns.</param>
    /// <param name="CachedBackground">Data the same for every reader until rebuilt, sent as a second
    /// system block with a cache breakpoint of its own; null for none.</param>
    /// <param name="AttachTwStock">Whether the tw-stock MCP server and its tools go with the turn.</param>
    /// <param name="Tools">The tools the server runs itself for this turn (<see cref="IAiTool"/>); null or
    /// empty for none, and then the turn is one request, as it always was.</param>
    public sealed record ChatProfileSetup(
        string SystemPrompt, string? CachedBackground, bool AttachTwStock, IReadOnlyList<IAiTool>? Tools = null);

    /// <summary>The registered profiles, by key.</summary>
    public interface IChatProfiles
    {
        /// <summary>The profile of that key, or null if there is none.</summary>
        IChatProfile? Find(string key);
    }

    public class ChatProfiles(IEnumerable<IChatProfile> profiles) : IChatProfiles
    {
        private readonly Dictionary<string, IChatProfile> _byKey =
            profiles.ToDictionary(p => p.Key, StringComparer.Ordinal);

        public IChatProfile? Find(string key) => _byKey.GetValueOrDefault(key);
    }
}
