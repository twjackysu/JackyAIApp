using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// Every feature has its one cost in <see cref="UsageCosts.Of"/>; a feature added without one
    /// would throw the first time it is charged, so this catches it first.
    /// </summary>
    public class UsageCostsTests
    {
        public static TheoryData<UsageFeature> AllFeatures => [.. Enum.GetValues<UsageFeature>()];

        [Theory]
        [MemberData(nameof(AllFeatures))]
        public void EveryFeature_HasAPositiveCost(UsageFeature feature)
        {
            Assert.True(UsageCosts.Of(feature) > 0);
        }

        [Fact]
        public void ChatMessage_AddsEachAttachment_AndPdfsPerTenPages()
        {
            var message = UsageCosts.Of(UsageFeature.ChatMessage);

            Assert.Equal(message, UsageCosts.ForChatMessage([]));
            // A text file, then a 25-page PDF (3 blocks of 10 pages, rounded up).
            Assert.Equal(
                message + UsageCosts.ChatAttachment + UsageCosts.ChatAttachment + 3 * UsageCosts.ChatPdfPer10Pages,
                UsageCosts.ForChatMessage([null, 25]));
        }
    }
}
