using DataAnalysisLibrary.Core.Enums;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.DataAnalysis
{
    /// <summary>
    /// The NL-query planning pipeline, extracted from DataAnalysisController so it can be tested
    /// without the controller, the engine, or the HTTP stack — just a value source and the LLM.
    /// </summary>
    public class NlQueryPlannerTests
    {
        private readonly Mock<IOpenAIPromptService> _openAI = new();
        private readonly Mock<INlValueSource> _values = new();

        private NlQueryPlanner Planner() =>
            new(NullLogger<NlQueryPlanner>.Instance, _openAI.Object);

        private static readonly Func<string, Task<string?>> NoMaxDate = _ => Task.FromResult<string?>(null);

        [Fact]
        public async Task EmptyFields_IsABadRequest_WithoutCallingTheLlm()
        {
            var result = await Planner().BuildPlanAsync(
                [], "top regions", "system", _values.Object, NoMaxDate, CancellationToken.None);

            Assert.Null(result.Plan);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
            _openAI.Verify(x => x.GetCompletionWithSystemPromptAsync<NlPlanDto>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ValidPlan_IsReturned_WithNoNotes()
        {
            _values.Setup(v => v.DistinctAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, ColumnValues>(StringComparer.OrdinalIgnoreCase));

            var plan = new NlPlanDto
            {
                ViewBy   = [new GroupFieldDto { FieldName = "Region" }],
                Measures = [new MeasureFieldDto { FieldName = "Amount", Aggregation = AggregationType.Sum }],
            };
            _openAI.Setup(x => x.GetCompletionWithSystemPromptAsync<NlPlanDto>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((plan, (string?)null));

            var fields = new List<(string, string)> { ("Region", "String"), ("Amount", "Decimal") };

            var result = await Planner().BuildPlanAsync(
                fields, "total amount by region", "system", _values.Object, NoMaxDate, CancellationToken.None);

            Assert.NotNull(result.Plan);
            Assert.Null(result.Error);
            Assert.Equal("Region", Assert.Single(result.Plan!.ViewBy).FieldName);
            Assert.Empty(result.Notes);
        }

        [Fact]
        public async Task HallucinatedField_IsRejected()
        {
            _values.Setup(v => v.DistinctAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, ColumnValues>(StringComparer.OrdinalIgnoreCase));

            var plan = new NlPlanDto
            {
                ViewBy   = [new GroupFieldDto { FieldName = "NoSuchColumn" }],
                Measures = [new MeasureFieldDto { FieldName = "Amount", Aggregation = AggregationType.Sum }],
            };
            _openAI.Setup(x => x.GetCompletionWithSystemPromptAsync<NlPlanDto>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((plan, (string?)null));

            var fields = new List<(string, string)> { ("Region", "String"), ("Amount", "Decimal") };

            var result = await Planner().BuildPlanAsync(
                fields, "by mystery", "system", _values.Object, NoMaxDate, CancellationToken.None);

            Assert.Null(result.Plan);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }
    }
}
