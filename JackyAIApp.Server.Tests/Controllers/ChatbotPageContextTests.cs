using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>What of a chat message's page context reaches the assistant: short entries, a few of them.</summary>
    public class ChatbotPageContextTests
    {
        [Fact]
        public void AMessageUpToTheLimit_IsTaken_AndALongerOrEmptyOneRefused()
        {
            Assert.Null(ChatbotController.MessageRefusal(new ChatRequest { Message = new string('字', ChatbotController.MaxMessageLength) }));
            Assert.Equal("Message is too long.",
                ChatbotController.MessageRefusal(new ChatRequest { Message = new string('字', ChatbotController.MaxMessageLength + 1) }));
            Assert.NotNull(ChatbotController.MessageRefusal(new ChatRequest { Message = "" }));
            // The Messages API refuses a text block of white space only, so every later turn would fail.
            Assert.NotNull(ChatbotController.MessageRefusal(new ChatRequest { Message = " \n\t " }));
            Assert.NotNull(ChatbotController.MessageRefusal(null));
        }

        [Fact]
        public void NoContext_IsAnEmptyOne()
        {
            Assert.Empty(ChatbotController.PageContext(null));
        }

        [Fact]
        public void LongOrEmptyEntries_AreDropped_AndAtMostEightKept()
        {
            var context = new Dictionary<string, string>
            {
                ["sourceId"] = "11111111-2222-3333-4444-555555555555",
                [""] = "no key",
                [new string('k', 41)] = "long key",
                ["long"] = new string('v', 201),
            };
            for (var i = 0; i < 10; i++) context[$"k{i}"] = "v";

            var page = ChatbotController.PageContext(context);

            Assert.Equal(8, page.Count);
            Assert.Equal("11111111-2222-3333-4444-555555555555", page["sourceId"]);
            Assert.False(page.ContainsKey("long"));
            Assert.False(page.ContainsKey(""));
        }
    }
}
