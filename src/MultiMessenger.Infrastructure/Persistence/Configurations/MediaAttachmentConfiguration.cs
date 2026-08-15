using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiMessenger.Core.Entities;

namespace MultiMessenger.Infrastructure.Persistence.Configurations;

public class MediaAttachmentConfiguration : IEntityTypeConfiguration<MediaAttachment>
{
    public void Configure(EntityTypeBuilder<MediaAttachment> builder)
    {
        builder.HasKey(attachment => attachment.Id);

        builder.Property(attachment => attachment.StorageKey)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(attachment => attachment.FileName).HasMaxLength(512);
        builder.Property(attachment => attachment.MimeType).HasMaxLength(128);

        // Один объект в MinIO — одно вложение. Заодно ловит повторное скачивание
        // того же файла при перезапуске загрузчика истории.
        builder.HasIndex(attachment => attachment.StorageKey)
            .IsUnique();
    }
}
