namespace JackyAIApp.Server.DTO
{
    // The tokens that let another AI use the site's tools through its MCP server (the client's
    // apis/connectorsApis/types.ts, SiteAccessToken).

    /// <summary>One of the reader's tokens, without the token itself (it is shown only when made).</summary>
    /// <param name="Hint">Its start and end ("jsk_AbC…xYz9").</param>
    public sealed record SiteAccessTokenDto(
        string Id, string Name, string Hint, bool AllowWrites, DateTime CreatedAt, DateTime ExpiresAt, DateTime? LastUsedAt);

    /// <summary>A token just made: <paramref name="Token"/> is the only time it is shown.</summary>
    public sealed record CreatedSiteAccessTokenDto(SiteAccessTokenDto Info, string Token);

    /// <summary>The reader's tokens, and where the server is.</summary>
    /// <param name="Limit">The most tokens one reader may have.</param>
    public sealed record SiteAccessTokenListDto(IReadOnlyList<SiteAccessTokenDto> Tokens, int Limit);

    public sealed class CreateSiteAccessTokenRequestDto
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Offer the tools that add or change something too.</summary>
        public bool AllowWrites { get; set; }

        /// <summary>30, 90 or 365.</summary>
        public int ExpiresInDays { get; set; } = 90;
    }
}
