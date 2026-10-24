using System.Text.Json;

namespace JackyAIApp.Server.Services.Connectors
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

    public static class ToolEffects
    {
        /// <summary>The effect as the client names it (<c>ToolEffect</c> in the client's connectors API types).</summary>
        public static string WireName(this ToolEffect effect) => effect switch
        {
            ToolEffect.Read => "read",
            ToolEffect.Write => "write",
            ToolEffect.Destructive => "destructive",
            _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, null),
        };
    }

    /// <summary>A tool as the model and the reader see it.</summary>
    /// <param name="Name">The name the model calls it by: letters, digits, <c>_</c> and <c>-</c>, at most 64
    /// (<see cref="ToolNames"/> makes a connector's names fit).</param>
    /// <param name="DisplayName">What the reader is shown while it runs, in Traditional Chinese.</param>
    /// <param name="Description">What it does and when to use it, for the model.</param>
    /// <param name="InputSchema">The JSON Schema of its input: an object schema.</param>
    /// <param name="Effect">What calling it does.</param>
    /// <param name="Connector">Whose tool it is, as the reader is asked about it: <see cref="AiToolDescriptor.SiteConnector"/> for
    /// the site's own, else the connector's key.</param>
    /// <param name="ConnectorName">What the reader calls that connector ("Jira"); null for the site's own.</param>
    public sealed record AiToolDescriptor(
        string Name, string DisplayName, string Description, JsonElement InputSchema, ToolEffect Effect,
        string Connector = AiToolDescriptor.SiteConnector, string? ConnectorName = null)
    {
        /// <summary>The connector key of the site's own tools (the client's <c>SITE_CONNECTOR</c>).</summary>
        public const string SiteConnector = "site";
    }

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
    /// <c>ChatProfileSetup.Tools</c>.
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

    /// <summary>
    /// A tool of the site's own that can say in words what one call of it would do ("把「serendipity」加入你的單字庫"),
    /// shown on the confirmation card above the call's exact arguments. Only the site's own tools: a connector's
    /// words would be an outside server's, which the card must not present as ours.
    /// </summary>
    public interface IDescribesCall
    {
        /// <summary>The call in one short sentence; null when the input says too little to tell.</summary>
        string? Describe(JsonElement input, AiToolContext context);
    }
}
