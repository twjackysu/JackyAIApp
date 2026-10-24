using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.Architecture
{
    /// <summary>
    /// Every tool the site writes itself describes itself the same way (docs/ai-native/CONTRIBUTING-CONNECTORS.md):
    /// a <c>public static readonly AiToolDescriptor Definition</c> (what the connectors page lists and the tool
    /// returns), a name the model can call that never looks like a connector's (<c>m1__…</c>), a name and an
    /// explanation for the reader and the model, and an object schema whose required fields it has. A new tool is
    /// checked here without a test of its own; only an outside connector's tools take their names from their server.
    /// </summary>
    public partial class SiteToolDefinitionsTests
    {
        [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
        private static partial Regex ToolName();

        private static readonly Type[] ToolTypes = typeof(IAiTool).Assembly.GetTypes()
            .Where(t => typeof(IAiTool).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .ToArray();

        private static AiToolDescriptor? DefinitionOf(Type tool) =>
            tool.GetField("Definition", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as AiToolDescriptor;

        public static TheoryData<string> Tools()
        {
            var data = new TheoryData<string>();
            foreach (var t in ToolTypes.Where(t => DefinitionOf(t) != null)) data.Add(t.FullName!);
            return data;
        }

        private static AiToolDescriptor Definition(string type) => DefinitionOf(ToolTypes.Single(t => t.FullName == type))!;

        [Fact]
        public void EveryToolTheSiteWrites_HasADefinition_AndTheyAreFound()
        {
            var without = ToolTypes.Where(t => DefinitionOf(t) == null).Select(t => t.Name).ToList();

            Assert.Equal(new[] { "McpConnectorTool" }, without);
            Assert.True(ToolTypes.Count(t => DefinitionOf(t) != null) >= 9, "the site's tools were not found");
        }

        [Fact]
        public void NoTwoTools_ShareAName()
        {
            var names = ToolTypes.Select(DefinitionOf).OfType<AiToolDescriptor>().Select(d => d.Name).ToList();

            Assert.Equal(names.Distinct().Count(), names.Count);
        }

        [Theory]
        [MemberData(nameof(Tools))]
        public void ItsNameIsOneTheModelCanCall_AndNeverAConnectorsShape(string type)
        {
            var d = Definition(type);

            Assert.Matches(ToolName(), d.Name);
            Assert.DoesNotContain("__", d.Name);
            Assert.Equal(AiToolDescriptor.SiteConnector, d.Connector);
        }

        [Theory]
        [MemberData(nameof(Tools))]
        public void ItSaysWhatItIs_ToTheReaderAndTheModel(string type)
        {
            var d = Definition(type);

            Assert.False(string.IsNullOrWhiteSpace(d.DisplayName));
            Assert.True(d.DisplayName.Length <= 20, $"{d.Name}'s display name is long for a chip: {d.DisplayName}");
            Assert.True(d.Description.Length >= 20, $"{d.Name} says too little about when to use it");
        }

        [Theory]
        [MemberData(nameof(Tools))]
        public void ItsInputIsAnObjectSchema_WithTheFieldsItRequires(string type)
        {
            var schema = Definition(type).InputSchema;

            Assert.Equal(JsonValueKind.Object, schema.ValueKind);
            Assert.Equal("object", schema.GetProperty("type").GetString());
            var properties = schema.TryGetProperty("properties", out var p) ? p : default;
            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var field in required.EnumerateArray())
                    Assert.True(properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(field.GetString()!, out _),
                        $"required field {field} is not among the properties");
            }
        }
    }
}
