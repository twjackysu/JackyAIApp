using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackyAIApp.Server.Services.Connectors.SiteMcp
{
    /// <summary>
    /// The site's MCP server as an MCP App (the MCP Apps extension, <c>io.modelcontextprotocol/ui</c>; design/03 §9.2): a
    /// client that draws apps (Claude, ChatGPT) shows a tool's card — the table or link the site's own chat shows — in a
    /// sandboxed frame under the answer, instead of only its text. One app draws every card: each tool names it
    /// (<c>_meta.ui.resourceUri</c>), the result carries its card as <c>structuredContent.card</c>, and the frame gets the
    /// result from the host by postMessage. A client without the extension ignores both and reads the text as before.
    /// <para>
    /// The app is one HTML page with its script and style inline, so it needs no outside address (the host's default
    /// CSP, which allows none, is enough). It writes every value as text (<c>textContent</c>, never markup), and a link
    /// leaves only through the host (<c>ui/open-link</c>), only to the site or https.
    /// </para>
    /// </summary>
    public static class SiteMcpApp
    {
        public const string ExtensionKey = "io.modelcontextprotocol/ui";
        public const string MimeType = "text/html;profile=mcp-app";
        public const string ResourceUri = "ui://jacky-studio/card.html";

        /// <summary>The most a card may come to as JSON; a bigger one is left to the text.</summary>
        public const int MaxCardChars = 200_000;

        /// <summary>What a tool says in <c>tools/list</c> so the host mounts the app for its results.</summary>
        public static JsonObject ToolMeta() => new() { ["ui"] = new JsonObject { ["resourceUri"] = ResourceUri } };

        /// <summary>The app as <c>resources/list</c> lists it.</summary>
        public static JsonObject ResourceJson() => new()
        {
            ["uri"] = ResourceUri,
            ["name"] = "card",
            ["title"] = "Jacky Studio 卡片",
            ["description"] = "把 Jacky Studio 工具的結果畫成表格或連結卡片。",
            ["mimeType"] = MimeType,
        };

        /// <summary>The answer to <c>resources/read</c> of the app.</summary>
        public static JsonObject ReadResult() => new()
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["uri"] = ResourceUri,
                ["mimeType"] = MimeType,
                ["text"] = Html,
                ["_meta"] = new JsonObject { ["ui"] = new JsonObject { ["prefersBorder"] = true } },
            }),
        };

        /// <summary>
        /// A tool's card as the result's <c>structuredContent</c> (<c>{ "card": … }</c>): a table, or a link whose address
        /// is made whole (<see cref="SiteMcpServer.AbsoluteHref"/>). Null for another kind, a link that may not be
        /// followed, or a card over <see cref="MaxCardChars"/>.
        /// </summary>
        public static JsonObject? StructuredContent(object? card, string siteOrigin)
        {
            if (card == null || JsonSerializer.SerializeToNode(card) is not JsonObject node) return null;
            switch (StringOf(node["kind"]))
            {
                case "table":
                    if (node["columns"] is not JsonArray || node["rows"] is not JsonArray) return null;
                    break;
                case "link":
                    if (SiteMcpServer.AbsoluteHref(StringOf(node["href"]), siteOrigin) is not { } url) return null;
                    node["href"] = url;
                    break;
                default:
                    return null;
            }
            var structured = new JsonObject { ["card"] = node };
            return structured.ToJsonString().Length > MaxCardChars ? null : structured;
        }

        /// <summary>The most buttons one card offers.</summary>
        public const int MaxActions = 3;

        /// <summary>
        /// Puts a card's buttons (<see cref="SiteAppAction"/>) on its structured content as <c>actions</c>
        /// (<c>[{ tool, label, arguments }]</c>), only those whose tool is in <paramref name="offered"/>: what this token may
        /// call. The app draws them; pressing one sends <c>tools/call</c> through the host.
        /// </summary>
        public static void AddActions(JsonObject structured, IReadOnlyList<SiteAppAction> actions, IReadOnlySet<string> offered)
        {
            var allowed = new JsonArray();
            foreach (var action in actions.Where(a => offered.Contains(a.Tool)).Take(MaxActions))
                allowed.Add(new JsonObject
                {
                    ["tool"] = action.Tool,
                    ["label"] = action.Label,
                    ["arguments"] = action.Arguments.DeepClone(),
                });
            if (allowed.Count > 0) structured["actions"] = allowed;
        }

        private static string? StringOf(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        /// <summary>
        /// The app. The handshake is the extension's: <c>ui/initialize</c> (app info, capabilities, protocol version), then
        /// <c>ui/notifications/initialized</c>; the host then sends <c>ui/notifications/tool-result</c>, whose
        /// <c>structuredContent.card</c> is drawn, and the frame's height goes back as <c>ui/notifications/size-changed</c>.
        /// </summary>
        internal const string Html = """
            <!doctype html>
            <html lang="zh-Hant">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Jacky Studio</title>
            <style>
              :root { --bg: #ffffff; --fg: #1c1b1f; --muted: #5f6368; --line: #e3e3e8; --head: #f5f5f8; --accent: #5b4bdb; --accent-fg: #ffffff; }
              :root[data-theme="dark"] { --bg: #1e1e22; --fg: #ececf1; --muted: #a0a0ab; --line: #34343c; --head: #26262c; --accent: #9d92ff; --accent-fg: #15151a; }
              * { box-sizing: border-box; }
              html, body { margin: 0; background: var(--bg); color: var(--fg); font: 14px/1.5 system-ui, -apple-system, "Segoe UI", "Noto Sans TC", sans-serif; }
              #app { padding: 12px 14px; }
              .brand { font-size: 11px; color: var(--muted); letter-spacing: .02em; margin-bottom: 6px; }
              .title { font-weight: 600; font-size: 15px; margin: 0 0 8px; }
              .desc { color: var(--muted); margin: 0 0 10px; }
              .scroll { overflow-x: auto; border: 1px solid var(--line); border-radius: 8px; }
              table { border-collapse: collapse; width: 100%; }
              th, td { padding: 6px 10px; border-bottom: 1px solid var(--line); text-align: left; vertical-align: top; }
              th { background: var(--head); font-weight: 600; white-space: nowrap; }
              tr:last-child td { border-bottom: 0; }
              td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
              td.nowrap { white-space: nowrap; }
              .more { color: var(--muted); font-size: 12px; margin-top: 6px; }
              button { background: var(--accent); color: var(--accent-fg); border: 0; border-radius: 6px; padding: 6px 14px; font: inherit; font-weight: 600; cursor: pointer; }
              button.secondary { background: transparent; color: var(--accent); border: 1px solid var(--accent); }
              button:disabled { opacity: .6; cursor: default; }
              .actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 10px; align-items: center; }
              .note { color: var(--muted); font-size: 12px; }
              .hidden { display: none; }
            </style>
            </head>
            <body>
            <div id="app" class="hidden"></div>
            <script>
            (function () {
              "use strict";
              var app = document.getElementById("app");
              var nextId = 1, pending = {}, ready = false;

              function send(message) { window.parent.postMessage(message, "*"); }
              function request(method, params) {
                var id = nextId++;
                return new Promise(function (resolve, reject) {
                  pending[id] = { resolve: resolve, reject: reject };
                  send({ jsonrpc: "2.0", id: id, method: method, params: params });
                });
              }
              function notify(method, params) { if (ready) send({ jsonrpc: "2.0", method: method, params: params }); }

              function el(tag, className, text) {
                var node = document.createElement(tag);
                if (className) node.className = className;
                if (text !== undefined && text !== null) node.textContent = String(text);
                return node;
              }
              function cell(value) {
                if (value === null || value === undefined) return "—";
                if (typeof value === "number") return value.toLocaleString("zh-TW", { maximumFractionDigits: 2 });
                return String(value);
              }
              function safeUrl(href) {
                try { var url = new URL(String(href)); return url.protocol === "https:" ? url.href : null; } catch (e) { return null; }
              }

              function drawTable(card) {
                if (card.title) app.appendChild(el("p", "title", card.title));
                var columns = Array.isArray(card.columns) ? card.columns.slice(0, 12) : [];
                var rows = Array.isArray(card.rows) ? card.rows.slice(0, 200) : [];
                var wrap = el("div", "scroll"), table = el("table"), head = el("tr"), body = el("tbody");
                columns.forEach(function (c) { head.appendChild(el("th", c && c.numeric ? "num" : "", c && c.label)); });
                var thead = el("thead"); thead.appendChild(head); table.appendChild(thead);
                rows.forEach(function (row) {
                  var tr = el("tr");
                  columns.forEach(function (c) {
                    var cls = c && c.numeric ? "num" : c && c.nowrap ? "nowrap" : "";
                    tr.appendChild(el("td", cls, cell(row && c ? row[c.key] : null)));
                  });
                  body.appendChild(tr);
                });
                table.appendChild(body); wrap.appendChild(table); app.appendChild(wrap);
                var total = typeof card.totalRows === "number" ? card.totalRows : rows.length;
                if (total > rows.length) app.appendChild(el("p", "more", "共 " + total + " 筆，顯示前 " + rows.length + " 筆"));
              }

              function drawLink(card) {
                app.appendChild(el("p", "title", card.title || "Jacky Studio"));
                if (card.description) app.appendChild(el("p", "desc", card.description));
                var url = safeUrl(card.href);
                if (!url) return;
                var button = el("button", "", card.actionLabel || "在 Jacky Studio 打開");
                button.type = "button";
                button.addEventListener("click", function () { request("ui/open-link", { url: url }).catch(function () {}); });
                app.appendChild(button);
              }

              // A card's buttons: each calls one of the site's tools through the host (tools/call), the reader's press
              // being the asking. A failure's text is shown (as text); a success just says so.
              function drawActions(actions) {
                if (!Array.isArray(actions) || actions.length === 0) return;
                var row = el("div", "actions"), note = el("span", "note");
                actions.slice(0, 3).forEach(function (a) {
                  if (!a || typeof a.tool !== "string" || typeof a.label !== "string") return;
                  var args = a.arguments && typeof a.arguments === "object" ? a.arguments : {};
                  var button = el("button", "secondary", a.label);
                  button.type = "button";
                  button.addEventListener("click", function () {
                    button.disabled = true;
                    note.textContent = "處理中…";
                    request("tools/call", { name: a.tool, arguments: args }).then(function (result) {
                      var first = result && Array.isArray(result.content) && result.content[0];
                      var text = first && typeof first.text === "string" ? first.text : "";
                      if (result && result.isError) {
                        button.disabled = false;
                        note.textContent = text || "沒有成功，請再試一次。";
                      } else {
                        // The tool's text is written for a model ("…the reader's repository"); the reader gets a plain done.
                        button.textContent = "✓ " + a.label;
                        note.textContent = "完成";
                      }
                      reportSize();
                    }).catch(function () {
                      button.disabled = false;
                      note.textContent = "這裡沒辦法直接執行，請在對話裡請 AI 幫你做。";
                      reportSize();
                    });
                  });
                  row.appendChild(button);
                });
                row.appendChild(note);
                app.appendChild(row);
              }

              function draw(result) {
                var card = result && result.structuredContent && result.structuredContent.card;
                app.textContent = "";
                if (!card || result.isError || (card.kind !== "table" && card.kind !== "link")) {
                  app.className = "hidden";
                } else {
                  app.className = "";
                  app.appendChild(el("div", "brand", "Jacky Studio"));
                  if (card.kind === "table") drawTable(card); else drawLink(card);
                  drawActions(result.structuredContent.actions);
                }
                reportSize();
              }

              function theme(context) {
                if (context && (context.theme === "dark" || context.theme === "light"))
                  document.documentElement.setAttribute("data-theme", context.theme);
              }

              function reportSize() {
                var height = app.className === "hidden" ? 0 : Math.ceil(document.documentElement.scrollHeight);
                notify("ui/notifications/size-changed", { width: Math.ceil(document.documentElement.scrollWidth), height: height });
              }

              window.addEventListener("message", function (event) {
                if (event.source !== window.parent) return; // only the host speaks to the app
                var m = event.data;
                if (!m || m.jsonrpc !== "2.0") return;
                if (m.id !== undefined && m.method === undefined) {
                  var waiting = pending[m.id];
                  if (waiting) { delete pending[m.id]; if (m.error) waiting.reject(m.error); else waiting.resolve(m.result); }
                  return;
                }
                if (m.method === "ui/notifications/tool-result") draw(m.params);
                else if (m.method === "ui/notifications/host-context-changed") theme(m.params);
                else if (m.id !== undefined && (m.method === "ui/resource-teardown" || m.method === "ping"))
                  send({ jsonrpc: "2.0", id: m.id, result: {} });
              });

              if (window.ResizeObserver) new ResizeObserver(reportSize).observe(document.body);

              request("ui/initialize", {
                appInfo: { name: "jacky-studio-card", version: "1.0.0" },
                appCapabilities: { availableDisplayModes: ["inline"] },
                protocolVersion: "2026-01-26"
              }).then(function (result) {
                theme(result && result.hostContext);
                send({ jsonrpc: "2.0", method: "ui/notifications/initialized", params: {} });
                ready = true;
                reportSize();
              }).catch(function () {});
            })();
            </script>
            </body>
            </html>
            """;
    }
}
