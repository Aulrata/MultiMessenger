using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiMessenger.Core.Entities;

namespace MultiMessenger.Infrastructure.Persistence.Configurations;

public class DialogConfiguration : IEntityTypeConfiguration<Dialog>
{
    public void Configure(EntityTypeBuilder<Dialog> builder)
    {
        builder.HasKey(dialog => dialog.Id);

        // Сортировка списка диалогов, как в Telegram: свежие сверху.
        builder.HasIndex(dialog => dialog.LastMessageAt)
            .IsDescending();

        // Один контакт через один канал — ровно один диалог. Защищает от гонки,
        // когда два входящих сообщения подряд создают дубли.
        builder.HasIndex(dialog => new { dialog.ContactId, dialog.MessengerAccountId })
            .IsUnique();

        builder.HasMany(dialog => dialog.Messages)
            .WithOne(message => message.Dialog)
            .HasForeignKey(message => message.DialogId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
