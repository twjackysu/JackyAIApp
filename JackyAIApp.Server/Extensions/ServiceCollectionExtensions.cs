using DotnetSdkUtilities.Factory.ResponseFactory;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.AiUsage;
using JackyAIApp.Server.Services.Chatbot;
using JackyAIApp.Server.Services.DailyChallenge;
using JackyAIApp.Server.Services.Dictionary;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Exam.Generators;
using JackyAIApp.Server.Services.Job;
using JackyAIApp.Server.Services.Job.Providers;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Prompt;
using JackyAIApp.Server.Services.Repository;
using JackyAIApp.Server.Services.SpacedRepetition;
using JackyAIApp.Server.Services.Speech;
using JackyAIApp.Server.Services.SpellChecker;
using JackyAIApp.Server.Services.WordOfTheDay;

namespace JackyAIApp.Server.Extensions
{
    /// <summary>
    /// Feature-grouped DI registrations to keep Program.cs focused on host/pipeline setup.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Cross-cutting services used across most feature modules: response
        /// factories, current-user access, the floating chatbot's direct
        /// Claude client, and the prompt-file-backed OpenAI wrapper.
        /// </summary>
        public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpContextAccessor();
            services.AddScoped<IApiResponseFactory, ApiResponseFactory>();
            services.AddScoped<IMyResponseFactory, ResponseFactory>();
            services.AddScoped<ICurrentUser, CurrentUser>();
            services.AddScoped<IAdminAccess, AdminAccess>();
            services.AddScoped<IUserAccountService, UserAccountService>();
            services.Configure<UsageLimitOptions>(configuration.GetSection(UsageLimitOptions.SectionName));
            services.AddScoped<UsageLedger>();
            services.AddScoped<IUsageLimitService, UsageLimitService>();
            services.AddScoped<IAiUsageLogger, AiUsageLogger>();
            services.AddSingleton<IAnthropicClientFactory, AnthropicClientFactory>();
            services.AddScoped<IAnthropicChatService, AnthropicChatService>();
            services.AddScoped<IAnthropicFileStore, AnthropicFileStore>();
            services.AddScoped<IChatAttachmentService, ChatAttachmentService>();
            services.AddScoped<IChatConversationService, ChatConversationService>();
            services.AddSingleton<ChatTurnRegistry>();
            services.AddScoped<IMarketBackground, MarketBackground>();

            services.AddSingleton<IPromptLoader, PromptLoader>();
            services.AddScoped<IOpenAIPromptService, OpenAIPromptService>();
            // TTS provider: Azure AI Speech when a key is configured (its F0 tier is free and is
            // not tied to the OpenAI credit balance), otherwise the original OpenAI path. Same
            // interface and same cache either way, so no caller changes when this flips.
            services.Configure<AzureSpeechOptions>(configuration.GetSection(AzureSpeechOptions.SectionName));
            var azureSpeech = configuration.GetSection(AzureSpeechOptions.SectionName).Get<AzureSpeechOptions>();
            if (!string.IsNullOrWhiteSpace(azureSpeech?.Key) && !string.IsNullOrWhiteSpace(azureSpeech.Region))
            {
                // Retry is safe here, unlike the OpenAI clients: synthesis is idempotent (the same
                // SSML yields the same audio) and F0 has no per-call charge to duplicate.
                services.AddHttpClient(AzureSpeechTextToSpeechService.HttpClientName, client =>
                {
                    // Azure documents a User-Agent as required on the synthesis endpoint.
                    client.DefaultRequestHeaders.Add("User-Agent", "JackyAIApp");
                }).AddReadOnlyResilience();
                services.AddScoped<ITextToSpeechService, AzureSpeechTextToSpeechService>();
            }
            else
            {
                services.AddScoped<ITextToSpeechService, TextToSpeechService>();
            }
            services.AddScoped<Services.Listening.IListeningImageProcessor, Services.Listening.ListeningImageProcessor>();
            services.AddScoped<Services.Listening.IListeningQuestionGenerator, Services.Listening.ListeningQuestionGenerator>();

