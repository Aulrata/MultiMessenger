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

    /// <summary>
    /// Мягкое удаление. Строка остаётся в таблице, интерфейс показывает заглушку,
    /// а текст на момент удаления лежит в <see cref="EditHistory"/>. Физически
    /// сообщения не удаляются: внутренний архив переписки не должен зависеть
    /// от того, что клиент передумал.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// Менеджер, фактически отправивший сообщение через наш интерфейс.
    /// Обычно совпадает с владельцем канала, но расходится при работе через
    /// мультиаккаунт — когда один сотрудник отвечает из аккаунта другого.
    /// Null для входящих и для отправленных с телефона.
    /// </summary>
    public Guid? SentByManagerId { get; set; }

    /// <summary>Причина неудачной отправки — показывается менеджеру в интерфейсе.</summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Сколько раз очередь пыталась отправить. Нужен, чтобы «отравленное» сообщение
    /// не повторялось вечно: временная ошибка сети и невозможность отправить в принципе
    /// снаружи выглядят одинаково.
    /// </summary>
    public int SendAttempts { get; set; }

    /// <summary>
    /// Не пытаться отправить раньше этого времени. Заполняется, когда Telegram
    /// прямо говорит, сколько ждать (FLOOD_WAIT), — угадывать своими паузами
    /// в такой ситуации бессмысленно.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>
    /// Время исходного сообщения на платформе, а не время записи в БД.
    /// Иначе догрузка истории перемешает порядок переписки.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<MessageEditHistory> EditHistory { get; set; } = [];

    public ICollection<MediaAttachment> Attachments { get; set; } = [];
}
