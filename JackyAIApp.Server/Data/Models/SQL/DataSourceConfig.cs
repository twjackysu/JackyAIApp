using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    public enum DataSourceConnectorType { MsSql, Adx }

    /// <summary>
    /// Stores a per-user saved DataAnalysis connector config (connection string or ADX credentials).
    /// Sensitive fields (passwords, client secrets) are stored as-is — encrypt before
    /// storing in production environments.
    /// </summary>
    public class DataSourceConfig
    {
        [Key]
        public Guid Id { get; set; }

        /// <summary>Owner of this connector — scoped per user.</summary>
        [Required]
        public required string UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        [Required, MaxLength(100)]
        public required string Name { get; set; }

        public DataSourceConnectorType ConnectorType { get; set; }

        /// <summary>
        /// JSON blob — shape depends on ConnectorType.
        /// MsSql: { "connectionString": "..." }
        /// Adx:   { "clusterUri": "...", "database": "...", "tenantId": "...",
        ///          "clientId": "...", "clientSecret": "...", "connectionString": null }
        /// </summary>
        [Required]
        public required string ConfigJson { get; set; }

        /// <summary>Default table name pre-filled in the query UI when this source is selected.</summary>
        [MaxLength(200)]
        public string? DefaultTableName { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
