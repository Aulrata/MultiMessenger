using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Entities;

/// <summary>
/// Событие состояния канала: подключение, обрыв, попытка переподключения, протухшая сессия.
/// Питает дашборд в админке.
/// </summary>
public class AccountHealthLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MessengerAccountId { get; set; }

    public MessengerAccount? MessengerAccount { get; set; }

    public AccountHealthEventType EventType { get; set; }

    /// <summary>
    /// Подробности события — текст ошибки при обрыве, номер попытки при переподключении.
    /// В ТЗ поля нет, но без него дашборд показывает «Disconnected» без объяснения причины.
    /// </summary>
    public string? Details { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
