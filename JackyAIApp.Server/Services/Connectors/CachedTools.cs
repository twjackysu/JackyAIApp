using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>One tool a connector's server last listed, as kept with its installation.</summary>
    /// <param name="Name">The tool's own name on its server.</param>
    /// <param name="Title">What the reader is shown, if the server gave one.</param>
    /// <param name="Effect">What calling it does, as worked out when it was listed (an MCP server's
    /// <c>readOnlyHint</c> / <c>destructiveHint</c>).</param>
    /// <param name="InputSchema">Its JSON Schema, as the server sent it.</param>
    /// <param name="UiResourceUri">The MCP App its results are drawn with (<c>_meta.ui.resourceUri</c>, a <c>ui://</c> URI),
    /// if it named one (docs/ai-native/design/09). Left out of the stored JSON when null.</param>
    /// <param name="AppOnly">Only its app may call it (<c>_meta.ui.visibility</c> without <c>model</c>): never offered to the
    /// model. Left out of the stored JSON when false.</param>
    /// <param name="NotForApp">Its server's apps may not call it (<c>_meta.ui.visibility</c> without <c>app</c>). Left out of the
    /// stored JSON when false.</param>
    public sealed record CachedTool(string Name, string? Title, string Description, ToolEffect Effect, JsonElement InputSchema,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? UiResourceUri = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool AppOnly = false,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool NotForApp = false);

    /// <summary>
    /// The tools of an installation as stored (<c>ConnectorInstallations.ToolsCacheJson</c>, a JSON array).
    /// It was written from what an outside server said, so it is read leniently: an entry without a
    /// name is left out, one without an effect counts as a write, and anything unreadable is no tools.
    /// </summary>
    public static class CachedTools
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        public static IReadOnlyList<CachedTool> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return [];
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
                var tools = new List<CachedTool>();
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var name = StringOf(e, "name");
                    if (string.IsNullOrEmpty(name)) continue;
                    var effect = Enum.TryParse<ToolEffect>(StringOf(e, "effect"), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                        ? parsed
                        : ToolEffect.Write;
                    var schema = e.TryGetProperty("inputSchema", out var s) && s.ValueKind == JsonValueKind.Object
                        ? s.Clone()
                        : JsonSerializer.SerializeToElement(new { type = "object" });
                    var appOnly = e.TryGetProperty("appOnly", out var only) && only.ValueKind == JsonValueKind.True;
                    var notForApp = e.TryGetProperty("notForApp", out var notApp) && notApp.ValueKind == JsonValueKind.True;
                    tools.Add(new CachedTool(name, StringOf(e, "title"), StringOf(e, "description") ?? "", effect, schema,
                        McpToolClient.AppUriOf(StringOf(e, "uiResourceUri")), appOnly, notForApp));
                }
                return tools;
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public static string Serialize(IEnumerable<CachedTool> tools) => JsonSerializer.Serialize(tools, Json);

        /// <summary>
        /// The tools of <paramref name="after"/> that were in <paramref name="before"/> but say something else now:
        /// their title, description, schema or effect. A server can change what a tool says it does after the reader
        /// trusted it (a "rug pull"), so such a tool is not run on its own on the strength of the old words.
        /// </summary>
        public static IReadOnlyList<string> Changed(IReadOnlyList<CachedTool> before, IReadOnlyList<CachedTool> after)
        {
            var old = before.GroupBy(t => t.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            return after
                .Where(t => old.TryGetValue(t.Name, out var was) && !Same(was, t))
                .Select(t => t.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        // The schema as JSON writes it, so the same schema stored and fresh from the server compare equal.
        private static bool Same(CachedTool a, CachedTool b) =>
            a.Title == b.Title && a.Description == b.Description && a.Effect == b.Effect &&
            JsonSerializer.Serialize(a.InputSchema) == JsonSerializer.Serialize(b.InputSchema);

        private static string? StringOf(JsonElement e, string property) =>
            e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
