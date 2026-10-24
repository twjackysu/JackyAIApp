using System.ComponentModel.DataAnnotations;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// A personal access token a user made so another AI (Claude Desktop, Cursor, ChatGPT…) may use the site's
    /// tools in their name through the site's own MCP server (<c>POST /api/mcp</c>). Only its SHA-256 is kept:
    /// the token itself is shown once, when it is made. Revoking it deletes the row.
    /// </summary>
    public class SiteAccessToken
    {
        [Key, MaxLength(64)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required, MaxLength(255)]
        public required string UserId { get; set; }

        /// <summary>What the user called it ("Claude Desktop").</summary>
        [Required, MaxLength(50)]
        public required string Name { get; set; }

        /// <summary>The token's SHA-256, lowercase hex: what a request's token is looked up by.</summary>
        [Required, MaxLength(64)]
        public required string TokenHash { get; set; }

        /// <summary>The token's start and end ("jsk_AbC…xYz9"), so the user can tell which is which.</summary>
        [Required, MaxLength(20)]
        public required string Hint { get; set; }

        /// <summary>Whether the site's tools that add or change something are offered too; reads only otherwise.</summary>
        public bool AllowWrites { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>When it stops working (UTC).</summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>When a request last used it (UTC, to the minute or so); null for never.</summary>
        public DateTime? LastUsedAt { get; set; }
    }
}
