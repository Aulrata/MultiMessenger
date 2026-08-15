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

        builder.HasIndex(history => new { history.MessageId, history.EditedAt });
    }
}
