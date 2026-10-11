using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;

namespace JackyAIApp.Server.DTO.Portfolio
{
    /// <summary>
    /// The 回落煞車 market signal (Issue #431). Market data shared by everyone: nothing here
    /// depends on the caller's holdings — the front end turns it into a personal suggestion.
    /// Percentages are in percent (−1.2 means 1.2% below the high), as in the other portfolio DTOs.
    /// While <see cref="IsInitializing"/> is true every other signal field is null.
    /// </summary>
    public class DrawdownBrakeDto
    {
        /// <summary>The newest finalized trading day the signal uses.</summary>
        public DateOnly? AsOfDate { get; set; }

        /// <summary>State after <see cref="AsOfDate"/>'s close.</summary>
        public BrakeState? State { get; set; }

        /// <summary>State after the trading day before <see cref="AsOfDate"/>; what lets a caller
        /// tell a switch (the state changed on <see cref="AsOfDate"/>) from a steady state.</summary>
        public BrakeState? PrevState { get; set; }

        /// <summary>The day the current state began; null when it has held since the series started.</summary>
        public DateOnly? LastSwitchDate { get; set; }

        public decimal? Close { get; set; }
        public decimal? High252 { get; set; }
        public decimal? Sma60 { get; set; }

        /// <summary>close ÷ 252-day high − 1, in percent.</summary>
        public decimal? DrawdownPct { get; set; }

        /// <summary>How far <see cref="DrawdownPct"/> is above the −8% trigger, in percentage
        /// points; negative once below it.</summary>
        public decimal? DistanceToTriggerPct { get; set; }

        /// <summary>Data that should have arrived has not (a market holiday does not count).</summary>
        public bool IsStale { get; set; }

        /// <summary>Not enough history stored yet (first run, or the backfill is still going).</summary>
        public bool IsInitializing { get; set; }

        /// <summary>Intraday estimate; always null until the second phase (TASK-038).</summary>
        public DrawdownBrakeEstimateDto? Estimate { get; set; }

        /// <summary>An earlier estimate disagreed with the official state; always false until TASK-038.</summary>
        public bool EstimateCorrected { get; set; }
    }

    /// <summary>The second-phase intraday estimate. Defined with the contract; filled by TASK-038.</summary>
    public class DrawdownBrakeEstimateDto
    {
        public DateTimeOffset AsOfTime { get; set; }
        public decimal EstimatedClose { get; set; }
        public BrakeState State { get; set; }
        public BrakeState PrevState { get; set; }
        public decimal DrawdownPct { get; set; }
    }
}
