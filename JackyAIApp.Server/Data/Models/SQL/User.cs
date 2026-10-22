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
        /// Gets or sets the date and time this user record was last updated. No longer
        /// credit-specific — also touched by non-credit flows (e.g. RepositoryService's
        /// word-list add/remove).
        /// </summary>
        public required DateTime LastUpdated { get; set; }

        /// <summary>
        /// Multiplier applied to <c>UsageLimits:DailyBudget</c> to raise this user's effective
        /// daily usage-limit budget above the default (DECISION-006/007). Set directly in the DB
        /// by the author on request — there is no admin UI for this (out of scope).
        /// </summary>
        public decimal UsageLimitMultiplier { get; set; } = 1m;

        /// <summary>
        /// Indicates if the user has administrator privileges.
        /// </summary>
        public bool? IsAdmin { get; set; }

        // Engagement / Gamification fields

        /// <summary>
        /// Current consecutive daily challenge streak.
        /// </summary>
        public int CurrentStreak { get; set; } = 0;

        /// <summary>
        /// Longest streak ever achieved.
        /// </summary>
        public int LongestStreak { get; set; } = 0;

        /// <summary>
        /// The last date the user completed a daily challenge (UTC date only).
        /// </summary>
        public DateTime? LastStreakDate { get; set; }

        /// <summary>
        /// Total experience points earned.
        /// </summary>
        public int TotalXP { get; set; } = 0;

        /// <summary>
        /// Streak freezes available. One missed day is forgiven per freeze so a single slip
        /// doesn't reset the streak. Refilled to 1 at the start of each week.
        /// </summary>
        public int StreakFreezesAvailable { get; set; } = 1;

        /// <summary>
        /// The last date (UTC) a weekly streak freeze was granted, used to refill weekly.
        /// </summary>
        public DateTime? LastFreezeGrantDate { get; set; }
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

        // SM-2 Spaced Repetition fields

        /// <summary>
        /// Next review date (UTC). Null means never reviewed — due immediately.
        /// </summary>
        public DateTime? NextReviewDate { get; set; }

        /// <summary>
        /// The UTC date of the latest review (a flashcard or an exam answer); null when never reviewed.
        /// What the weekly report counts: <see cref="NextReviewDate"/> is when the word is due, so a word
        /// reviewed weeks ago and scheduled for next month would pass a "this week" test on it.
        /// </summary>
        public DateTime? LastReviewedDate { get; set; }

        /// <summary>
        /// Current review interval in days. Starts at 1.
        /// </summary>
        public double ReviewIntervalDays { get; set; } = 1;

        /// <summary>
        /// SM-2 ease factor. Starts at 2.5, minimum 1.3.
        /// </summary>
        public double EaseFactor { get; set; } = 2.5;

        /// <summary>
        /// Total number of reviews completed for this word.
        /// </summary>
        public int ReviewCount { get; set; } = 0;

        /// <summary>
        /// Number of consecutive correct reviews.
        /// </summary>
        public int ConsecutiveCorrect { get; set; } = 0;
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

        // Custom OAuth configuration (optional, overrides system defaults)

        /// <summary>
        /// Custom OAuth Client ID (encrypted)
        /// </summary>
        public string? CustomClientId { get; set; }

        /// <summary>
        /// Custom OAuth Client Secret (encrypted)
        /// </summary>
        public string? EncryptedCustomClientSecret { get; set; }

        /// <summary>
        /// Custom Tenant ID for Microsoft
        /// </summary>
        public string? CustomTenantId { get; set; }

        /// <summary>
        /// Custom OAuth Scopes
        /// </summary>
        public string? CustomScopes { get; set; }
    }
}