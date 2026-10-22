using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// A user-owned dashboard: a named collection of saved DataAnalysis widgets laid out on a grid.
    /// Each widget stores a re-runnable summarize plan; the data is re-fetched when the dashboard is viewed.
    /// </summary>
    public class Dashboard
    {
        [Key]
        public Guid Id { get; set; }

        /// <summary>Owner of this dashboard.</summary>
        [Required]
        public required string UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        [Required, MaxLength(200)]
        public required string Name { get; set; }

        /// <summary>Who can view this dashboard: "Private" | "Public" | "Restricted".</summary>
        [Required, MaxLength(20)]
        public string Visibility { get; set; } = "Private";

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UpdatedAt { get; set; }

        public List<DashboardWidget> Widgets { get; set; } = [];

        /// <summary>Explicit viewers when Visibility == "Restricted".</summary>
        public List<DashboardShare> Shares { get; set; } = [];
    }
}
