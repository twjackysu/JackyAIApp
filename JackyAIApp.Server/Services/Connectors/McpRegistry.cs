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
        /// The registry's latest, active servers that can be reached the way the site connects (Streamable
        /// HTTP at a fixed https URL) whose name, title or description holds <paramref name="search"/>
        /// (empty: from the start). Free; the registry is read once for every reader and every search.
        /// </summary>
        Task<ServiceResult<McpRegistrySearchDto>> SearchAsync(string? search, CancellationToken ct);
    }

    public sealed partial class McpRegistry(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<McpRegistry> logger)
        : IMcpRegistry
    {
        public const string HttpClientName = "ExternalReadOnly";
        public const string BaseUrl = "https://registry.modelcontextprotocol.io/v0.1/servers";

        /// <summary>Longest search kept.</summary>
        public const int MaxSearchLength = 50;

        /// <summary>How many entries one answer shows.</summary>
        public const int MaxResults = 30;

        public const int PageSize = 100;

        /// <summary>
        /// Most registry pages read in one walk (4000 servers): most of the registry is local (stdio) servers,
        /// so the remote ones are only found by reading it whole.
        /// </summary>
        public const int MaxPages = 40;

        public const int MaxTitleLength = 100;
        public const int MaxDescriptionLength = 200;
        public const int MaxRequiredHeaders = 3;

        /// <summary>A registry page larger than this is not read (a page of 100 is about 200KB).</summary>
        public const long MaxBytes = 2 * 1024 * 1024;

        private const string CacheKey = "mcp-registry:remote-servers";
        private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
        private static readonly TimeSpan WalkTimeout = TimeSpan.FromSeconds(60);
        private const string OfficialMeta = "io.modelcontextprotocol.registry/official";

        public async Task<ServiceResult<McpRegistrySearchDto>> SearchAsync(string? search, CancellationToken ct)
        {
            // One list for every search: a search is a filter of it, so no reader's typing reaches the registry.
            var all = await cache.GetOrCreateSingleFlightAsync(CacheKey, Ttl, FetchAllAsync, ct);
            return all == null
                ? ServiceResult.Fail(ErrorCodes.ExternalApiError, "MCP 官方目錄暫時讀不到，請稍後再試，或直接貼上網址。")
                : ServiceResult.Ok(Search(all, CleanSearch(search)));
        }

        /// <summary>The search as it is matched: one line, trimmed, at most <see cref="MaxSearchLength"/>.</summary>
        internal static string CleanSearch(string? search)
        {
            var text = new string((search ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            return text.Length > MaxSearchLength ? text[..MaxSearchLength].Trim() : text;
        }

        /// <summary>
        /// The servers whose name, title or description holds every word of <paramref name="query"/> (any case),
        /// those matching by name or title first, at most <see cref="MaxResults"/>.
        /// </summary>
        internal static McpRegistrySearchDto Search(IReadOnlyList<McpRegistryServerDto> all, string query)
        {
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool Holds(string text) => words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
            var matches = all
                .Select(s => (Server: s, ByName: Holds(s.Name + " " + s.Title), Any: Holds(s.Name + " " + s.Title + " " + s.Description)))
                .Where(m => m.Any)
                .OrderByDescending(m => m.ByName) // stable: the registry's order within each
                .Select(m => m.Server)
                .ToList();
            return new McpRegistrySearchDto(matches.Take(MaxResults).ToList(), More: matches.Count > MaxResults);
        }

        internal static string UrlFor(string? cursor) =>
            $"{BaseUrl}?version=latest&limit={PageSize}" + (string.IsNullOrEmpty(cursor) ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");

        /// <summary>
        /// Every remote server the registry lists, page by page; null when not even its first page can be read.
        /// A walk cut short later keeps what it read (logged), so one slow page does not empty the list.
        /// </summary>
        private async Task<IReadOnlyList<McpRegistryServerDto>?> FetchAllAsync()
        {
            // Shared by every reader (single flight), so no reader's token stops it.
            using var timeout = new CancellationTokenSource(WalkTimeout);
            var servers = new List<McpRegistryServerDto>();
            var urls = new HashSet<string>(StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            for (var page = 0; page < MaxPages; page++)
            {
                RegistryPage read;
                try
                {
                    read = await FetchPageAsync(cursor, timeout.Token);
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or IOException)
                {
                    logger.LogWarning(ex, "MCP registry page {Page} could not be read", page + 1);
                    return page == 0 ? null : servers;
                }
                servers.AddRange(read.Servers.Where(s => urls.Add(s.Url)));
                // A cursor seen before would walk in a circle.
                if (read.NextCursor == null || !cursors.Add(read.NextCursor)) return servers;
                cursor = read.NextCursor;
            }
            logger.LogWarning("MCP registry has more than {Pages} pages; the rest is not listed", MaxPages);
            return servers;
        }

        private async Task<RegistryPage> FetchPageAsync(string? cursor, CancellationToken ct)
        {
            var http = httpClientFactory.CreateClient(HttpClientName);
            using var response = await http.GetAsync(UrlFor(cursor), HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"MCP registry answered {(int)response.StatusCode}");
            await response.Content.LoadIntoBufferAsync(MaxBytes, ct);
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(body, default, ct);
            return Parse(doc.RootElement);
        }

        /// <summary>One registry page: the servers on it the site can add, and the cursor of the next (null at the end).</summary>
        internal sealed record RegistryPage(IReadOnlyList<McpRegistryServerDto> Servers, string? NextCursor);

        /// <summary>
        /// The entries of one registry page the site can add, read leniently: the registry holds what anyone
        /// published, so a field of the wrong type is skipped, text is cut and kept plain, and only https links
        /// are kept.
        /// </summary>
        internal static RegistryPage Parse(JsonElement root)
        {
            var servers = new List<McpRegistryServerDto>();
            var builtIns = BuiltInMcpConnectors.All
                .Select(d => (d.Key, Url: McpServerUrl.Normalize(d.Url).Url?.ToString()))
                .Where(d => d.Url != null)
                .ToList();

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("servers", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (Entry(item) is { } entry)
                        servers.Add(entry with { CatalogKey = builtIns.FirstOrDefault(b => b.Url == entry.Url).Key });
                }
            }

            string? nextCursor = null;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("metadata", out var meta) &&
                meta.ValueKind == JsonValueKind.Object && Text(meta, "nextCursor") is { Length: > 0 and <= 1000 } next)
                nextCursor = next;
            return new RegistryPage(servers, nextCursor);
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
