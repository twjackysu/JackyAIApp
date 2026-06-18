using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// One widget on a dashboard. Stores the re-runnable summarize plan (context + viewBy +
    /// measures + filters as JSON), the chosen visualization type, and its grid position/size.
    /// The widget's data is produced by re-running the plan against the summarize endpoint on view.
    /// </summary>
    public class DashboardWidget
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public required Guid DashboardId { get; set; }

        [ForeignKey(nameof(DashboardId))]
        public Dashboard Dashboard { get; set; } = null!;

        [MaxLength(200)]
        public string? Title { get; set; }

        /// <summary>Visualization id (matches the frontend VizId, e.g. "column", "kpi", "treemap").</summary>
        [Required, MaxLength(40)]
        public required string VizType { get; set; }

        /// <summary>Serialized SummarizeRequest (context + viewBy + measures + filters) — re-run on view.</summary>
        [Required]
        public required string PlanJson { get; set; }

        // Grid position/size in react-grid-layout units (12-col grid).
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
