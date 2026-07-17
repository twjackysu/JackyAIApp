
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
            // Configure User-Word many-to-many relationship
            modelBuilder.Entity<UserWord>()
                .HasKey(uw => uw.Id);

            modelBuilder.Entity<UserWord>()
                .HasOne(uw => uw.User)
                .WithMany(u => u.UserWords)
                .HasForeignKey(uw => uw.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserWord>()
                .HasOne(uw => uw.Word)
                .WithMany()
                .HasForeignKey(uw => uw.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure JiraConfig relationship
            modelBuilder.Entity<JiraConfig>()
                .HasOne(jc => jc.User)
                .WithMany(u => u.JiraConfigs)
                .HasForeignKey(jc => jc.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure WordMeaning relationship
            modelBuilder.Entity<WordMeaning>()
                .HasOne(wm => wm.Word)
                .WithMany(w => w.Meanings)
                .HasForeignKey(wm => wm.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure Definition relationship
            modelBuilder.Entity<Definition>()
                .HasOne(d => d.WordMeaning)
                .WithMany(wm => wm.Definitions)
                .HasForeignKey(d => d.WordMeaningId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure ExampleSentence relationship
            modelBuilder.Entity<ExampleSentence>()
                .HasOne(es => es.WordMeaning)
                .WithMany(wm => wm.ExampleSentences)
                .HasForeignKey(es => es.WordMeaningId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure WordMeaningTag relationship
            modelBuilder.Entity<WordMeaningTag>()
                .HasOne(wmt => wmt.WordMeaning)
                .WithMany(wm => wm.Tags)
                .HasForeignKey(wmt => wmt.WordMeaningId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure ClozeTest relationship
            modelBuilder.Entity<ClozeTest>()
                .HasOne(ct => ct.Word)
                .WithMany(w => w.ClozeTests)
                .HasForeignKey(ct => ct.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure ClozeTestOption relationship
            modelBuilder.Entity<ClozeTestOption>()
                .HasOne(cto => cto.ClozeTest)
                .WithMany(ct => ct.Options)
                .HasForeignKey(cto => cto.ClozeTestId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure TranslationTest relationship
            modelBuilder.Entity<TranslationTest>()
                .HasOne(tt => tt.Word)
                .WithMany(w => w.TranslationTests)
                .HasForeignKey(tt => tt.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure SentenceTest relationship
            modelBuilder.Entity<SentenceTest>()
                .HasOne(st => st.Word)
                .WithMany(w => w.SentenceTests)
                .HasForeignKey(st => st.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure UserConnector relationship
            modelBuilder.Entity<UserConnector>()
                .HasOne(uc => uc.User)
                .WithMany(u => u.UserConnectors)
                .HasForeignKey(uc => uc.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Ensure unique constraint for UserId + ProviderName
            modelBuilder.Entity<UserConnector>()
                .HasIndex(uc => new { uc.UserId, uc.ProviderName })
                .IsUnique();

            // Configure CreditTransaction relationship
            modelBuilder.Entity<CreditTransaction>()
                .HasOne(ct => ct.User)
                .WithMany(u => u.CreditTransactions)
                .HasForeignKey(ct => ct.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for faster queries by user
            modelBuilder.Entity<CreditTransaction>()
                .HasIndex(ct => ct.UserId);

            // Index for faster queries by date
            modelBuilder.Entity<CreditTransaction>()
                .HasIndex(ct => ct.CreatedAt);

            // Configure DailyChallengeResult relationship
            modelBuilder.Entity<DailyChallengeResult>()
                .HasOne(dc => dc.User)
                .WithMany(u => u.DailyChallengeResults)
                .HasForeignKey(dc => dc.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for faster queries: one result per user per day
            modelBuilder.Entity<DailyChallengeResult>()
                .HasIndex(dc => new { dc.UserId, dc.ChallengeDate })
                .IsUnique();

            // Configure DataSourceConfig relationship (per-user)
            modelBuilder.Entity<DataSourceConfig>()
                .HasOne(ds => ds.User)
                .WithMany(u => u.DataSourceConfigs)
                .HasForeignKey(ds => ds.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for fast per-user listing
            modelBuilder.Entity<DataSourceConfig>()
                .HasIndex(ds => ds.UserId);

            // Configure ExamAttempt relationships
            modelBuilder.Entity<ExamAttempt>()
                .HasOne(ea => ea.User)
                .WithMany()
                .HasForeignKey(ea => ea.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExamAttempt>()
                .HasOne(ea => ea.Word)
                .WithMany()
                .HasForeignKey(ea => ea.WordId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ExamAttempt>()
                .HasOne(ea => ea.ListeningTest)
                .WithMany()
                .HasForeignKey(ea => ea.ListeningTestId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ExamAttempt>()
                .HasOne(ea => ea.ListeningResponseTest)
                .WithMany()
                .HasForeignKey(ea => ea.ListeningResponseTestId)
                .OnDelete(DeleteBehavior.NoAction);

            // Indexes for per-word history and recent-activity queries
            modelBuilder.Entity<ExamAttempt>()
                .HasIndex(ea => new { ea.UserId, ea.WordId });
            modelBuilder.Entity<ExamAttempt>()
                .HasIndex(ea => new { ea.UserId, ea.CreatedAt });

            // Configure ListeningTestOption relationship
            modelBuilder.Entity<ListeningTestOption>()
                .HasOne(lto => lto.ListeningTest)
                .WithMany(lt => lt.Options)
                .HasForeignKey(lto => lto.ListeningTestId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure ListeningResponseOption relationship
            modelBuilder.Entity<ListeningResponseOption>()
                .HasOne(lro => lro.ListeningResponseTest)
                .WithMany(lrt => lrt.Options)
                .HasForeignKey(lro => lro.ListeningResponseTestId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure Dashboard relationships
            modelBuilder.Entity<Dashboard>()
                .HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Dashboard>()
                .HasIndex(d => d.UserId);

            modelBuilder.Entity<DashboardWidget>()
                .HasOne(w => w.Dashboard)
                .WithMany(d => d.Widgets)
                .HasForeignKey(w => w.DashboardId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<DashboardWidget>()
                .HasIndex(w => w.DashboardId);

            modelBuilder.Entity<DashboardShare>()
                .HasOne(s => s.Dashboard)
                .WithMany(d => d.Shares)
                .HasForeignKey(s => s.DashboardId)
                .OnDelete(DeleteBehavior.Cascade);
            // Grantee user — NoAction to avoid multiple cascade paths (User → Dashboard → Share).
            modelBuilder.Entity<DashboardShare>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<DashboardShare>()
                .HasIndex(s => new { s.DashboardId, s.UserId })
                .IsUnique();

            // Portfolio
            modelBuilder.Entity<HoldingSnapshot>()
                .HasOne(hs => hs.User)
                .WithMany(u => u.HoldingSnapshots)
                .HasForeignKey(hs => hs.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HoldingSnapshot>()
                .HasIndex(hs => new { hs.UserId, hs.EffectiveDate })
                .IsUnique();

            modelBuilder.Entity<HoldingPosition>()
                .HasOne(hp => hp.Snapshot)
                .WithMany(hs => hs.Positions)
                .HasForeignKey(hp => hp.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HoldingPosition>()
                .Property(hp => hp.Quantity).HasPrecision(18, 6);
            modelBuilder.Entity<HoldingPosition>()
                .Property(hp => hp.UnitPrice).HasPrecision(18, 6);
            modelBuilder.Entity<HoldingPosition>()
                .Property(hp => hp.CostBasis).HasPrecision(18, 4);

            // Chatbot conversation history (self-hosted, replaces Dify-side storage)
            modelBuilder.Entity<ChatConversation>()
                .HasIndex(c => new { c.UserId, c.UpdatedAt });

            modelBuilder.Entity<ChatMessage>()
                .HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatMessage>()
                .HasIndex(m => new { m.ConversationId, m.CreatedAt });

            // Persistent TW stock price cache (shared across users, immutable finalized bars).
            modelBuilder.Entity<DailyStockPrice>()
                .HasIndex(p => new { p.Symbol, p.TradeDate })
                .IsUnique();
            modelBuilder.Entity<DailyStockPrice>()
                .Property(p => p.Open).HasPrecision(18, 4);
            modelBuilder.Entity<DailyStockPrice>()
                .Property(p => p.High).HasPrecision(18, 4);
            modelBuilder.Entity<DailyStockPrice>()
                .Property(p => p.Low).HasPrecision(18, 4);
            modelBuilder.Entity<DailyStockPrice>()
                .Property(p => p.Close).HasPrecision(18, 4);
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
        public virtual DbSet<CreditTransaction> CreditTransactions { get; set; }
        public virtual DbSet<DailyChallengeResult> DailyChallengeResults { get; set; }
        public virtual DbSet<DataSourceConfig> DataSourceConfigs { get; set; }
        public virtual DbSet<ExamAttempt> ExamAttempts { get; set; }
        public virtual DbSet<ListeningTest> ListeningTests { get; set; }
        public virtual DbSet<ListeningTestOption> ListeningTestOptions { get; set; }
        public virtual DbSet<ListeningResponseTest> ListeningResponseTests { get; set; }
        public virtual DbSet<ListeningResponseOption> ListeningResponseOptions { get; set; }
        public virtual DbSet<Dashboard> Dashboards { get; set; }
        public virtual DbSet<DashboardWidget> DashboardWidgets { get; set; }
        public virtual DbSet<DashboardShare> DashboardShares { get; set; }
        public virtual DbSet<HoldingSnapshot> HoldingSnapshots { get; set; }
        public virtual DbSet<HoldingPosition> HoldingPositions { get; set; }
        public virtual DbSet<AiUsageLog> AiUsageLogs { get; set; }
        public virtual DbSet<ChatConversation> ChatConversations { get; set; }
        public virtual DbSet<ChatMessage> ChatMessages { get; set; }
        public virtual DbSet<DailyStockPrice> DailyStockPrices { get; set; }
        public virtual DbSet<StockSymbolInfo> StockSymbolInfos { get; set; }
        public virtual DbSet<TwListedCompany> TwListedCompanies { get; set; }
    }
}
