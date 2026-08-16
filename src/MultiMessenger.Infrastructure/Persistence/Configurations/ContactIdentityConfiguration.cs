using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiMessenger.Core.Entities;

namespace MultiMessenger.Infrastructure.Persistence.Configurations;

public class ContactIdentityConfiguration : IEntityTypeConfiguration<ContactIdentity>
{
    public void Configure(EntityTypeBuilder<ContactIdentity> builder)
    {
        builder.HasKey(identity => identity.Id);

        builder.Property(identity => identity.PlatformUserId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(identity => identity.DisplayNameOnPlatform).HasMaxLength(200);

        // Главный индекс горячего пути: по каждому входящему сообщению система ищет,
        // какому контакту принадлежит отправитель. Уникальный — один и тот же
        // пользователь платформы не может быть привязан к двум контактам.
        builder.HasIndex(identity => new { identity.Platform, identity.PlatformUserId })
            .IsUnique();
    }
}
