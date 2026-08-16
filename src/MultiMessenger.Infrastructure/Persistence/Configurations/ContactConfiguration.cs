using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiMessenger.Core.Entities;

namespace MultiMessenger.Infrastructure.Persistence.Configurations;

public class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.HasKey(contact => contact.Id);

        builder.Property(contact => contact.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(contact => contact.CrmOrderNumber).HasMaxLength(64);

        // Поиск по номеру заявки из карточки контакта (этап 3.4).
        builder.HasIndex(contact => contact.CrmOrderNumber);

        builder.HasMany(contact => contact.Identities)
            .WithOne(identity => identity.Contact)
            .HasForeignKey(identity => identity.ContactId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(contact => contact.Dialogs)
            .WithOne(dialog => dialog.Contact)
            .HasForeignKey(dialog => dialog.ContactId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
