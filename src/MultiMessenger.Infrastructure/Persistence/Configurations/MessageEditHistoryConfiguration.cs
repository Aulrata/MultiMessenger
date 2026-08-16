using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiMessenger.Core.Entities;

namespace MultiMessenger.Infrastructure.Persistence.Configurations;

public class MessageEditHistoryConfiguration : IEntityTypeConfiguration<MessageEditHistory>
{
    public void Configure(EntityTypeBuilder<MessageEditHistory> builder)
    {
        builder.ToTable("MessageEditHistory");

        builder.HasKey(history => history.Id);

        // Полная история одного сообщения в хронологическом порядке.
        builder.HasIndex(history => new { history.MessageId, history.ChangedAt });

        // «Что этот сотрудник правил и удалял» — для разбора спорных ситуаций.
        builder.HasIndex(history => new { history.ChangedByManagerId, history.ChangedAt })
            .IsDescending(false, true)
            .HasFilter("\"ChangedByManagerId\" IS NOT NULL");
    }
}
