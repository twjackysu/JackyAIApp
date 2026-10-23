using System.Security.Cryptography;
using System.Text;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// The name a connector's tool goes by in a request to the model. The API takes
    /// <c>^[a-zA-Z0-9_-]{1,64}$</c>; a connector's own tool names are its server's choice, so each is
    /// prefixed with its connector's short key — two connectors may both have a <c>search</c>, and an
    /// outside server must not pass its tool off as one of the site's own — with anything outside
    /// the allowed characters replaced, and a name still too long cut and ended with a hash of the
    /// whole, so it stays unique. The caller keeps the map back to the connector and its tool.
    /// </summary>
    public static class ToolNames
    {
        public const int MaxLength = 64;
        private const string Separator = "__";
        private const int HashLength = 6;

        /// <param name="connectorKey">The connector's short key, itself plain (e.g. <c>c3</c>, <c>site</c>).</param>
        /// <param name="toolName">The tool's name as its connector gives it.</param>
        public static string ForModel(string connectorKey, string toolName)
        {
            var name = $"{Sanitize(connectorKey)}{Separator}{Sanitize(toolName)}";
            if (name.Length <= MaxLength) return name;

            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{connectorKey}\n{toolName}")))[..HashLength];
            return $"{name[..(MaxLength - HashLength - 1)]}_{hash}";
        }

        /// <summary>True when the API would take this name as it is.</summary>
        public static bool IsValid(string name) =>
            name.Length is > 0 and <= MaxLength && name.All(IsAllowed);

        private static string Sanitize(string text)
        {
            var chars = text.Select(c => IsAllowed(c) ? c : '_').ToArray();
            return chars.Length == 0 ? "_" : new string(chars);
        }

        private static bool IsAllowed(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-';
    }
}
