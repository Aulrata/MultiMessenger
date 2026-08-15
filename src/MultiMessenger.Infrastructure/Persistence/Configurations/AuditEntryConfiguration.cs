using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MultiMessenger.Core.Auditing;

namespace MultiMessenger.Infrastructure.Persistence.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Subject).HasMaxLength(256);
        builder.Property(entry => entry.EntityType).HasMaxLength(64);
        builder.Property(entry => entry.IpAddress).HasMaxLength(64);

        // jsonb, а не text: по подробностям события можно фильтровать SQL-запросом,
        // не заводя отдельную колонку под каждый тип действия.
        builder.Property(entry => entry.DetailsJson).HasColumnType("jsonb");

        // «Все действия сотрудника за период» — основной сценарий разбора инцидента.
        builder.HasIndex(entry => new { entry.ManagerId, entry.OccurredAt })
            .IsDescending(false, true);

        // «Кто открывал этот диалог» — второй сценарий, от сущности.
        builder.HasIndex(entry => new { entry.EntityType, entry.EntityId });

        builder.HasIndex(entry => entry.OccurredAt)
            .IsDescending();

        // Namespace у AuditEntry другой, а FK на Manager нет намеренно: запись аудита
        // должна пережить удаление учётной записи, иначе теряется весь смысл журнала.
    }
}
