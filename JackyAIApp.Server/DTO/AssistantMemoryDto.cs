namespace JackyAIApp.Server.DTO
{
    /// <summary>
    /// One thing the reader asked Studio AI to remember (docs/ai-native/design/08): its id (what forgetting it names), the
    /// note in their words, and when it was kept (UTC).
    /// </summary>
    public sealed record AssistantMemoryDto(string Id, string Text, DateTime CreatedAt);

    /// <summary>The reader's memories, newest first, and how many they may keep.</summary>
    public sealed record AssistantMemoryListDto(IReadOnlyList<AssistantMemoryDto> Memories, int Limit);
}
