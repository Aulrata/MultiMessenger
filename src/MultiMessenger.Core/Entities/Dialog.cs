using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Entities;

/// <summary>
/// Переписка конкретного клиента через конкретный канал менеджера.
/// Если клиент пишет и в Telegram, и в WhatsApp — это два диалога у одного контакта.
/// </summary>
public class Dialog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ContactId { get; set; }

    public Contact? Contact { get; set; }

    public Guid MessengerAccountId { get; set; }

    public MessengerAccount? MessengerAccount { get; set; }

    /// <summary>
    /// Дублирует платформу аккаунта. Денормализация ради сортировки и фильтрации
    /// списка диалогов без join'а к <see cref="MessengerAccount"/>.
    /// </summary>
    public MessengerPlatform Platform { get; set; }

    public DialogStatus Status { get; set; } = DialogStatus.Active;

    /// <summary>Время последнего сообщения — по нему сортируется список диалогов.</summary>
    public DateTimeOffset? LastMessageAt { get; set; }

    public ICollection<Message> Messages { get; set; } = [];
}
