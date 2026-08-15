using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiMessenger.Core.Entities;

namespace MultiMessenger.Infrastructure.Persistence.Configurations;

public class ManagerConfiguration : IEntityTypeConfiguration<Manager>
{
    public void Configure(EntityTypeBuilder<Manager> builder)
    {
        builder.HasKey(manager => manager.Id);

        builder.Property(manager => manager.PhoneNumber)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(manager => manager.PasswordHash)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(manager => manager.FullName)
            .HasMaxLength(200)
            .IsRequired();

        // Номер телефона — логин, дубли недопустимы.
        builder.HasIndex(manager => manager.PhoneNumber)
            .IsUnique();

        builder.HasMany(manager => manager.MessengerAccounts)
            .WithOne(account => account.Manager)
            .HasForeignKey(account => account.ManagerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
