using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiMessenger.Core.Entities;

namespace MultiMessenger.Infrastructure.Persistence.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(message => message.Id);

        builder.Property(message => message.PlatformMessageId).HasMaxLength(128);
        builder.Property(message => message.FailureReason).HasMaxLength(1000);

        // Выборка истории переписки: все сообщения диалога по возрастанию времени.
        builder.HasIndex(message => new { message.DialogId, message.CreatedAt });

        // Защита от дублей при синхронизации. Фильтр обязателен: у сообщений в очереди
        // PlatformMessageId ещё null, и таких строк в одном диалоге может быть много.
        builder.HasIndex(message => new { message.DialogId, message.PlatformMessageId })
            .IsUnique()
            .HasFilter("\"PlatformMessageId\" IS NOT NULL");

        // Outbox забирает Pending-сообщения; частичный индекс не растёт вместе с историей.
        builder.HasIndex(message => message.Status)
            .HasFilter("\"Status\" = 'Pending'");

        builder.HasMany(message => message.EditHistory)
            .WithOne(history => history.Message)
            .HasForeignKey(history => history.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(message => message.Attachments)
            .WithOne(attachment => attachment.Message)
            .HasForeignKey(attachment => attachment.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
