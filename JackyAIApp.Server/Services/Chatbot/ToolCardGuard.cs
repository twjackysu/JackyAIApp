using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// What a tool's card (<c>tool_result.card</c>, docs/ai-native/design/04 §1.5) may be before the reader
    /// is sent it and it is kept with the answer. A card comes from a tool, and a connector's tool from
    /// anyone, so the server holds it to the limits the client draws within (the client's
    /// <c>parseToolCard</c> checks them again): an object of a known shape of kind, a table of at most
    /// <see cref="MaxColumns"/> columns and <see cref="MaxRows"/> rows (more are cut, and counted in
    /// <c>totalRows</c>), a link only to this site or https, and nothing over <see cref="MaxChars"/> in all.
    /// </summary>
    internal static partial class ToolCardGuard
    {
        public const int MaxColumns = 12;
        public const int MaxRows = 200;

        /// <summary>The most a card may come to as JSON (a chart's 500 rows of a few columns fit easily).</summary>
        public const int MaxChars = 200_000;

        [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$")]
        private static partial Regex KindPattern();

        /// <summary>The card as it may be sent, or null when it may not be sent at all.</summary>
        public static JsonElement? Check(JsonElement card)
        {
            if (card.ValueKind != JsonValueKind.Object) return null;
            if (JsonNode.Parse(card.GetRawText()) is not JsonObject node) return null;
            if (node["kind"] is not JsonValue kindValue || !kindValue.TryGetValue<string>(out var kind) ||
                !KindPattern().IsMatch(kind))
                return null;

            var ok = kind switch
            {
                "table" => CheckTable(node),
                "link" => IsSafeHref(node["href"]),
                _ => node["data"] is JsonObject,
            };
            if (!ok) return null;

            var json = node.ToJsonString();
            return json.Length > MaxChars ? null : JsonDocument.Parse(json).RootElement.Clone();
        }

        private static bool CheckTable(JsonObject table)
        {
            if (table["columns"] is not JsonArray columns || table["rows"] is not JsonArray rows) return false;
            while (columns.Count > MaxColumns) columns.RemoveAt(columns.Count - 1);
            if (rows.Count > MaxRows)
            {
                var total = rows.Count;
                while (rows.Count > MaxRows) rows.RemoveAt(rows.Count - 1);
                if (table["totalRows"] is not JsonValue given || !given.TryGetValue<int>(out var count) || count < total)
                    table["totalRows"] = total;
            }
            return true;
        }

        /// <summary>A path on this site (not <c>//host</c>), or an absolute https URL.</summary>
        internal static bool IsSafeHref(JsonNode? href)
        {
            if (href is not JsonValue value || !value.TryGetValue<string>(out var text)) return false;
            text = text.Trim();
            // A browser drops tabs and newlines from a URL, so "/\t/evil.example" would become "//evil.example".
            if (text.Any(char.IsControl)) return false;
            if (text.StartsWith('/')) return !text.StartsWith("//") && !text.StartsWith("/\\");
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
        }
    }
}
