using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    public class DrawdownBrakeDtoTests
    {
        private static DrawdownBrakeDto Full() => new()
        {
            AsOfDate = new DateOnly(2026, 10, 7),
            State = BrakeState.OFFENSE,
            PrevState = BrakeState.DEFENSE,
            LastSwitchDate = new DateOnly(2026, 5, 12),
            Close = 41000m,
            High252 = 42000m,
            Sma60 = 40000m,
            DrawdownPct = -2.38m,
            DistanceToTriggerPct = 5.62m,
            IsStale = true,
            IsInitializing = false,
            LastCheckedAt = new DateTimeOffset(2026, 10, 8, 2, 0, 0, TimeSpan.Zero),
            GeneratedAt = new DateTimeOffset(2026, 10, 8, 2, 1, 0, TimeSpan.Zero),
        };

        [Fact]
        public void WithEstimate_CarriesEveryOtherField_ThoughOneAddedLaterIsNotNamedHere()
        {
            var official = Full();
            var estimate = new DrawdownBrakeEstimateDto { State = BrakeState.DEFENSE };

            var copy = official.WithEstimate(estimate, estimateCorrected: true);

            // Every public property but the two being set, by reflection: a field added to the DTO later
            // and not copied would fail here.
            foreach (var property in typeof(DrawdownBrakeDto).GetProperties()
                         .Where(p => p.Name is not (nameof(DrawdownBrakeDto.Estimate) or nameof(DrawdownBrakeDto.EstimateCorrected))))
                Assert.Equal(property.GetValue(official), property.GetValue(copy));
            Assert.Same(estimate, copy.Estimate);
            Assert.True(copy.EstimateCorrected);
        }

        [Fact]
        public void WithEstimate_LeavesTheOriginalAlone()
        {
            var official = Full();

            var copy = official.WithEstimate(new DrawdownBrakeEstimateDto(), estimateCorrected: true);

            Assert.NotSame(official, copy);
            Assert.Null(official.Estimate);
            Assert.False(official.EstimateCorrected);
        }
    }
}
