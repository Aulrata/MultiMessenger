namespace MultiMessenger.Core.Auditing;

/// <summary>
/// Запись журнала аудита: кто, что и когда сделал.
/// <para>
/// Хранится в PostgreSQL, а не в файлах логов, потому что это данные, а не диагностика:
/// нужна связь с менеджером, выборки и выгрузки через SQL, хранение годами и попадание
/// в регулярный дамп БД. Переписка с клиентами подпадает под 152-ФЗ, и вопрос «кто из
/// сотрудников открывал этот диалог» должен иметь ответ.
/// </para>
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Кто выполнил действие. Null допустим для неудачной попытки входа —
    /// менеджер ещё не установлен, в <see cref="Subject"/> при этом лежит введённый номер.
    /// </summary>
    public Guid? ManagerId { get; set; }

    /// <summary>
    /// Чей аккаунт использовался, когда действие выполнялось через мультиаккаунт.
    /// В <see cref="ManagerId"/> при этом остаётся тот, кто действие фактически совершил.
    /// Разделение принципиальное: журнал должен отвечать «кто это сделал»,
    /// а не «под кем это выглядело».
    /// </summary>
    public Guid? ImpersonatedManagerId { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>
    /// Над чем совершено действие в человекочитаемом виде: номер телефона при входе,
    /// имя клиента при открытии диалога. Нужно, чтобы журнал читался без джойнов
    /// и оставался осмысленным после удаления связанных записей.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>Тип затронутой сущности: <c>Dialog</c>, <c>Contact</c>, <c>MessengerAccount</c>.</summary>
    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    /// <summary>
    /// Произвольные подробности события. В PostgreSQL ложится в <c>jsonb</c>,
    /// поэтому по содержимому можно фильтровать запросами.
    /// </summary>
    public string? DetailsJson { get; set; }

    /// <summary>IP-адрес, с которого пришёл запрос. Для разбора инцидентов.</summary>
    public string? IpAddress { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
