namespace MultiMessenger.Core.Entities;

/// <summary>
/// Версия текста до правки. Менеджер видит актуальный вариант, как в обычном клиенте,
/// но полная история изменений остаётся в БД.
/// </summary>
public class MessageEditHistory
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MessageId { get; set; }

    public Message? Message { get; set; }

    public string? PreviousText { get; set; }

    public DateTimeOffset EditedAt { get; set; } = DateTimeOffset.UtcNow;
}
