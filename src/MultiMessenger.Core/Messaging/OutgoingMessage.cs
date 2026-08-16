using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Messaging;

/// <summary>Сообщение, которое очередь отправки передаёт коннектору.</summary>
public sealed record OutgoingMessage
{
    /// <summary>
    /// Наш идентификатор записи в БД. Возвращается вместе с результатом,
    /// чтобы обновить именно её, не гадая по тексту и времени.
    /// </summary>
    public required Guid MessageId { get; init; }

    /// <summary>Получатель на стороне платформы.</summary>
    public required string PlatformUserId { get; init; }

    public string? Text { get; init; }

    public IReadOnlyList<OutgoingAttachment> Attachments { get; init; } = [];
}

/// <summary>
/// Вложение исходящего сообщения. Содержимое отдаётся не потоком, а функцией,
/// открывающей его: файл лежит в MinIO, и качать его стоит в момент отправки,
/// а не когда сообщение только встало в очередь.
/// </summary>
public sealed record OutgoingAttachment
{
    public required MediaType Type { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required Func<CancellationToken, Task<Stream>> OpenAsync { get; init; }

    /// <summary>Длительность голосового или видео — платформы просят её при отправке.</summary>
    public int? DurationSeconds { get; init; }
}

public sealed record EditMessageRequest
{
    public required Guid MessageId { get; init; }

    public required string PlatformUserId { get; init; }

    public required string PlatformMessageId { get; init; }

    public required string NewText { get; init; }
}

public sealed record DeleteMessageRequest
{
    public required Guid MessageId { get; init; }

    public required string PlatformUserId { get; init; }

    public required string PlatformMessageId { get; init; }

    /// <summary>
    /// Удалять у собеседника, а не только у себя. По принятому решению система
    /// всегда удаляет у всех; флаг оставлен для платформ, которые так не умеют, —
    /// см. <see cref="PlatformCapabilities.SupportsDeleteForEveryone"/>.
    /// </summary>
    public bool ForEveryone { get; init; } = true;
}

public enum DeliveryFailureReason
{
    /// <summary>Платформа просит подождать. Длительность — в <see cref="DeliveryResult.RetryAfter"/>.</summary>
    RateLimited,

    /// <summary>Сессия больше не годится, аккаунту нужен повторный вход.</summary>
    AuthenticationExpired,

    /// <summary>Получателя не существует или он заблокировал отправителя.</summary>
    RecipientUnavailable,

    /// <summary>Истекло окно, в течение которого правка или удаление ещё возможны.</summary>
    WindowExpired,

    /// <summary>Сообщения уже нет на стороне платформы.</summary>
    MessageNotFound,

    NetworkError,

    Unknown,
}

/// <summary>
/// Итог отправки, правки или удаления — операции разные, но исходы у них одинаковые.
/// </summary>
public sealed record DeliveryResult
{
    public required Guid MessageId { get; init; }

    public required bool Succeeded { get; init; }

    /// <summary>Присвоенный платформой идентификатор. Заполняется только при отправке.</summary>
    public string? PlatformMessageId { get; init; }

    /// <summary>Время по часам платформы — по нему сортируется переписка.</summary>
    public DateTimeOffset? AcceptedAt { get; init; }

    public DeliveryFailureReason? FailureReason { get; init; }

    /// <summary>Текст для показа менеджеру и для колонки <c>Message.FailureReason</c>.</summary>
    public string? FailureDetails { get; init; }

    /// <summary>
    /// Сколько ждать до следующей попытки. Заполняется при
    /// <see cref="DeliveryFailureReason.RateLimited"/> — платформы вроде Telegram
    /// прямо называют это время, и угадывать его своими паузами не нужно.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    public static DeliveryResult Success(Guid messageId, string platformMessageId, DateTimeOffset acceptedAt) =>
        new()
        {
            MessageId = messageId,
            Succeeded = true,
            PlatformMessageId = platformMessageId,
            AcceptedAt = acceptedAt,
        };

    public static DeliveryResult Failure(
        Guid messageId,
        DeliveryFailureReason reason,
        string? details = null,
        TimeSpan? retryAfter = null) =>
        new()
        {
            MessageId = messageId,
            Succeeded = false,
            FailureReason = reason,
            FailureDetails = details,
            RetryAfter = retryAfter,
        };
}
