using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JackyAIApp.Server.Data.Configurations
{
    // The exam question banks (word-scoped and standalone) and each user's exam attempts.

    public sealed class ClozeTestConfiguration : IEntityTypeConfiguration<ClozeTest>
    {
        public void Configure(EntityTypeBuilder<ClozeTest> builder)
        {
            builder.HasOne(ct => ct.Word)
                .WithMany(w => w.ClozeTests)
                .HasForeignKey(ct => ct.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class ClozeTestOptionConfiguration : IEntityTypeConfiguration<ClozeTestOption>
    {
        public void Configure(EntityTypeBuilder<ClozeTestOption> builder)
        {
            builder.HasOne(cto => cto.ClozeTest)
                .WithMany(ct => ct.Options)
                .HasForeignKey(cto => cto.ClozeTestId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class TranslationTestConfiguration : IEntityTypeConfiguration<TranslationTest>
    {
        public void Configure(EntityTypeBuilder<TranslationTest> builder)
        {
            builder.HasOne(tt => tt.Word)
                .WithMany(w => w.TranslationTests)
                .HasForeignKey(tt => tt.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class SentenceTestConfiguration : IEntityTypeConfiguration<SentenceTest>
    {
        public void Configure(EntityTypeBuilder<SentenceTest> builder)
        {
            builder.HasOne(st => st.Word)
                .WithMany(w => w.SentenceTests)
                .HasForeignKey(st => st.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class ListeningTestOptionConfiguration : IEntityTypeConfiguration<ListeningTestOption>
    {
        public void Configure(EntityTypeBuilder<ListeningTestOption> builder)
        {
            builder.HasOne(lto => lto.ListeningTest)
                .WithMany(lt => lt.Options)
                .HasForeignKey(lto => lto.ListeningTestId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class ListeningResponseOptionConfiguration : IEntityTypeConfiguration<ListeningResponseOption>
    {
        public void Configure(EntityTypeBuilder<ListeningResponseOption> builder)
        {
            builder.HasOne(lro => lro.ListeningResponseTest)
                .WithMany(lrt => lrt.Options)
                .HasForeignKey(lro => lro.ListeningResponseTestId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class ReadingQuestionConfiguration : IEntityTypeConfiguration<ReadingQuestion>
    {
        public void Configure(EntityTypeBuilder<ReadingQuestion> builder)
        {
            builder.HasOne(rq => rq.ReadingPassage)
                .WithMany(rp => rp.Questions)
                .HasForeignKey(rq => rq.PassageId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class ReadingQuestionOptionConfiguration : IEntityTypeConfiguration<ReadingQuestionOption>
    {
        public void Configure(EntityTypeBuilder<ReadingQuestionOption> builder)
        {
            builder.HasOne(rqo => rqo.ReadingQuestion)
                .WithMany(rq => rq.Options)
                .HasForeignKey(rqo => rqo.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class ExamAttemptConfiguration : IEntityTypeConfiguration<ExamAttempt>
    {
        public void Configure(EntityTypeBuilder<ExamAttempt> builder)
        {
            builder.HasOne(ea => ea.User)
                .WithMany()
                .HasForeignKey(ea => ea.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(ea => ea.Word)
                .WithMany()
                .HasForeignKey(ea => ea.WordId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(ea => ea.ListeningTest)
                .WithMany()
                .HasForeignKey(ea => ea.ListeningTestId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(ea => ea.ListeningResponseTest)
                .WithMany()
                .HasForeignKey(ea => ea.ListeningResponseTestId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(ea => ea.ReadingPassage)
                .WithMany()
                .HasForeignKey(ea => ea.ReadingPassageId)
                .OnDelete(DeleteBehavior.NoAction);

            // Indexes for per-word history and recent-activity queries
            builder.HasIndex(ea => new { ea.UserId, ea.WordId });
            builder.HasIndex(ea => new { ea.UserId, ea.CreatedAt });
        }
    }
}
