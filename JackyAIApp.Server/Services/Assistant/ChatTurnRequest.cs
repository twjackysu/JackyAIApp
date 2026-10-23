using Anthropic.Models.Beta.Messages;
using JackyAIApp.Server.Services.Connectors;
using AnthropicModel = Anthropic.Models.Messages.Model;

namespace JackyAIApp.Server.Services.Assistant
{
    /// <summary>
    /// The request for one turn of any assistant: the shared model settings, the profile's system
    /// prompt and background (<see cref="ChatProfileSetup"/>), and the conversation.
    /// </summary>
    public static class ChatTurnRequest
    {
        /// <summary>The usage log's model name; must name the same model as the request below.</summary>
        public const string ModelId = "claude-haiku-5-5";

        // Haiku 5.5 thinks by default (adaptive), and that thinking counts toward this cap along
        // with the MCP tool call and the analysis text. 2048 was once observed truncating
        // mid-analysis (empty final response) on requests like "分析一下2330", and thinking only
        // adds to what a turn needs. It is a ceiling, not a charge: a turn pays for what it uses.
        private const int MaxTokens = 32000;

        /// <summary>The tw-stock tools that are not deferred (<see cref="TwStockMcp.AlwaysLoadedTools"/>).</summary>
        internal static readonly string[] AlwaysLoadedTools = [.. TwStockMcp.AlwaysLoadedTools];

        /// <summary>
        /// The request for one turn. The whole conversation, attachments included, is sent again on
        /// every turn, so it is cached: the top-level cache_control puts the breakpoint on the last
        /// block, and the next turn — the same prefix with its new messages after — reads everything
        /// up to there at a tenth of the input price instead of paying for it again. The default
        /// 5-minute TTL: a reader's next message usually comes within minutes, each read restarts
        /// the 5 minutes, and a miss costs only a quarter more (the 1-hour TTL doubles every write).
        /// For the prefix to match, everything ahead of the conversation must stay byte-identical
        /// between turns: model, effort, tools and system prompt (which changes once a day, with
        /// the date, and with the profile's background whenever that is rebuilt), and each stored
        /// turn must be rebuilt exactly as it was sent.
        /// <para>
        /// The background, when there is one, is a second system block with its own breakpoint: it
        /// is the same for every reader until rebuilt, so tools and system prompt are cached once
        /// for all conversations, not each on its own.
        /// </para>
        /// </summary>
        public static MessageCreateParams Build(ChatProfileSetup setup, List<BetaMessageParam> messages)
        {
            if (!setup.AttachTwStock)
            {
                return new MessageCreateParams
                {
                    Model = AnthropicModel.ClaudeHaiku5_5,
                    MaxTokens = MaxTokens,
                    OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                    System = setup.CachedBackground == null
                        ? setup.SystemPrompt
                        : new List<BetaTextBlockParam>
                        {
                            new() { Text = setup.SystemPrompt },
                            new() { Text = setup.CachedBackground, CacheControl = new BetaCacheControlEphemeral() },
                        },
                    Messages = messages,
                    CacheControl = new BetaCacheControlEphemeral(),
                };
            }

            return new MessageCreateParams
            {
                Model = AnthropicModel.ClaudeHaiku5_5,
                MaxTokens = MaxTokens,
                // Medium is Haiku 5.5's default; set explicitly so a change of default upstream
                // cannot quietly change how much every turn thinks (and costs).
                OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                Betas = [TwStockMcp.ConnectorBeta],
                McpServers = [new BetaRequestMcpServerUrlDefinition { Name = TwStockMcp.Name, Url = TwStockMcp.Url }],
                // Regex, not BM25: BM25 found almost no tool for questions asked in Chinese. No
                // cache_control on a deferred tool (the API answers 400); the top-level breakpoint
                // below still caches the tools ahead of it.
                Tools =
                [
                    new BetaToolSearchToolRegex20251119 { Type = BetaToolSearchToolRegex20251119Type.ToolSearchToolRegex20251119 },
                    new BetaMcpToolset
                    {
                        McpServerName = TwStockMcp.Name,
                        DefaultConfig = new BetaMcpToolDefaultConfig { DeferLoading = true },
                        Configs = AlwaysLoadedTools.ToDictionary(name => name, _ => new BetaMcpToolConfig { DeferLoading = false }),
                    },
                ],
                System = setup.CachedBackground == null
                    ? setup.SystemPrompt
                    : new List<BetaTextBlockParam>
                    {
                        new() { Text = setup.SystemPrompt },
                        new() { Text = setup.CachedBackground, CacheControl = new BetaCacheControlEphemeral() },
                    },
                Messages = messages,
                CacheControl = new BetaCacheControlEphemeral(),
            };
        }
    }
}
