using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Messaging;

/// <summary>Общая часть всего, что прилетает от платформы.</summary>
public abstract record PlatformEvent
{
    /// <summary>Через какой канал менеджера пришло событие.</summary>
    public required Guid MessengerAccountId { get; init; }

    /// <summary>Собеседник на стороне платформы.</summary>
    public required string PlatformUserId { get; init; }

    /// <summary>Время по часам платформы, а не время получения нами.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary>
/// Новое сообщение в переписке.
/// <para>
/// Событие приходит и на чужие сообщения, и на свои: менеджер может писать клиенту
/// с телефона, и такое сообщение обязано появиться в системе, иначе в истории
/// образуются дыры. Отличать одно от другого — задача <see cref="Direction"/>.
/// </para>
/// </summary>
public sealed record IncomingMessage : PlatformEvent
{
    public required string PlatformMessageId { get; init; }

    /// <summary>
    /// <see cref="MessageDirection.Outgoing"/> означает, что сообщение отправлено
    /// с этого же аккаунта — из нашей очереди или вручную с телефона.
    /// Какой именно случай, решается сопоставлением по <see cref="PlatformMessageId"/>.
    /// </summary>
    public required MessageDirection Direction { get; init; }

    public string? Text { get; init; }

    /// <summary>Имя или username собеседника на платформе — для карточки контакта.</summary>
    public string? SenderDisplayName { get; init; }

    public IReadOnlyList<IncomingAttachment> Attachments { get; init; } = [];
}

/// <summary>
/// Вложение входящего сообщения. Содержимого здесь нет — только описание и ссылка
/// для скачивания через <see cref="IMessengerConnector.DownloadAttachmentAsync"/>.
/// Так загрузку можно отложить и не держать в памяти файлы, которые, возможно,
/// никто не откроет.
/// </summary>
public sealed record IncomingAttachment
{
    public required MediaType Type { get; init; }

    /// <summary>Идентификатор файла на стороне платформы.</summary>
    public required string PlatformFileReference { get; init; }

    public string? FileName { get; init; }

    public string? MimeType { get; init; }

    public long? SizeBytes { get; init; }

    public int? DurationSeconds { get; init; }
}

/// <summary>Сообщение исправлено — кем угодно, включая самого менеджера с телефона.</summary>
public sealed record MessageEdited : PlatformEvent
{
    public required string PlatformMessageId { get; init; }

    public string? NewText { get; init; }
}

/// <summary>
/// Сообщение удалено на стороне платформы. У нас оно останется с пометкой
/// <c>IsDeleted</c>, а текст уедет в журнал изменений.
/// </summary>
public sealed record MessageDeleted : PlatformEvent
{
    public required string PlatformMessageId { get; init; }
}

/// <summary>
/// Сообщения прочитаны. Приходит и когда менеджер читает переписку в официальном
/// приложении, — без этого счётчики непрочитанных в системе разойдутся с реальностью.
/// </summary>
public sealed record ReadStatusChanged : PlatformEvent
{
    /// <summary>
    /// Платформы обычно сообщают «прочитано всё до этого момента», а не список
    /// идентификаторов. Отмечаем прочитанными все сообщения диалога до этого времени.
    /// </summary>
    public required DateTimeOffset ReadUpTo { get; init; }

    /// <summary>
    /// Прочитал собеседник (тогда обновляются статусы наших исходящих)
    /// или сам менеджер (тогда гасится счётчик непрочитанных).
    /// </summary>
    public required bool ReadByCounterparty { get; init; }
}

public enum ConnectionState
{
    Connected,
    Disconnected,

    /// <summary>Сессия невалидна: настоящий выход из аккаунта, а не обрыв связи.</summary>
    RequiresReauth,
}

/// <summary>
/// Изменение состояния соединения. В отличие от прочих событий не относится
/// к конкретному собеседнику, поэтому наследуется отдельно.
/// </summary>
public sealed record ConnectionStateChanged
{
    public required Guid MessengerAccountId { get; init; }

    public required ConnectionState State { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Причина — уезжает в <c>AccountHealthLog.Details</c> и на дашборд.</summary>
    public string? Details { get; init; }
}
