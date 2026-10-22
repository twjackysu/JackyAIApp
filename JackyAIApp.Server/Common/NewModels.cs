namespace JackyAIApp.Server.Common
{
    public class NewModels
    {
        /// <summary>
        /// Cheapest OpenAI model (~$0.10/$0.50 per 1M tokens), replacing gpt-4.1-nano. Reasoning-family:
        /// it takes reasoning_effort and max_completion_tokens, not max_tokens.
        /// </summary>
        public const string GPT_6_LUNA = "gpt-6-luna";
    }
}
