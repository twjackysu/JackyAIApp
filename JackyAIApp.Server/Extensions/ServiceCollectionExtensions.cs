using DotnetSdkUtilities.Factory.ResponseFactory;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
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
        /// factories, current-user/credit access, the floating chatbot's direct
        /// Claude client, and the prompt-file-backed OpenAI wrapper.
        /// </summary>
        public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpContextAccessor();
            services.AddScoped<IApiResponseFactory, ApiResponseFactory>();
            services.AddScoped<IMyResponseFactory, ResponseFactory>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<ICreditService, CreditService>();
            services.Configure<UsageLimitOptions>(configuration.GetSection(UsageLimitOptions.SectionName));
            services.AddScoped<IUsageLimitService, UsageLimitService>();
            services.AddScoped<IAiUsageLogger, AiUsageLogger>();
            services.AddScoped<IAnthropicChatService, AnthropicChatService>();

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
#pragma warning disable CA1416 // ListeningImageProcessor is Windows-only; app only runs on Windows App Service.
            services.AddScoped<Services.Listening.IListeningImageProcessor, Services.Listening.ListeningImageProcessor>();
#pragma warning restore CA1416
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

            // Exam services + per-type generator strategies
            services.AddScoped<IWordExamGenerator, ClozeExamGenerator>();
            services.AddScoped<IWordExamGenerator, TranslationExamGenerator>();
            services.AddScoped<IWordExamGenerator, SentenceExamGenerator>();
            services.AddScoped<IExamService, ExamService>();

            services.AddScoped<IRepositoryService, RepositoryService>();
            services.AddScoped<IWordOfTheDayService, WordOfTheDayService>();

            // Shared SM-2 scheduling (Review flow + exam-result feedback)
            services.AddScoped<ISpacedRepetitionService, SpacedRepetitionService>();

            services.AddSingleton<IDailyChallengeQuestionGenerator, DailyChallengeQuestionGenerator>();
            services.AddScoped<IDailyChallengeService, DailyChallengeService>();

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
                client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml");
                client.DefaultRequestHeaders.Add("Accept-Language", "zh-TW,zh;q=0.9,en;q=0.8");
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                client.Timeout = TimeSpan.FromSeconds(15);
            }).AddReadOnlyResilience();
            services.AddSingleton<IJobProvider, Job104Provider>();
            services.AddSingleton<IJobProvider, CakeJobsProvider>();
            services.AddSingleton<IJobAggregatorService, JobAggregatorService>();

            return services;
        }

        /// <summary>Connector storage + payment providers (PayPal / ECPay).</summary>
        public static IServiceCollection AddConnectorAndPaymentServices(
            this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<ConnectorOptions>(configuration.GetSection(ConnectorOptions.SectionName));
            services.AddScoped<ITokenEncryptionService, TokenEncryptionService>();
            services.AddScoped<IStateService, StateService>();
            services.AddScoped<IConnectorService, ConnectorService>();

            services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));
            services.AddHttpClient<IPayPalService, PayPalService>();

            services.Configure<ECPayOptions>(configuration.GetSection(ECPayOptions.SectionName));
            // Typed client (was AddScoped) — subscription cancellation now calls ECPay's
            // CreditCardPeriodAction API server-to-server, same pattern as PayPalService above.
            services.AddHttpClient<IECPayService, ECPayService>();

            services.Configure<PayUniOptions>(configuration.GetSection(PayUniOptions.SectionName));
            // No outbound HTTP calls (UPP checkout is a browser-submitted form; PayUni has no
            // cancel API to call — see PayUniService's class doc), plain scoped service.
            services.AddScoped<IPayUniService, PayUniService>();

            return services;
        }
    }
}
