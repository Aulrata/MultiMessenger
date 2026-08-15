using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiMessenger.Core.Entities;

namespace MultiMessenger.Infrastructure.Persistence.Configurations;

public class AccountHealthLogConfiguration : IEntityTypeConfiguration<AccountHealthLog>
{
    public void Configure(EntityTypeBuilder<AccountHealthLog> builder)
    {
        builder.HasKey(log => log.Id);

        builder.Property(log => log.Details).HasMaxLength(2000);

        // Дашборд показывает последние события по каждому каналу.
        builder.HasIndex(log => new { log.MessengerAccountId, log.OccurredAt })
            .IsDescending(false, true);
    }
}
