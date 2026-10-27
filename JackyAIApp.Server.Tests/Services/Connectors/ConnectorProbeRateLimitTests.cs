using System.Reflection;
using System.Security.Claims;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>
    /// What makes the server reach a host a reader named is held per reader (D-155): a press in the add dialog or on a
    /// connector stays well inside the limit, a loop does not.
    /// </summary>
    public class ConnectorProbeRateLimitTests
    {
        private static HttpContext Reader(string userId) => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Cookies")),
        };

        [Fact]
        public void EachReaderHasTheirOwnWindow_WhichAllowsItsPermitsThenRefuses()
        {
            var alice = RateLimitPolicies.ConnectorProbePartition(Reader("alice"));
            var bob = RateLimitPolicies.ConnectorProbePartition(Reader("bob"));
            Assert.NotEqual(alice.PartitionKey, bob.PartitionKey);

            using var limiter = alice.Factory(alice.PartitionKey);
            for (var i = 0; i < RateLimitPolicies.ConnectorProbePermitsPerMinute; i++)
                Assert.True(limiter.AttemptAcquire().IsAcquired, $"permit {i + 1}");
            Assert.False(limiter.AttemptAcquire().IsAcquired);
        }

        [Fact]
        public void ChatAttachmentUploads_AreHeldPerReader_AndThePdfUnlockerPerAddress()
        {
            var alice = RateLimitPolicies.ChatAttachmentUploadPartition(Reader("alice"));
            Assert.NotEqual(alice.PartitionKey, RateLimitPolicies.ChatAttachmentUploadPartition(Reader("bob")).PartitionKey);
            using (var limiter = alice.Factory(alice.PartitionKey))
            {
                for (var i = 0; i < RateLimitPolicies.ChatAttachmentUploadPermitsPerMinute; i++)
                    Assert.True(limiter.AttemptAcquire().IsAcquired, $"upload {i + 1}");
                Assert.False(limiter.AttemptAcquire().IsAcquired);
            }

            var visitor = new DefaultHttpContext();
            visitor.Request.Headers[RateLimitPolicies.CloudflareClientIpHeader] = "203.0.113.7";
            var other = new DefaultHttpContext();
            other.Request.Headers[RateLimitPolicies.CloudflareClientIpHeader] = "198.51.100.2";
            var pdf = RateLimitPolicies.PdfUnlockPartition(visitor);
            Assert.NotEqual(pdf.PartitionKey, RateLimitPolicies.PdfUnlockPartition(other).PartitionKey);
            using (var limiter = pdf.Factory(pdf.PartitionKey))
            {
                for (var i = 0; i < RateLimitPolicies.PdfUnlockPermitsPerMinute; i++)
                    Assert.True(limiter.AttemptAcquire().IsAcquired, $"unlock {i + 1}");
                Assert.False(limiter.AttemptAcquire().IsAcquired);
            }

            Assert.Equal(RateLimitPolicies.ChatAttachmentUpload,
                typeof(ChatbotController).GetMethod(nameof(ChatbotController.UploadAttachment))!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
            Assert.Equal(RateLimitPolicies.PdfUnlock,
                typeof(PDFController).GetMethod(nameof(PDFController.UnlockPdf))!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        }

        [Fact]
        public void ADashboardsWidgetRows_AreHeldPerReader_AndForAVisitorWithoutASignInPerAddress()
        {
            // A visitor of an "Anyone" dashboard runs the owner's query on the owner's source, uncharged (D-163).
            var visitor = new DefaultHttpContext();
            visitor.Request.Headers[RateLimitPolicies.CloudflareClientIpHeader] = "203.0.113.7";
            var other = new DefaultHttpContext();
            other.Request.Headers[RateLimitPolicies.CloudflareClientIpHeader] = "198.51.100.2";
            var anonymous = RateLimitPolicies.DashboardWidgetDataPartition(visitor);
            Assert.NotEqual(anonymous.PartitionKey, RateLimitPolicies.DashboardWidgetDataPartition(other).PartitionKey);
            using (var limiter = anonymous.Factory(anonymous.PartitionKey))
            {
                for (var i = 0; i < RateLimitPolicies.DashboardWidgetDataPermitsPerMinuteAnonymous; i++)
                    Assert.True(limiter.AttemptAcquire().IsAcquired, $"widget {i + 1}");
                Assert.False(limiter.AttemptAcquire().IsAcquired);
            }

            // A signed-in reader is held by who they are, wherever they come from, with more room (their own dashboards).
            var alice = Reader("alice");
            alice.Request.Headers[RateLimitPolicies.CloudflareClientIpHeader] = "203.0.113.7";
            var signedIn = RateLimitPolicies.DashboardWidgetDataPartition(alice);
            Assert.NotEqual(anonymous.PartitionKey, signedIn.PartitionKey);
            Assert.NotEqual(signedIn.PartitionKey, RateLimitPolicies.DashboardWidgetDataPartition(Reader("bob")).PartitionKey);
            using (var limiter = signedIn.Factory(signedIn.PartitionKey))
            {
                for (var i = 0; i < RateLimitPolicies.DashboardWidgetDataPermitsPerMinuteSignedIn; i++)
                    Assert.True(limiter.AttemptAcquire().IsAcquired, $"widget {i + 1}");
                Assert.False(limiter.AttemptAcquire().IsAcquired);
            }

            Assert.Equal(RateLimitPolicies.DashboardWidgetData,
                typeof(DashboardController).GetMethod(nameof(DashboardController.WidgetData))!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        }

        [Theory]
        [InlineData(nameof(DataAnalysisController.CreateSource))]
        [InlineData(nameof(DataAnalysisController.UpdateSource))]
        public void SavingADataSource_WhichNamesAHostTheServerConnectsTo_IsHeldToItToo(string action)
        {
            var method = typeof(DataAnalysisController).GetMethod(action)!;

            Assert.Equal(RateLimitPolicies.ConnectorProbe, method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        }

        [Fact]
        public void TheConsentPagesAnswer_IsHeldPerReader()
        {
            var alice = RateLimitPolicies.SiteOAuthConsentPartition(Reader("alice"));
            Assert.NotEqual(alice.PartitionKey, RateLimitPolicies.SiteOAuthConsentPartition(Reader("bob")).PartitionKey);
            using (var limiter = alice.Factory(alice.PartitionKey))
            {
                for (var i = 0; i < RateLimitPolicies.SiteOAuthConsentPermitsPerMinute; i++)
                    Assert.True(limiter.AttemptAcquire().IsAcquired, $"permit {i + 1}");
                Assert.False(limiter.AttemptAcquire().IsAcquired);
            }
            Assert.Equal(RateLimitPolicies.SiteOAuthConsent,
                typeof(SiteOAuthController).GetMethod(nameof(SiteOAuthController.Decide))!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        }

        [Theory]
        [InlineData(nameof(AiConnectorsController.Probe))]
        [InlineData(nameof(AiConnectorsController.Create))]
        [InlineData(nameof(AiConnectorsController.Test))]
        public void TheEndpointsThatReachAServer_AreHeldToIt(string action)
        {
            var method = typeof(AiConnectorsController).GetMethod(action)!;

            Assert.Equal(RateLimitPolicies.ConnectorProbe, method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        }
    }
}
