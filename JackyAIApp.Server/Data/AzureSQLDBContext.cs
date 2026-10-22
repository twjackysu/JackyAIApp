
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Data
{
    public class AzureSQLDBContext : DbContext
    {
        public AzureSQLDBContext(DbContextOptions<AzureSQLDBContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Each entity's keys, relationships, indexes and column types are in its own
            // IEntityTypeConfiguration<T>, grouped by feature in Data/Configurations.
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AzureSQLDBContext).Assembly);
        }

        public virtual DbSet<User> Users { get; set; }
        public virtual DbSet<Word> Words { get; set; }
        public virtual DbSet<WordMeaning> WordMeanings { get; set; }
        public virtual DbSet<Definition> Definitions { get; set; }
        public virtual DbSet<ExampleSentence> ExampleSentences { get; set; }
        public virtual DbSet<WordMeaningTag> WordMeaningTags { get; set; }
        public virtual DbSet<ClozeTest> ClozeTests { get; set; }
        public virtual DbSet<ClozeTestOption> ClozeTestOptions { get; set; }
        public virtual DbSet<TranslationTest> TranslationTests { get; set; }
        public virtual DbSet<SentenceTest> SentenceTests { get; set; }
        public virtual DbSet<UserWord> UserWords { get; set; }
        public virtual DbSet<JiraConfig> JiraConfigs { get; set; }
        public virtual DbSet<UserConnector> UserConnectors { get; set; }
        public virtual DbSet<DailyUsage> DailyUsages { get; set; }
        public virtual DbSet<DailyChallengeResult> DailyChallengeResults { get; set; }
        public virtual DbSet<DailyChallengeSet> DailyChallengeSets { get; set; }
        public virtual DbSet<DataSourceConfig> DataSourceConfigs { get; set; }
        public virtual DbSet<ExamAttempt> ExamAttempts { get; set; }
        public virtual DbSet<ListeningTest> ListeningTests { get; set; }
        public virtual DbSet<ListeningTestOption> ListeningTestOptions { get; set; }
        public virtual DbSet<ListeningResponseTest> ListeningResponseTests { get; set; }
        public virtual DbSet<ListeningResponseOption> ListeningResponseOptions { get; set; }
        public virtual DbSet<ReadingPassage> ReadingPassages { get; set; }
        public virtual DbSet<ReadingQuestion> ReadingQuestions { get; set; }
        public virtual DbSet<ReadingQuestionOption> ReadingQuestionOptions { get; set; }
        public virtual DbSet<Dashboard> Dashboards { get; set; }
        public virtual DbSet<DashboardWidget> DashboardWidgets { get; set; }
        public virtual DbSet<DashboardShare> DashboardShares { get; set; }
        public virtual DbSet<HoldingSnapshot> HoldingSnapshots { get; set; }
        public virtual DbSet<HoldingPosition> HoldingPositions { get; set; }
        public virtual DbSet<AiUsageLog> AiUsageLogs { get; set; }
        public virtual DbSet<ChatConversation> ChatConversations { get; set; }
        public virtual DbSet<ChatMessage> ChatMessages { get; set; }
        public virtual DbSet<ChatAttachment> ChatAttachments { get; set; }
        public virtual DbSet<DailyStockPrice> DailyStockPrices { get; set; }
        public virtual DbSet<FxRate> FxRates { get; set; }
        public virtual DbSet<StockSymbolInfo> StockSymbolInfos { get; set; }
        public virtual DbSet<TwListedCompany> TwListedCompanies { get; set; }
        public virtual DbSet<StockPrediction> StockPredictions { get; set; }
        public virtual DbSet<TaiexTotalReturnDaily> TaiexTotalReturnDailies { get; set; }
    }
}
