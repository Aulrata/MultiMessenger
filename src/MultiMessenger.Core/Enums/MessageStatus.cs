namespace MultiMessenger.Core.Enums;

/// <summary>
/// Статус сообщения. Для исходящих описывает путь через очередь отправки,
/// для входящих используются только <see cref="Delivered"/> и <see cref="Read"/> —
/// по ним считается счётчик непрочитанных в списке диалогов.
/// </summary>
public enum MessageStatus
{
    /// <summary>Исходящее принято системой, ждёт своей очереди в Outbox.</summary>
    Pending,

    /// <summary>Отдано платформе, получен <c>PlatformMessageId</c>.</summary>
    Sent,

    /// <summary>Доставлено получателю.</summary>
    Delivered,

    /// <summary>Прочитано.</summary>
    Read,

    /// <summary>Отправить не удалось, причина в <c>Message.FailureReason</c>.</summary>
    Failed,
}
