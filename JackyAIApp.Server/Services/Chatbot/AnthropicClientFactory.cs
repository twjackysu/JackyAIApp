using Anthropic;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// Creates the Anthropic client a chat turn or a Files API call goes through — in one place, so a
    /// test can give the SDK an HttpClient serving recorded responses and run the real stream parsing.
    /// </summary>
    public interface IAnthropicClientFactory
    {
        AnthropicClient Create(string apiKey);
    }

    public sealed class AnthropicClientFactory : IAnthropicClientFactory
    {
        public AnthropicClient Create(string apiKey) => new() { ApiKey = apiKey };
    }
}
