using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid DialogId { get; set; }

    public Dialog? Dialog { get; set; }

    public MessageDirection Direction { get; set; }

    public SenderType SenderType { get; set; }

    /// <summary>
    /// Идентификатор на стороне платформы. Ключ дедупликации: по нему исходящее из
    /// собственной очереди отличается от того же сообщения, пришедшего апдейтом
    /// с флагом <c>out_ = true</c> после отправки с телефона менеджера.
    /// <para>
    /// Null, пока сообщение лежит в очереди со статусом <see cref="MessageStatus.Pending"/>, —
    /// в этот период роль временного локального идентификатора играет <see cref="Id"/>.
    /// </para>
    /// </summary>
    public string? PlatformMessageId { get; set; }

    /// <summary>Актуальный текст. При редактировании прошлая версия уезжает в <see cref="EditHistory"/>.</summary>
    public string? Text { get; set; }

    public MessageStatus Status { get; set; }

    public bool IsEdited { get; set; }

    /// <summary>Причина неудачной отправки — показывается менеджеру в интерфейсе.</summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Время исходного сообщения на платформе, а не время записи в БД.
    /// Иначе догрузка истории перемешает порядок переписки.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<MessageEditHistory> EditHistory { get; set; } = [];

    public ICollection<MediaAttachment> Attachments { get; set; } = [];
}
