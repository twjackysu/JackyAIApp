using System.Runtime.CompilerServices;
using Anthropic.Exceptions;
using JackyAIApp.Server.Services.Chatbot;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>
    /// What the browser is told when a call to the Anthropic API fails. Only a code goes out, and
    /// the code decides what the reader is asked to do: start a new conversation, send less, wait,
    /// or nothing at all (the site's problem).
    /// </summary>
    public class ChatErrorCodesTests
    {
        // The SDK's exceptions are only ever built by the SDK; the classification only looks at
        // their type, so an instance without a constructor run is enough to test it.
        private static T Thrown<T>() where T : Exception => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        [Fact]
        public void RateLimitsOverloadServerErrorsAndNetworkFailures_AreWorthTryingAgainLater()
        {
            Assert.Equal(ChatErrorCodes.Busy, ChatErrorCodes.For(Thrown<AnthropicRateLimitException>()));
            Assert.Equal(ChatErrorCodes.Busy, ChatErrorCodes.For(Thrown<Anthropic5xxException>()));
            Assert.Equal(ChatErrorCodes.Busy, ChatErrorCodes.For(Thrown<AnthropicIOException>()));
            Assert.Equal(ChatErrorCodes.Busy, ChatErrorCodes.For(new HttpRequestException("reset")));
            Assert.Equal(ChatErrorCodes.Busy, ChatErrorCodes.ForStatus(529, "Overloaded"));
        }

        [Fact]
        public void AKeyOrAccountProblem_IsNothingTheReaderCanFix()
        {
            Assert.Equal(ChatErrorCodes.Unavailable, ChatErrorCodes.For(Thrown<AnthropicUnauthorizedException>()));
            Assert.Equal(ChatErrorCodes.Unavailable, ChatErrorCodes.For(Thrown<AnthropicForbiddenException>()));
            Assert.Equal(ChatErrorCodes.Unavailable, ChatErrorCodes.For(Thrown<AnthropicNotFoundException>()));
            Assert.Equal(ChatErrorCodes.Unavailable, ChatErrorCodes.ForStatus(402, "billing_error"));
            Assert.Equal(ChatErrorCodes.Unavailable,
                ChatErrorCodes.ForStatus(400, "Your credit balance is too low to access the Anthropic API."));
        }

        [Fact]
        public void APromptOverTheContextWindow_AsksForANewConversation()
        {
            Assert.Equal(ChatErrorCodes.ContextTooLong,
                ChatErrorCodes.ForStatus(400, "prompt is too long: 1048937 tokens > 1000000 maximum"));
        }

        [Fact]
        public void ARequestOverTheSizeLimit_AsksToSendLess()
        {
            Assert.Equal(ChatErrorCodes.RequestTooLarge, ChatErrorCodes.ForStatus(413, "Request exceeds the maximum allowed number of bytes."));
        }

        [Fact]
        public void AnythingElse_IsAPlainFailure()
        {
            Assert.Equal(ChatErrorCodes.Failed, ChatErrorCodes.ForStatus(400, "messages: roles must alternate"));
            Assert.Equal(ChatErrorCodes.Failed, ChatErrorCodes.ForStatus(400, null));
            Assert.Equal(ChatErrorCodes.Failed, ChatErrorCodes.For(new InvalidOperationException()));
        }
    }
}
