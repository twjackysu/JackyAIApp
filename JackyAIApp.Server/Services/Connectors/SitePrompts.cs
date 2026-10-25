using System.Text;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>One value a <see cref="SitePrompt"/> asks for (a word, a dashboard's name), as MCP's <c>prompts/list</c> describes it.</summary>
    /// <param name="Name">The argument's key: lowercase letters, digits and underscores.</param>
    /// <param name="Description">What to type, in the reader's words; the other client shows it next to the box.</param>
    public sealed record SitePromptArgument(string Name, string Description, bool Required = false);

    /// <summary>
    /// A ready-made request another AI's reader can pick from the site's MCP server (MCP's prompts, design/03 §9.3):
    /// Claude Code shows each as a slash command (<c>/mcp__jacky-studio__daily_briefing</c>), Claude Desktop and VS Code
    /// in their prompt menus. It is the site's own wording of what its tools do best together — the morning briefing,
    /// a review quiz asked one question at a time — so another client's model uses them as Studio AI would.
    /// <para>
    /// A feature declares its prompts next to its tools (a <c>public static readonly SitePrompt</c>) and registers each
    /// (<c>services.AddSingleton(prompt)</c>). A prompt is offered only when the token may use every tool it names
    /// (<see cref="Tools"/>): one asking for a tool the reader turned off, or a write a read-only token cannot make,
    /// would only fail. <c>SitePromptDefinitionsTests</c> holds every prompt to the same shape.
    /// </para>
    /// </summary>
    /// <param name="Name">The prompt's key: lowercase letters, digits and underscores, unique across the site.</param>
    /// <param name="Title">What the reader sees in the menu.</param>
    /// <param name="Description">One line on what it asks for.</param>
    /// <param name="Tools">The site tools its text asks for, by name: all must be offered for the prompt to be.</param>
    /// <param name="Text">The message the prompt becomes, from the arguments as <see cref="SitePrompts.Clean"/> left them (a missing optional one is absent).</param>
    public sealed record SitePrompt(
        string Name,
        string Title,
        string Description,
        IReadOnlyList<string> Tools,
        IReadOnlyList<SitePromptArgument> Arguments,
        Func<IReadOnlyDictionary<string, string>, string> Text);

    public static class SitePrompts
    {
        /// <summary>The most of one argument kept: a word or a dashboard's name, never a document.</summary>
        public const int MaxArgumentChars = 100;

        /// <summary>
        /// An argument as it goes into the prompt's text: on one line (a control character, a line break included, becomes
        /// a space), trimmed, at most <see cref="MaxArgumentChars"/>. Null when nothing is left. The reader typed it into
        /// their own client, so it is their request; one line keeps it from passing for more of the prompt's own wording.
        /// </summary>
        public static string? Clean(string? value)
        {
            if (value == null) return null;
            var text = new StringBuilder(Math.Min(value.Length, MaxArgumentChars * 2));
            foreach (var c in value) text.Append(char.IsControl(c) ? ' ' : c);
            var cleaned = text.ToString().Trim();
            if (cleaned.Length > MaxArgumentChars)
            {
                // Never cut a surrogate pair in half.
                var cut = char.IsHighSurrogate(cleaned[MaxArgumentChars - 1]) ? MaxArgumentChars - 1 : MaxArgumentChars;
                cleaned = cleaned[..cut].TrimEnd();
            }
            return cleaned.Length == 0 ? null : cleaned;
        }
    }
}
