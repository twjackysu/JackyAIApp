using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JackyAIApp.Server.Common;
using Microsoft.Extensions.Options;
using static JackyAIApp.Server.Services.Connectors.Atlassian.JiraIssuesTool;

namespace JackyAIApp.Server.Services.Connectors.Atlassian
{
    /// <summary>
    /// A comment on one of the reader's Jira issues, in their name, through the same Atlassian connection as
    /// <see cref="JiraIssuesTool"/> (scope <c>write:jira-work</c>, which the connection already asks for): Studio AI's
    /// first write to an outside service. A write, so the reader is asked first, and the card says the issue and the whole
    /// comment (<see cref="IDescribesCall"/>); the comment is posted exactly as asked — too long is refused, never cut — so
    /// what was confirmed is what colleagues read. A conversation that has read Jira is tainted, so this always asks there,
    /// whatever its permission (a summary someone else wrote cannot have it post on its own).
    /// </summary>
    public sealed class JiraAddCommentTool(
        IConnectorService connections, IHttpClientFactory httpClientFactory, ILogger<JiraAddCommentTool> logger) : ISiteTool, IDescribesCall
    {
        public const string Name = "jira_add_comment";

        /// <summary>
        /// The client posting the comment: the app's default (its timeout), never <see cref="JiraIssuesTool.HttpClientName"/>,
        /// whose retries could post a comment twice.
        /// </summary>
        public static readonly string WriteHttpClientName = Options.DefaultName;

        /// <summary>The longest comment taken.</summary>
        internal const int MaxComment = 2000;

        /// <summary>The scope that lets a site's issues be commented on.</summary>
        internal const string WriteScope = "write:jira-work";

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "Jira 留言",
            "用使用者的名義在他 Jira（Atlassian 帳號連線）的一張 issue 留言，同事都看得到。使用者要你在某張票留言、回覆、更新進度時使用；" +
            $"issueKey 是票號（例如 WEB-12），comment 是留言全文（最多 {MaxComment} 字，可以換行），用使用者的口吻、他要的語言寫。" +
            "票號不確定時先用 jira_issues 找，不要猜。執行前會請使用者確認留言全文。",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    issueKey = new { type = "string", description = "Jira 票號，例如 WEB-12" },
                    comment = new { type = "string", description = $"留言全文，最多 {MaxComment} 字" },
                },
                required = new[] { "issueKey", "comment" },
            }),
            ToolEffect.Write);

        public AiToolDescriptor Descriptor => Definition;

        public string? Describe(JsonElement input, AiToolContext context) =>
            JiraComment.Of(input) is { } c ? $"用你的名義在 Jira 的 {c.IssueKey} 留言：「{c.Text.ReplaceLineEndings(" ")}」" : null;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            if (JiraComment.Of(input) is not { } comment)
                return AiToolResult.Error(
                    $"要給 issueKey（Jira 票號，例如 WEB-12）與 comment（留言全文，1 到 {MaxComment} 字），所以沒有留言。太長就請使用者縮短，不要自己截斷。");

            var token = await connections.GetAccessTokenAsync(context.UserId, ConnectorProviders.Atlassian);
            if (string.IsNullOrEmpty(token))
                return new AiToolResult(
                    "使用者還沒有連線 Atlassian 帳號（或連線已失效），所以沒有留言。請告訴使用者到 Connectors 頁面的「帳號連線」連線 Atlassian，再試一次。",
                    IsError: true,
                    Card: ConnectCard);

            try
            {
                using var resources = await GetAsync(httpClientFactory.CreateClient(HttpClientName), ResourcesUrl, token, ct);
                if (Refused(resources)) return RefusedResult;
                if (!resources.IsSuccessStatusCode)
                {
                    logger.LogWarning("Atlassian accessible resources answered {Status}", (int)resources.StatusCode);
                    return Unreachable;
                }
                using var sites = await JsonDocument.ParseAsync(await resources.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (JiraSite.FirstOf(sites.RootElement, WriteScope) is not { } site)
                    return new AiToolResult(
                        "這個 Atlassian 帳號連線沒有可以留言的 Jira 網站（沒有 Jira，或連線時沒有授權寫入 Jira），所以沒有留言。請告訴使用者到 Connectors 中斷再重新連線 Atlassian。",
                        IsError: true,
                        Card: ConnectCard);

                using var request = new HttpRequestMessage(HttpMethod.Post, site.CommentUrl(comment.IssueKey))
                {
                    Content = new StringContent(JsonSerializer.Serialize(comment.Body()), Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var posted = await httpClientFactory.CreateClient(WriteHttpClientName).SendAsync(request, ct);
                if (Refused(posted)) return RefusedResult;
                if (posted.StatusCode == HttpStatusCode.NotFound)
                    return AiToolResult.Error($"Jira（{site.Name}）找不到 {comment.IssueKey}，或使用者沒有在這張票留言的權限，所以沒有留言。請跟使用者確認票號（可以先用 jira_issues 找）。");
                if (posted.StatusCode == HttpStatusCode.BadRequest)
                    return AiToolResult.Error($"Jira 不接受這則留言，所以沒有留言。請告訴使用者，必要時請他到 Jira 自己留言：{site.BrowseUrl(comment.IssueKey)}");
                if (!posted.IsSuccessStatusCode)
                {
                    logger.LogWarning("Jira comment answered {Status}", (int)posted.StatusCode);
                    // Not necessarily not posted (a 5xx after the write): say so, so it is not simply asked again.
                    return AiToolResult.Error($"Jira 沒有確認留言成功（可能沒有留上），請告訴使用者到 Jira 看一下再決定要不要重試：{site.BrowseUrl(comment.IssueKey)}");
                }

                using var body = await JsonDocument.ParseAsync(await posted.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var link = site.CommentLink(comment.IssueKey, CommentIdOf(body.RootElement));
                return new AiToolResult(
                    $"已用使用者的名義在 Jira（{site.Name}）的 {comment.IssueKey} 留言（{comment.Text.Length} 字）：{link}",
                    Card: new
                    {
                        kind = "link",
                        title = $"已在 {comment.IssueKey} 留言",
                        href = link,
                        description = Preview(comment.Text),
                        actionLabel = "在 Jira 打開",
                    });
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Jira comment could not be posted");
                // A timeout may come after Jira wrote it: never say it was not posted.
                return AiToolResult.Error("連不上 Jira 或沒有收到回應（留言可能沒有留上），請告訴使用者到 Jira 看一下再決定要不要重試。");
            }
        }

        private static AiToolResult RefusedResult => new(
            "Atlassian 不接受目前的連線（可能沒有 Jira 的寫入權限或已過期），所以沒有留言。請告訴使用者到 Connectors 頁面中斷再重新連線 Atlassian。",
            IsError: true,
            Card: ConnectCard);

        private static AiToolResult Unreachable => AiToolResult.Error("Jira 暫時連不上，所以沒有留言，請告訴使用者稍後再試。");

        /// <summary>The new comment's id from Jira's answer, only when it is digits (it goes into a link).</summary>
        internal static string? CommentIdOf(JsonElement answer) =>
            Text(answer, "id") is { } id && id.Length is > 0 and <= 20 && id.All(char.IsAsciiDigit) ? id : null;

        /// <summary>The comment's first line, cut, for the card.</summary>
        internal static string Preview(string text)
        {
            var line = text.ReplaceLineEndings(" ");
            return line.Length > 120 ? CutAt(line, 120) + "…" : line;
        }
    }

    /// <summary>What one <see cref="JiraAddCommentTool"/> call asks for: the issue, and the comment exactly as it will be posted.</summary>
    internal sealed record JiraComment(string IssueKey, string Text)
    {
        public static JiraComment? Of(JsonElement input)
        {
            if (input.ValueKind != JsonValueKind.Object) return null;
            var key = JiraIssuesTool.Text(input, "issueKey")?.Trim().ToUpperInvariant();
            var text = JiraIssuesTool.Text(input, "comment")?.Trim();
            if (key == null || !JiraIssuesTool.IsIssueKey(key)) return null;
            if (string.IsNullOrEmpty(text) || text.Length > JiraAddCommentTool.MaxComment) return null;
            return new JiraComment(key, text);
        }

        /// <summary>
        /// The comment in Jira's document format (API v3 takes nothing else): one paragraph a line, blank lines dropped (a
        /// text node may not be empty). Plain text only — no marks or links are made of what the model wrote.
        /// </summary>
        public object Body() => new
        {
            body = new
            {
                type = "doc",
                version = 1,
                content = Text.ReplaceLineEndings("\n").Split('\n')
                    .Select(line => line.TrimEnd())
                    .Where(line => line.Length > 0)
                    .Select(line => new { type = "paragraph", content = new[] { new { type = "text", text = line } } })
                    .ToArray(),
            },
        };
    }
}
