using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Entities;

/// <summary>
/// Клиент. Один и тот же человек может писать с нескольких платформ —
/// за это отвечает коллекция <see cref="Identities"/>.
/// </summary>
public class Contact
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Имя, которое видит менеджер. Редактируется вручную.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Платформа последнего входящего сообщения. Обновляется автоматически
    /// и определяет, куда по умолчанию уйдёт ответ.
    /// </summary>
    public MessengerPlatform PrimaryPlatform { get; set; }

    /// <summary>Номер заявки в U-ON. Пока просто текст — задел под интеграцию.</summary>
    public string? CrmOrderNumber { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ContactIdentity> Identities { get; set; } = [];

    public ICollection<Dialog> Dialogs { get; set; } = [];
}
