using MultiMessenger.Core.Enums;

namespace MultiMessenger.Infrastructure.Workspace;

/// <summary>
/// Строка списка слева. Список строится по контактам, а не по диалогам: у клиента
/// может быть несколько платформ, и в интерфейсе он остаётся одним человеком
/// (см. docs/Интерфейс_переписки.md).
/// </summary>
public sealed record ContactSummary
{
    public required Guid ContactId { get; init; }

    public required string DisplayName { get; init; }

    public string? CrmOrderNumber { get; init; }

    public required DateTimeOffset? LastMessageAt { get; init; }

    public required int UnreadCount { get; init; }

    /// <summary>Последнее сообщение — для второй строки в списке, как в Telegram.</summary>
    public string? LastMessagePreview { get; init; }

    /// <summary>По одной записи на платформу. Одна — открывается сразу, несколько — раскрывается.</summary>
    public required IReadOnlyList<DialogSummary> Dialogs { get; init; }

    public bool HasSeveralPlatforms => Dialogs.Count > 1;
}

public sealed record DialogSummary
{
    public required Guid DialogId { get; init; }

    public required MessengerPlatform Platform { get; init; }

    public required DateTimeOffset? LastMessageAt { get; init; }

    public required int UnreadCount { get; init; }

    public required MessengerAccountStatus ChannelStatus { get; init; }
}

/// <summary>Сообщение в окне переписки.</summary>
public sealed record MessageView
{
    public required Guid MessageId { get; init; }

    public required MessageDirection Direction { get; init; }

    public string? Text { get; init; }

    public required MessageStatus Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required bool IsEdited { get; init; }

    public required bool IsDeleted { get; init; }

    /// <summary>Причина неудачной отправки — показывается менеджеру прямо в переписке.</summary>
    public string? FailureReason { get; init; }

    public required int AttachmentCount { get; init; }
}

/// <summary>Страница истории переписки. Загружается от новых к старым.</summary>
public sealed record MessagePage
{
    public required IReadOnlyList<MessageView> Messages { get; init; }

    /// <summary>Есть ли что подгружать выше.</summary>
    public required bool HasMore { get; init; }
}

/// <summary>Карточка контакта справа.</summary>
public sealed record ContactCard
{
    public required Guid ContactId { get; init; }

    public required string DisplayName { get; init; }

    public string? CrmOrderNumber { get; init; }

    public string? Notes { get; init; }

    public required MessengerPlatform PrimaryPlatform { get; init; }

    public required IReadOnlyList<ContactChannel> Channels { get; init; }
}

public sealed record ContactChannel
{
    public required MessengerPlatform Platform { get; init; }

    public string? DisplayNameOnPlatform { get; init; }
}
