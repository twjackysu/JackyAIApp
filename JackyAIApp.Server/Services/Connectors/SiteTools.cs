using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// Some of the site's own tools, as one feature registers them (<c>services.AddSingleton(new
    /// SiteToolSet(…))</c> in its module's registration), so the connectors page can list them and
    /// the reader set what the AI may do with each. The tools themselves still come with the assistant
    /// that uses them (<c>ChatProfileSetup.Tools</c>): a tool may need the page it is asked from.
    /// </summary>
    /// <param name="Group">What the reader calls the feature they belong to ("資料分析").</param>
    public sealed record SiteToolSet(string Group, IReadOnlyList<AiToolDescriptor> Tools);

    /// <summary>
    /// One of the site's own tools that needs nothing of the page it is asked from (a word lookup, adding
    /// a word): a feature registers it (<c>services.AddScoped&lt;ISiteTool, …&gt;()</c>, and lists it in a
    /// <see cref="SiteToolSet"/>), and an assistant that offers the site's tools, Studio AI, gets them all.
    /// </summary>
    public interface ISiteTool : IAiTool;

    /// <summary>
    /// A button a tool's card offers in another AI's frame (the site's MCP App, design/03 §9.2): pressing it calls
    /// <paramref name="Tool"/>, another of the site's tools, with <paramref name="Arguments"/>, through that AI's host
    /// (<c>tools/call</c> from the app). The reader pressing it is the asking: there is no model in between.
    /// </summary>
    public sealed record SiteAppAction(string Tool, string Label, JsonObject Arguments);

    /// <summary>
    /// A site tool whose card has a next step the reader may take with one press (a word looked up → add it to the
    /// repository). The site's MCP server offers an action only when the token may use its tool (a write only with a
    /// token that allows writes, never one the reader turned off).
    /// </summary>
    public interface IOffersAppActions
    {
        /// <summary>The buttons for the card of a call with <paramref name="input"/>; none when the input says too little.</summary>
        IReadOnlyList<SiteAppAction> AppActionsFor(JsonElement input);
    }

    /// <summary>The connector the site's own tools belong to: always there, nothing to sign in to.</summary>
    public static class SiteConnector
    {
        public const string Key = AiToolDescriptor.SiteConnector;
        public const string DisplayName = "本站功能";
        public const string Description = "AI 可以直接使用這個網站的功能：查詢與彙總資料、讀取資料結構等。寫入型的動作一律先問你。";
        public const string Publisher = "Jacky Studio";
    }
}
