using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JackyAIApp.Server.Data.Configurations
{
    // Chatbot conversation history (self-hosted, replaces Dify-side storage).

    /// <summary>Studio AI's notes about the reader (docs/ai-native/design/08): read all at once, per reader.</summary>
    public sealed class AssistantMemoryConfiguration : IEntityTypeConfiguration<AssistantMemory>
    {
        public void Configure(EntityTypeBuilder<AssistantMemory> builder)
        {
            builder.HasIndex(m => m.UserId);
        }
    }

    public sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
    {
        public void Configure(EntityTypeBuilder<ChatConversation> builder)
        {
            builder.HasIndex(c => new { c.UserId, c.UpdatedAt });
        }
    }

    public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
    {
        public void Configure(EntityTypeBuilder<ChatMessage> builder)
        {
            builder.HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        }
    }

    public sealed class ChatAttachmentConfiguration : IEntityTypeConfiguration<ChatAttachment>
    {
        public void Configure(EntityTypeBuilder<ChatAttachment> builder)
        {
            // Attachments go with their message (and so with the conversation).
            builder.HasOne(a => a.Message)
                .WithMany(m => m.Attachments)
                .HasForeignKey(a => a.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(a => new { a.UserId, a.MessageId });
        }
    }
}
