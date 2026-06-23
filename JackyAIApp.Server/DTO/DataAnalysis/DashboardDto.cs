using System.Text.Json;

namespace JackyAIApp.Server.DTO.DataAnalysis
{
    /// <summary>Dashboard list item.</summary>
    public sealed record DashboardSummaryDto
    {
        public Guid Id { get; init; }
        public required string Name { get; init; }
        public int WidgetCount { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    /// <summary>A dashboard plus its widgets.</summary>
    public sealed record DashboardDetailDto
    {
        public Guid Id { get; init; }
        public required string Name { get; init; }
        public required List<DashboardWidgetDto> Widgets { get; init; }
    }

    public sealed record DashboardWidgetDto
    {
        public Guid Id { get; init; }
        public string? Title { get; init; }
        public required string VizType { get; init; }
        /// <summary>The re-runnable summarize plan (raw JSON: context + viewBy + measures + filters).</summary>
        public required JsonElement Plan { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int W { get; init; }
        public int H { get; init; }
    }

    public sealed record CreateDashboardRequest
    {
        public required string Name { get; init; }
    }

    public sealed record AddWidgetRequest
    {
        public string? Title { get; init; }
        public required string VizType { get; init; }
        /// <summary>SummarizeRequest JSON to re-run when the widget is viewed.</summary>
        public required JsonElement Plan { get; init; }
        public int W { get; init; } = 6;
        public int H { get; init; } = 8;
    }

    public sealed record RenameDashboardRequest
    {
        public required string Name { get; init; }
    }

    /// <summary>Batch grid layout update for a dashboard's widgets.</summary>
    public sealed record UpdateLayoutRequest
    {
        public required List<WidgetLayoutDto> Items { get; init; }
    }

    public sealed record WidgetLayoutDto
    {
        public required Guid Id { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int W { get; init; }
        public int H { get; init; }
    }
}
