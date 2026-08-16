using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiMessenger.Core.Entities;

namespace MultiMessenger.Infrastructure.Persistence.Configurations;

public class MessengerAccountConfiguration : IEntityTypeConfiguration<MessengerAccount>
{
    public void Configure(EntityTypeBuilder<MessengerAccount> builder)
    {
        builder.HasKey(account => account.Id);

        builder.Property(account => account.PhoneNumber).HasMaxLength(20);
        builder.Property(account => account.BotToken).HasMaxLength(512);
        builder.Property(account => account.SessionPath).HasMaxLength(512);

        // Дважды подключить одну и ту же платформу под одним номером нельзя.
        builder.HasIndex(account => new { account.ManagerId, account.Platform })
            .IsUnique();

        // Выборка активных каналов при старте приложения.
        builder.HasIndex(account => account.Status);

        builder.HasMany(account => account.Dialogs)
            .WithOne(dialog => dialog.MessengerAccount)
            .HasForeignKey(dialog => dialog.MessengerAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(account => account.HealthLogs)
            .WithOne(log => log.MessengerAccount)
            .HasForeignKey(log => log.MessengerAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
