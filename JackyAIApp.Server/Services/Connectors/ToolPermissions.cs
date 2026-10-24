using System.Text.Json;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>What the AI may do with one tool: run it on its own, ask the reader first, or not see it.</summary>
    public enum ToolPermission
    {
        Auto,
        Confirm,
        Disabled,
    }

    /// <summary>
    /// Which permission a tool has (docs/ai-native/design/03 §2.2): the reader's own choice for it if they
    /// made one, else its default — a read runs on its own, anything else is asked first. A tool that
    /// cannot be undone is always asked first, whatever was stored.
    /// </summary>
    public static class ToolPermissions
    {
        /// <summary>A tool's permission when the reader has not chosen one.</summary>
        public static ToolPermission Default(ToolEffect effect) =>
            effect == ToolEffect.Read ? ToolPermission.Auto : ToolPermission.Confirm;

        /// <summary>Whether the reader may choose <paramref name="permission"/> for a tool of <paramref name="effect"/>.</summary>
        public static bool Allowed(ToolEffect effect, ToolPermission permission) =>
            !(effect == ToolEffect.Destructive && permission == ToolPermission.Auto);

        /// <summary>The tool's permission, given the reader's choices (<see cref="Parse"/>).</summary>
        public static ToolPermission For(string toolName, ToolEffect effect, IReadOnlyDictionary<string, ToolPermission> chosen) =>
            chosen.TryGetValue(toolName, out var permission) && Allowed(effect, permission) ? permission : Default(effect);

        /// <summary>Whether a call of a tool with this permission waits for the reader.</summary>
        public static bool AsksFirst(ToolPermission permission) => permission == ToolPermission.Confirm;

        /// <summary>The permission as the client names it.</summary>
        public static string WireName(this ToolPermission permission) => permission switch
        {
            ToolPermission.Auto => "auto",
            ToolPermission.Confirm => "confirm",
            ToolPermission.Disabled => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null),
        };

        /// <summary>A permission as the client names it, or null for none of them.</summary>
        public static ToolPermission? FromWireName(string? name) => name switch
        {
            "auto" => ToolPermission.Auto,
            "confirm" => ToolPermission.Confirm,
            "disabled" => ToolPermission.Disabled,
            _ => null,
        };

        /// <summary>
        /// The reader's choices as stored (<c>ToolSettingsJson</c>: tool name → permission name); an
        /// entry that is not a permission is left out, and anything unreadable is no choices at all.
        /// </summary>
        public static Dictionary<string, ToolPermission> Parse(string? json)
        {
            var chosen = new Dictionary<string, ToolPermission>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return chosen;
            try
            {
                var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                foreach (var (tool, name) in stored ?? [])
                    if (Enum.TryParse<ToolPermission>(name, ignoreCase: false, out var permission) && Enum.IsDefined(permission))
                        chosen[tool] = permission;
            }
            catch (JsonException)
            {
                // A value written by hand or by an older version: the defaults apply.
            }
            return chosen;
        }

        /// <summary>The choices as stored.</summary>
        public static string Serialize(IReadOnlyDictionary<string, ToolPermission> chosen) =>
            JsonSerializer.Serialize(chosen.OrderBy(c => c.Key, StringComparer.Ordinal)
                .ToDictionary(c => c.Key, c => c.Value.ToString(), StringComparer.Ordinal));
    }
}
