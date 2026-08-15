namespace MultiMessenger.Core.Auditing;

/// <summary>
/// Запись событий в журнал аудита. Реализация появится в этапе 1.4 вместе
/// с <c>AppDbContext</c>, первые вызовы — в 1.5 на входе и выходе менеджера.
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
