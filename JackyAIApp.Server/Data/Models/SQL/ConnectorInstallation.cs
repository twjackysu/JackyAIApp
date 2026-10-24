using System.ComponentModel.DataAnnotations;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// A user's connector for the AI (docs/ai-native/design/03 §3): one of the catalog's (by its key,
    /// once per user) or an MCP server of their own (<see cref="CustomKey"/>, many). Holds its
    /// credentials (encrypted), whether it is on, the reader's permission for each of its tools, the
    /// tools it last listed and how it last went. A connector that is always there (the site's own
    /// tools) has a row only once its tool permissions were changed.
    /// </summary>
    public class ConnectorInstallation
    {
        /// <summary>The <see cref="DefinitionKey"/> of an MCP server the user added themselves.</summary>
        public const string CustomKey = "custom";

        [Key, MaxLength(64)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required, MaxLength(255)]
        public required string UserId { get; set; }

        /// <summary>The catalog entry's key, or <see cref="CustomKey"/>.</summary>
        [Required, MaxLength(100)]
        public required string DefinitionKey { get; set; }

        /// <summary>The name the user gave a server of their own; null for a catalog entry (it has its own).</summary>
        [MaxLength(100)]
        public string? DisplayName { get; set; }

        /// <summary>A custom MCP server's URL as normalized (<c>McpServerUrl.Normalize</c>).</summary>
        [MaxLength(500)]
        public string? ServerUrl { get; set; }

        /// <summary>None, ApiKey, Bearer or OAuth.</summary>
        [Required, MaxLength(20)]
        public string AuthKind { get; set; } = "None";

        /// <summary>The credentials as JSON, encrypted with <c>ITokenEncryptionService</c>; null for none.</summary>
        public string? EncryptedSecret { get; set; }

        public bool Enabled { get; set; } = true;

        /// <summary><c>{ "toolName": "Auto" | "Confirm" | "Disabled" }</c> for the tools the user changed.</summary>
        public string? ToolSettingsJson { get; set; }

        /// <summary>The tools the server last listed (name, description, schema, annotations), as JSON.</summary>
        public string? ToolsCacheJson { get; set; }

        public DateTime? ToolsFetchedAt { get; set; }

        /// <summary>Ok, NeedsAuth, Error or Unknown.</summary>
        [Required, MaxLength(20)]
        public string LastStatus { get; set; } = "Unknown";

        /// <summary>What went wrong last, in words for the owner (never a token).</summary>
        [MaxLength(500)]
        public string? LastError { get; set; }

        public DateTime? LastTestedAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
