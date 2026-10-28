namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// An MCP server's URL as a user typed it, checked and put in the canonical form the MCP
    /// authorization spec uses as the server's identity (RFC 8707's <c>resource</c>): https only,
    /// lower-case scheme and host, no default port, no fragment, no user info, no trailing slash
    /// unless the path is only "/". Whether the host is a public address is checked when connecting
    /// (<see cref="SafeHttp"/>), since a name can resolve differently later.
    /// </summary>
    public static class McpServerUrl
    {
        /// <summary>Longest URL kept (the column's size).</summary>
        public const int MaxLength = 500;

        /// <summary>
        /// The text two addresses of the same server share (the canonical URL), or null for one that cannot be used:
        /// to tell whether a server the registry lists is one the reader added (D-198).
        /// </summary>
        public static string? Key(string? input) => Normalize(input).Url?.AbsoluteUri;

        /// <summary>The canonical URL, or null with the reason (in Chinese, shown to the user) when it cannot be used.</summary>
        public static (Uri? Url, string? Error) Normalize(string? input)
        {
            var text = input?.Trim();
            if (string.IsNullOrEmpty(text)) return (null, "請輸入 MCP server 的網址。");
            if (text.Length > MaxLength) return (null, $"網址太長（上限 {MaxLength} 個字元）。");
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return (null, "網址格式不正確。");
            if (uri.Scheme != Uri.UriSchemeHttps) return (null, "只支援 https:// 開頭的網址。");
            if (!string.IsNullOrEmpty(uri.UserInfo)) return (null, "網址不能包含帳號密碼，請改用認證設定。");
            if (uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
                return (null, "網址的主機名稱不正確。");

            var builder = new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttps,
                Host = uri.Host.ToLowerInvariant(),
                Port = uri.IsDefaultPort ? -1 : uri.Port,
                Fragment = string.Empty,
            };
            var path = builder.Path;
            if (path.Length > 1 && path.EndsWith('/')) builder.Path = path.TrimEnd('/');

            var canonical = builder.Uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);
            if (canonical.EndsWith('/') && builder.Uri.AbsolutePath == "/" && string.IsNullOrEmpty(builder.Uri.Query))
                canonical = canonical.TrimEnd('/');
            return (new Uri(canonical), null);
        }
    }
}
