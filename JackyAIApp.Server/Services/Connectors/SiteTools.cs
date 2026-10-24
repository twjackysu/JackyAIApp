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

    /// <summary>The connector the site's own tools belong to: always there, nothing to sign in to.</summary>
    public static class SiteConnector
    {
        public const string Key = AiToolDescriptor.SiteConnector;
        public const string DisplayName = "本站功能";
        public const string Description = "AI 可以直接使用這個網站的功能：查詢與彙總資料、讀取資料結構等。寫入型的動作一律先問你。";
        public const string Publisher = "Jacky Studio";
    }
}