            return services;
        }

        /// <summary>
        /// English-learning domain: dictionary, exam (+ generator strategies),
        /// repository, word-of-the-day, and daily challenge.
        /// </summary>
        public static IServiceCollection AddVocabularyServices(this IServiceCollection services)
        {
            // Spell-checker dictionary is expensive to load from disk; share one instance.
            services.AddSingleton<ISpellChecker, HunspellSpellChecker>();
            services.AddScoped<IDictionaryService, DictionaryService>();
            // Holds the words being generated, so every lookup of one waits on the same run.
            services.AddSingleton<IWordGenerator, WordGenerator>();
            services.AddScoped<IPublicWordList, PublicWordList>();

            // Exam services + per-type generator strategies
            services.AddScoped<IWordExamGenerator<ClozeTestResponse>, ClozeExamGenerator>();
            services.AddScoped<IWordExamGenerator<TranslationTestResponse>, TranslationExamGenerator>();
            services.AddScoped<IWordExamGenerator<SentenceTestResponse>, SentenceExamGenerator>();
            services.AddScoped<IExamWordPicker, ExamWordPicker>();
            services.AddScoped<IExamBankPicker, ExamBankPicker>();
            services.AddScoped<IWordExamService, WordExamService>();
            services.AddScoped<IWritingExamService, WritingExamService>();
            services.AddScoped<IDictationExamService, DictationExamService>();
            services.AddScoped<IConversationPracticeService, ConversationPracticeService>();
            services.AddScoped<IExamResultRecorder, ExamResultRecorder>();
            services.AddScoped<IListeningExamService, ListeningExamService>();
            services.AddScoped<IListeningResponseExamService, ListeningResponseExamService>();
            services.AddScoped<IReadingExamService, ReadingExamService>();
            services.AddScoped<IExamSpeech, ExamSpeech>();

            services.AddScoped<IRepositoryService, RepositoryService>();
            services.AddScoped<IWordOfTheDayService, WordOfTheDayService>();

            // Shared SM-2 scheduling (Review flow + exam-result feedback)
            services.AddScoped<ISpacedRepetitionService, SpacedRepetitionService>();
            services.AddScoped<IReviewService, ReviewService>();

            services.AddSingleton<IDailyChallengeQuestionGenerator, DailyChallengeQuestionGenerator>();
            services.AddScoped<IDailyChallengeService, DailyChallengeService>();
            services.AddScoped<IStatsService, StatsService>();

            return services;
        }

        /// <summary>Job aggregator: named HttpClients for 104/Cake + providers + aggregator.</summary>
        public static IServiceCollection AddJobAggregatorServices(this IServiceCollection services)
        {
            services.AddHttpClient("104", client =>
            {
                client.DefaultRequestHeaders.Add("Referer", "https://www.104.com.tw/");
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                client.Timeout = TimeSpan.FromSeconds(15);
            }).AddReadOnlyResilience();
            services.AddHttpClient("Cake", client =>
            {
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("Accept-Language", "zh-TW,zh;q=0.9,en;q=0.8");
                client.DefaultRequestHeaders.Add("Origin", "https://www.cake.me");
                client.DefaultRequestHeaders.Add("Referer", "https://www.cake.me/");
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                client.Timeout = TimeSpan.FromSeconds(15);
            }).AddReadOnlyResilience();
            services.AddSingleton<IJobProvider, Job104Provider>();
            services.AddSingleton<IJobProvider, CakeJobsProvider>();
            services.AddSingleton<IJobAggregatorService, JobAggregatorService>();

            return services;
        }

        /// <summary>Connector storage for third-party OAuth integrations (Jira / Microsoft / Google).</summary>
        public static IServiceCollection AddConnectorServices(
            this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<ConnectorOptions>(configuration.GetSection(ConnectorOptions.SectionName));
            services.AddScoped<ITokenEncryptionService, TokenEncryptionService>();
            services.AddScoped<IStateService, StateService>();
            services.AddScoped<IConnectorService, ConnectorService>();

            return services;
        }
    }
}
