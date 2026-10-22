using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JackyAIApp.Server.Data.Configurations
{
    // The dictionary's words and their meanings, and the words each user has saved.

    public sealed class UserWordConfiguration : IEntityTypeConfiguration<UserWord>
    {
        public void Configure(EntityTypeBuilder<UserWord> builder)
        {
            // Configure User-Word many-to-many relationship
            builder.HasKey(uw => uw.Id);

            // A word can be in a user's collection once. Adding checks first, but two adds landing
            // together (a double click) both pass that check, and the duplicate they left broke
            // every lookup that expects one row (delete, word detail, exam scheduling).
            builder.HasIndex(uw => new { uw.UserId, uw.WordId })
                .IsUnique();

            builder.HasOne(uw => uw.User)
                .WithMany(u => u.UserWords)
                .HasForeignKey(uw => uw.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(uw => uw.Word)
                .WithMany()
                .HasForeignKey(uw => uw.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class WordConfiguration : IEntityTypeConfiguration<Word>
    {
        public void Configure(EntityTypeBuilder<Word> builder)
        {
            // Public routing key for word-detail pages (Growth & Retention SEO) — also closes a
            // data-integrity gap, since uniqueness was previously enforced only by application
            // logic (DictionaryService.LoadWordAsync's SingleOrDefaultAsync). nvarchar(max)
            // cannot be indexed in SQL Server, so the column is bounded first; 450 chars is the
            // largest an index key supports under the DB's default collation (900-byte key
            // limit / 2 bytes per char). No custom collation introduced — uniqueness relies on
            // whatever case sensitivity the database's default collation already provides, same
            // as every other unique index in this model.
            builder.Property(w => w.WordText)
                .HasMaxLength(450);

            builder.HasIndex(w => w.WordText)
                .IsUnique();
        }
    }

    public sealed class WordMeaningConfiguration : IEntityTypeConfiguration<WordMeaning>
    {
        public void Configure(EntityTypeBuilder<WordMeaning> builder)
        {
            builder.HasOne(wm => wm.Word)
                .WithMany(w => w.Meanings)
                .HasForeignKey(wm => wm.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class DefinitionConfiguration : IEntityTypeConfiguration<Definition>
    {
        public void Configure(EntityTypeBuilder<Definition> builder)
        {
            builder.HasOne(d => d.WordMeaning)
                .WithMany(wm => wm.Definitions)
                .HasForeignKey(d => d.WordMeaningId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class ExampleSentenceConfiguration : IEntityTypeConfiguration<ExampleSentence>
    {
        public void Configure(EntityTypeBuilder<ExampleSentence> builder)
        {
            builder.HasOne(es => es.WordMeaning)
                .WithMany(wm => wm.ExampleSentences)
                .HasForeignKey(es => es.WordMeaningId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class WordMeaningTagConfiguration : IEntityTypeConfiguration<WordMeaningTag>
    {
        public void Configure(EntityTypeBuilder<WordMeaningTag> builder)
        {
            builder.HasOne(wmt => wmt.WordMeaning)
                .WithMany(wm => wm.Tags)
                .HasForeignKey(wmt => wmt.WordMeaningId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
