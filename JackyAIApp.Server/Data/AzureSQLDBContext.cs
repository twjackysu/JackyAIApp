
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
    }
}
