using System.Text.Json;
using System.Text.RegularExpressions;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// Finding a remote MCP server in the official MCP Registry (registry.modelcontextprotocol.io, its
    /// <c>GET /v0.1/servers</c>; docs/ai-native/design/03 §6.1, D-122), so the reader does not have to know
    /// a server's URL to add it. Only a pointer: choosing one puts its URL in the add dialog, which probes
    /// and adds it as any server of the reader's own (<see cref="IConnectorInstallService"/>, every request
    /// through <see cref="SafeHttp"/>). Nothing of an entry reaches the AI.
    /// </summary>
    public interface IMcpRegistry
    {
        /// <summary>
        /// The registry's latest, active servers whose name holds <paramref name="search"/> (the registry
        /// matches names only; empty lists from the start) and that can be reached the way the site
        /// connects: Streamable HTTP at a fixed https URL. Free, cached for every reader.
        /// </summary>
        Task<ServiceResult<McpRegistrySearchDto>> SearchAsync(string? search, CancellationToken ct);
    }

    public sealed partial class McpRegistry(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<McpRegistry> logger)
        : IMcpRegistry
    {
        public const string HttpClientName = "ExternalReadOnly";
        public const string BaseUrl = "https://registry.modelcontextprotocol.io/v0.1/servers";

        /// <summary>Longest search kept; the registry's names are at most 200 characters.</summary>
        public const int MaxSearchLength = 50;

        /// <summary>How many entries one answer shows (the registry is asked for <see cref="PageSize"/>).</summary>
        public const int MaxResults = 30;

        public const int PageSize = 100;
        public const int MaxTitleLength = 100;
        public const int MaxDescriptionLength = 200;
        public const int MaxRequiredHeaders = 3;

        /// <summary>A registry answer larger than this is not read (a page of 100 is about 200KB).</summary>
        public const long MaxBytes = 2 * 1024 * 1024;

        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
        private const string OfficialMeta = "io.modelcontextprotocol.registry/official";

        public async Task<ServiceResult<McpRegistrySearchDto>> SearchAsync(string? search, CancellationToken ct)
        {
            var query = CleanSearch(search);
            var found = await cache.GetOrCreateSingleFlightAsync($"mcp-registry:{query.ToLowerInvariant()}", Ttl,
                () => FetchAsync(query), ct);
            return found == null
                ? ServiceResult.Fail(ErrorCodes.ExternalApiError, "MCP 官方目錄暫時讀不到，請稍後再試，或直接貼上網址。")
                : ServiceResult.Ok(found);
        }

        /// <summary>The search as the registry is asked it: one line, trimmed, at most <see cref="MaxSearchLength"/>.</summary>
        internal static string CleanSearch(string? search)
        {
            var text = new string((search ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            return text.Length > MaxSearchLength ? text[..MaxSearchLength].Trim() : text;
        }

        internal static string UrlFor(string query) =>
            $"{BaseUrl}?version=latest&limit={PageSize}" + (query.Length == 0 ? "" : $"&search={Uri.EscapeDataString(query)}");

        private async Task<McpRegistrySearchDto?> FetchAsync(string query)
        {
            // Shared by every reader asking the same thing (single flight), so no reader's token stops it.
            using var timeout = new CancellationTokenSource(Timeout);
            try
            {
                var http = httpClientFactory.CreateClient(HttpClientName);
                using var response = await http.GetAsync(UrlFor(query), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("MCP registry answered {Status} for {Query}", (int)response.StatusCode, query);
                    return null;
                }
                await response.Content.LoadIntoBufferAsync(MaxBytes, timeout.Token);
                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var doc = await JsonDocument.ParseAsync(body, default, timeout.Token);
                return Parse(doc.RootElement);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or IOException)
            {
                logger.LogWarning(ex, "MCP registry could not be read for {Query}", query);
                return null;
            }
        }

        /// <summary>
        /// The entries of one registry page the site can add, read leniently: the registry holds what anyone
        /// published, so a field of the wrong type is skipped, text is cut and kept plain, and only https links
        /// are kept.
        /// </summary>
        internal static McpRegistrySearchDto Parse(JsonElement root)
        {
            var servers = new List<McpRegistryServerDto>();
            var urls = new HashSet<string>(StringComparer.Ordinal);
            var builtIns = BuiltInMcpConnectors.All
                .Select(d => (d.Key, Url: McpServerUrl.Normalize(d.Url).Url?.ToString()))
                .Where(d => d.Url != null)
                .ToList();
            var skippedMore = false;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("servers", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (Entry(item) is not { } entry || !urls.Add(entry.Url)) continue;
                    if (servers.Count == MaxResults)
                    {
                        skippedMore = true;
                        break;
                    }
                    servers.Add(entry with { CatalogKey = builtIns.FirstOrDefault(b => b.Url == entry.Url).Key });
                }
            }

            var nextCursor = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("metadata", out var meta) &&
                             meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("nextCursor", out var cursor) &&
                             cursor.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(cursor.GetString());
            return new McpRegistrySearchDto(servers, More: skippedMore || nextCursor);
        }

        private static McpRegistryServerDto? Entry(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("server", out var server) ||
                server.ValueKind != JsonValueKind.Object)
                return null;
            if (item.TryGetProperty("_meta", out var meta) && meta.ValueKind == JsonValueKind.Object &&
                meta.TryGetProperty(OfficialMeta, out var official) && official.ValueKind == JsonValueKind.Object &&
                Text(official, "status") is { } status && status != "active")
                return null;

            var name = Text(server, "name");
            if (name == null || !ServerName().IsMatch(name)) return null;
            if (RemoteOf(server) is not { } remote) return null;

            var title = Plain(Text(server, "title"), MaxTitleLength) ?? name[(name.IndexOf('/') + 1)..];
            var description = Plain(Text(server, "description"), MaxDescriptionLength) ?? "";
            var repository = server.TryGetProperty("repository", out var repo) && repo.ValueKind == JsonValueKind.Object
                ? HttpsLink(Text(repo, "url"))
                : null;
            return new McpRegistryServerDto(name, title, description, remote.Url, HttpsLink(Text(server, "websiteUrl")),
                repository, Plain(Text(server, "version"), 40), remote.RequiredHeaders, CatalogKey: null);
        }

        /// <summary>
        /// The first remote the site can connect to: Streamable HTTP (the site's MCP client speaks only that) at
        /// a URL with no template variable, in the canonical form an installation keeps. Its required headers are
        /// what the reader will have to fill in (an API key's header, or Authorization for a token).
        /// </summary>
        private static (string Url, IReadOnlyList<string> RequiredHeaders)? RemoteOf(JsonElement server)
        {
            if (!server.TryGetProperty("remotes", out var remotes) || remotes.ValueKind != JsonValueKind.Array) return null;
            foreach (var remote in remotes.EnumerateArray())
            {
                if (remote.ValueKind != JsonValueKind.Object || Text(remote, "type") != "streamable-http") continue;
                var raw = Text(remote, "url");
                if (raw == null || raw.Contains('{')) continue;
                var (url, _) = McpServerUrl.Normalize(raw);
                if (url == null) continue;

                var headers = new List<string>();
                if (remote.TryGetProperty("headers", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var header in list.EnumerateArray())
                    {
                        if (header.ValueKind != JsonValueKind.Object ||
                            !(header.TryGetProperty("isRequired", out var required) && required.ValueKind == JsonValueKind.True))
                            continue;
                        if (Text(header, "name") is { } headerName && HeaderName().IsMatch(headerName) &&
                            !headers.Contains(headerName, StringComparer.OrdinalIgnoreCase) && headers.Count < MaxRequiredHeaders)
                            headers.Add(headerName);
                    }
                }
                return (url.ToString(), headers);
            }
            return null;
        }

        private static string? Text(JsonElement obj, string property) =>
            obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        /// <summary>One line of plain text, cut to <paramref name="max"/>; null when nothing is left.</summary>
        internal static string? Plain(string? text, int max)
        {
            if (text == null) return null;
            var line = Whitespace().Replace(new string(text.Select(c => char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c)
                .Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray()), " ").Trim();
            if (line.Length == 0) return null;
            return line.Length > max ? line[..(max - 1)].TrimEnd() + "…" : line;
        }

        private static string? HttpsLink(string? text) =>
            text != null && text.Length <= McpServerUrl.MaxLength && Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
                ? uri.AbsoluteUri
                : null;

        // The registry's own rule for a name: reverse-DNS namespace, one slash, a name.
        [GeneratedRegex("^[a-zA-Z0-9.-]{1,150}/[a-zA-Z0-9._-]{1,150}$")]
        private static partial Regex ServerName();

        // The same header names the add dialog accepts for an API key.
        [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
        private static partial Regex HeaderName();

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();
    }
}
