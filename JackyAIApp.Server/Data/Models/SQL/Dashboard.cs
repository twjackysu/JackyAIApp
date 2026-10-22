using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>Who can view a dashboard. Stored by name (see AzureSQLDBContext), so the values
    /// already in the table are these names.</summary>
    public enum DashboardVisibility
    {
        /// <summary>Its owner only.</summary>
        Private,
        /// <summary>Any signed-in user with the link.</summary>
        Public,
        /// <summary>Its owner and the users it is shared with (<see cref="DashboardShare"/>).</summary>
        Restricted,
        /// <summary>Anyone with the link, signed in or not.</summary>
        Anyone,
    }

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

        /// <summary>Who can view this dashboard; see <c>DashboardAccessPolicy</c>.</summary>
        [Required, MaxLength(20)]
        public DashboardVisibility Visibility { get; set; } = DashboardVisibility.Private;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UpdatedAt { get; set; }

        public List<DashboardWidget> Widgets { get; set; } = [];

        /// <summary>Explicit viewers when Visibility is Restricted.</summary>
        public List<DashboardShare> Shares { get; set; } = [];
    }
}
