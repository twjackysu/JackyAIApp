using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    public class User
    {
        [Key]
        public required string Id { get; set; }

        /// <summary>
        /// User's name
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// User's email
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// Gets or sets the remaining credit balance for the user.
        /// </summary>
        public required ulong CreditBalance { get; set; }

        /// <summary>
        /// Gets or sets the total number of credits the user has used.
        /// </summary>
        public required ulong TotalCreditsUsed { get; set; }

        /// <summary>
        /// Gets or sets the date and time when the credit data was last updated.
        /// </summary>
        public required DateTime LastUpdated { get; set; }

        /// <summary>
        /// Indicates if the user has administrator privileges.
        /// </summary>
        public bool? IsAdmin { get; set; }

        // Navigation properties
        public ICollection<UserWord> UserWords { get; set; } = [];
        public ICollection<JiraConfig> JiraConfigs { get; set; } = [];
        public ICollection<UserConnector> UserConnectors { get; set; } = [];
    }

    /// <summary>
    /// Join table to establish many-to-many relationship between Users and Words
    /// </summary>
    public class UserWord
    {
        public int Id { get; set; }

        public required string UserId { get; set; }
        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        public required string WordId { get; set; }
        [ForeignKey(nameof(WordId))]
        public Word Word { get; set; } = null!;

        /// <summary>
        /// The date when the word was added to the user's collection.
        /// </summary>
        public DateTime DateAdded { get; set; }
    }

    /// <summary>
    /// SQL version of JiraConfig
    /// </summary>
    public class JiraConfig
    {
        [Key]
        public required string Id { get; set; }

        /// <summary>
        /// The Jira site URL (e.g., "https://your-domain.atlassian.net")
        /// </summary>
        public required string Domain { get; set; }

        /// <summary>
        /// The Jira email address used for authentication
        /// </summary>
        public required string Email { get; set; }

        /// <summary>
        /// The API token used for Jira API authentication
        /// </summary>
        public required string Token { get; set; }

        /// <summary>
        /// Foreign key to the User table
        /// </summary>
        public required string UserId { get; set; }
        
        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;
    }

    /// <summary>
    /// User's external service connectors (Microsoft, Atlassian, Google)
    /// </summary>
    public class UserConnector
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Foreign key to the User table
        /// </summary>
        public required string UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        /// <summary>
        /// Provider name: Microsoft, Atlassian, Google
        /// </summary>
        public required string ProviderName { get; set; }

        /// <summary>
        /// Encrypted access token for API calls
        /// </summary>
        public string? EncryptedAccessToken { get; set; }

        /// <summary>
        /// Encrypted refresh token for token renewal
        /// </summary>
        public string? EncryptedRefreshToken { get; set; }

        /// <summary>
        /// Access token expiration time
        /// </summary>
        public DateTime? TokenExpiresAt { get; set; }

        /// <summary>
        /// Refresh token expiration time (typically 90 days)
        /// </summary>
        public DateTime? RefreshTokenExpiresAt { get; set; }

        /// <summary>
        /// Whether this connector is currently active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When this connector was first created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When this connector was last updated
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}