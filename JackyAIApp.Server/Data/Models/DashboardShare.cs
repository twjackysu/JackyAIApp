using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// Grants a specific user view access to a Restricted dashboard (Confluence-style).
    /// </summary>
    public class DashboardShare
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public required Guid DashboardId { get; set; }

        [ForeignKey(nameof(DashboardId))]
        public Dashboard Dashboard { get; set; } = null!;

        /// <summary>The user granted access.</summary>
        [Required]
        public required string UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
