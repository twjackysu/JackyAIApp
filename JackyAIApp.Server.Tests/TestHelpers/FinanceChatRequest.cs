using Anthropic.Models.Beta.Messages;
using JackyAIApp.Server.Services.Assistant;
using JackyAIApp.Server.Services.Finance.Assistant;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>The request the Finance assistant sends for a turn: the shared engine's request with Finance's profile.</summary>
    internal static class FinanceChatRequest
    {
        public static MessageCreateParams Build(
            DateOnly taipeiToday, List<BetaMessageParam> messages, string? marketBackground = null) =>
            ChatTurnRequest.Build(FinanceChatProfile.SetupFor(taipeiToday, marketBackground), messages);
    }
}
