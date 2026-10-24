using System.Text.Json;

namespace JackyAIApp.Server.Services.Assistant
{
    /// <summary>What calling a tool does, which decides whether the reader is asked first (docs/ai-native/design/03).</summary>
    public enum ToolEffect
    {
        /// <summary>Only reads; runs without asking.</summary>
        Read,

        /// <summary>Adds or changes something of the reader's; asked first.</summary>
        Write,

        /// <summary>Removes or overwrites something that cannot be had back; always asked first.</summary>
        Destructive,
    }

    /// <summary>A tool as the model and the reader see it.</summary>
    /// <param name="Name">The name the model calls it by: letters, digits, <c>_</c> and <c>-</c>, at most 64
    /// (<see cref="Connectors.ToolNames"/> makes a connector's names fit).</param>
    /// <param name="DisplayName">What the reader is shown while it runs, in Traditional Chinese.</param>
    /// <param name="Description">What it does and when to use it, for the model.</param>
    /// <param name="InputSchema">The JSON Schema of its input: an object schema.</param>
    /// <param name="Effect">What calling it does.</param>
    public sealed record AiToolDescriptor(
        string Name, string DisplayName, string Description, JsonElement InputSchema, ToolEffect Effect);

    /// <summary>Who a tool runs for, and in which conversation.</summary>
    public sealed record AiToolContext(string UserId, string ConversationId);

    /// <summary>
    /// What a tool call came to. <paramref name="Content"/> goes back to the model (wrapped as
    /// untrusted data, see <c>ClientToolRunner</c>); <paramref name="Card"/> is what the reader is shown in
    /// the answer, a <c>{ kind, … }</c> object the client draws (docs/ai-native/design/04 §1.5).
    /// </summary>
    /// <param name="Content">The result as text, for the model.</param>
    /// <param name="IsError">The call failed; <paramref name="Content"/> says why, in words the model can pass on.</param>
    /// <param name="Card">What the reader is shown, or null for nothing.</param>
    public sealed record AiToolResult(string Content, bool IsError = false, object? Card = null)
    {
        /// <summary>A failed call, with what the model is told.</summary>
        public static AiToolResult Error(string message) => new(message, IsError: true);
    }

    /// <summary>
    /// A tool the server runs itself, in the reader's name: one of the site's own (a word lookup, a
    /// data question) or, later, one of a connector's. A profile hands its tools to the turn through
    /// <see cref="ChatProfileSetup.Tools"/>.
    /// </summary>
    public interface IAiTool
    {
        AiToolDescriptor Descriptor { get; }

        /// <summary>
        /// Runs one call. A failure the model should hear about (bad input, nothing found, an upstream
        /// error) is an <see cref="AiToolResult.Error"/>, not an exception; an exception is also
        /// reported to the model as a failure, without its message.
        /// </summary>
        Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct);
    }
}
