using System.Reflection;
using System.Security.Claims;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services.Speech;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace JackyAIApp.Server.Tests.Services.Speech
{
    /// <summary>
    /// The anonymous text-to-speech endpoint is bounded (D-173): the clips it keeps by the bytes they hold, and its
    /// requests per address (per reader once signed in).
    /// </summary>
    public class TtsCacheTests
    {
        [Fact]
        public void AClip_IsReadBackUnderItsTextVoiceModelAndSpeed()
        {
            using var cache = new TtsCache();
            byte[] clip = [1, 2, 3];
            cache.Set("Hello.", "alloy", "tts-1", 1.0f, clip);

            Assert.True(cache.TryGet("Hello.", "alloy", "tts-1", 1.0f, out var audio));
            Assert.Equal(clip, audio);
            Assert.False(cache.TryGet("Hello.", "alloy", "tts-1", 0.5f, out _));
            Assert.False(cache.TryGet("Hello.", "echo", "tts-1", 1.0f, out _));
        }

        [Fact]
        public void AClipPastTheLimit_IsNotKept()
        {
            using var cache = new TtsCache(maxBytes: 100);
            cache.Set("first", "alloy", "tts-1", 1.0f, new byte[60]);
            cache.Set("second", "alloy", "tts-1", 1.0f, new byte[60]);

            // The second would take the cache past its bytes, so it is not kept (the first may be dropped meanwhile to
            // make room for later ones, but never both kept).
            Assert.False(cache.TryGet("second", "alloy", "tts-1", 1.0f, out _));
            Assert.True(cache.Count <= 1);
        }

        [Fact]
        public void TheLimit_LeavesRoomForAMachineThatHasOthersToRun()
        {
            Assert.Equal(128L * 1024 * 1024, TtsCache.MaxBytes);
        }

        [Fact]
        public void AVisitor_IsHeldPerAddress_AndAReader_PerAccount()
        {
            var visitor = new DefaultHttpContext();
            visitor.Request.Headers[RateLimitPolicies.CloudflareClientIpHeader] = "203.0.113.7";
            var other = new DefaultHttpContext();
            other.Request.Headers[RateLimitPolicies.CloudflareClientIpHeader] = "198.51.100.2";
            var reader = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test")),
            };
            reader.Request.Headers[RateLimitPolicies.CloudflareClientIpHeader] = "203.0.113.7";

            var partition = RateLimitPolicies.TextToSpeechPartition(visitor);
            Assert.NotEqual(partition.PartitionKey, RateLimitPolicies.TextToSpeechPartition(other).PartitionKey);
            Assert.NotEqual(partition.PartitionKey, RateLimitPolicies.TextToSpeechPartition(reader).PartitionKey);

            using (var limiter = partition.Factory(partition.PartitionKey))
            {
                for (var i = 0; i < RateLimitPolicies.TextToSpeechPermitsPerMinuteAnonymous; i++)
                    Assert.True(limiter.AttemptAcquire().IsAcquired, $"clip {i + 1}");
                Assert.False(limiter.AttemptAcquire().IsAcquired);
            }

            var signedIn = RateLimitPolicies.TextToSpeechPartition(reader);
            using (var limiter = signedIn.Factory(signedIn.PartitionKey))
            {
                for (var i = 0; i < RateLimitPolicies.TextToSpeechPermitsPerMinuteSignedIn; i++)
                    Assert.True(limiter.AttemptAcquire().IsAcquired, $"clip {i + 1}");
                Assert.False(limiter.AttemptAcquire().IsAcquired);
            }

            Assert.Equal(RateLimitPolicies.TextToSpeech,
                typeof(AudioController).GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        }
    }
}
