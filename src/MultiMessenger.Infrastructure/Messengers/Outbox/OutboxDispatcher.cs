using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Configuration;
using MultiMessenger.Infrastructure.Persistence;

namespace MultiMessenger.Infrastructure.Messengers.Outbox;

public enum DispatchOutcome
{
    /// <summary>Отправлять было нечего.</summary>
    Idle,

    Sent,

    /// <summary>Не удалось, но попытка будет повторена.</summary>
    Deferred,

    /// <summary>Исчерпаны попытки либо ошибка неустранима — сообщение помечено неудачным.</summary>
    Failed,

    /// <summary>Канал не подключён: ждём, пока поднимется.</summary>
    ChannelOffline,

    /// <summary>Пауза между отправками ещё не вышла.</summary>
    Throttled,
}

/// <summary>
/// Что произошло с конкретным сообщением за проход очереди. Идентификатор нужен
/// и для разбора логов, и чтобы отличать свои сообщения от чужих.
/// </summary>
public sealed record DispatchAttempt(Guid MessageId, DispatchOutcome Outcome);

/// <summary>
/// Один проход очереди исходящих: берёт по одному готовому сообщению на канал
/// и отдаёт его платформе.
/// <para>
/// Вынесено из фоновой службы отдельным классом, чтобы поведение — паузы, повторы,
/// разбор ошибок — проверялось тестами без запуска хоста и ожидания таймеров.
/// </para>
/// </summary>
public sealed class OutboxDispatcher(
    AppDbContext dbContext,
    AccountConnectionManager connections,
    OutboxRateLimiter rateLimiter,
    IWorkspaceNotifier notifier,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger)
{
    private readonly OutboxOptions _options = options.Value;

    /// <summary>
    /// Обрабатывает по одному сообщению на каждый канал, у которого есть готовые.
    /// По одному — потому что между отправками нужна пауза, и разбирать всю очередь
    /// разом всё равно нельзя.
    /// </summary>
    public async Task<IReadOnlyList<DispatchAttempt>> DispatchDueAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        var due = await dbContext.Messages
            .Where(message => message.Status == MessageStatus.Pending
                              && (message.NextAttemptAt == null || message.NextAttemptAt <= now))
            .OrderBy(message => message.CreatedAt)
            .Select(message => new { message.Id, AccountId = message.Dialog!.MessengerAccountId })
            .ToListAsync(cancellationToken);

        var attempts = new List<DispatchAttempt>();

        foreach (var group in due.GroupBy(item => item.AccountId))
        {
            var oldest = group.First();

            attempts.Add(new DispatchAttempt(oldest.Id, await DispatchOneAsync(oldest.Id, cancellationToken)));
        }

        return attempts;
    }

    public async Task<DispatchOutcome> DispatchOneAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(messageId, cancellationToken);

        if (context is null)
        {
            return DispatchOutcome.Idle;
        }

        var (message, accountId, platform, platformUserId) = context;

        if (platformUserId is null)
        {
            // Диалог есть, а идентификатора собеседника на этой платформе нет —
            // отправлять физически некуда, повторы не помогут.
            return await MarkFailedAsync(message, "У контакта нет идентификатора на этой платформе", cancellationToken);
        }

        if (!connections.TryGet(accountId, out var connector))
        {
            logger.LogDebug("Канал {AccountId} не подключён, сообщение ждёт", accountId);
            return DispatchOutcome.ChannelOffline;
        }

        if (!rateLimiter.IsAllowed(accountId, platformUserId, _options))
        {
            return DispatchOutcome.Throttled;
        }

        var result = await connector.SendMessageAsync(
            new OutgoingMessage
            {
                MessageId = message.Id,
                PlatformUserId = platformUserId,
                Text = message.Text,
                // Вложения появятся на этапе 2.9 вместе с загрузкой медиа.
                Attachments = [],
            },
            cancellationToken);

        // Отмечаем обращение к платформе независимо от исхода: для её лимитов
        // неудачная попытка ничем не отличается от удачной.
        rateLimiter.RecordSend(accountId, platformUserId);

        message.SendAttempts++;

        return result.Succeeded
            ? await MarkSentAsync(message, result, platform, cancellationToken)
            : await HandleFailureAsync(message, result, cancellationToken);
    }

    private async Task<DispatchOutcome> MarkSentAsync(
        Core.Entities.Message message,
        DeliveryResult result,
        MessengerPlatform platform,
        CancellationToken cancellationToken)
    {
        message.Status = MessageStatus.Sent;
        message.PlatformMessageId = result.PlatformMessageId;
        message.FailureReason = null;
        message.NextAttemptAt = null;

        // Время по часам платформы: по нему сортируется переписка, и расхождение
        // с нашими часами перемешало бы порядок сообщений.
        if (result.AcceptedAt is { } acceptedAt)
        {
            message.CreatedAt = acceptedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAsync(message, WorkspaceEventKind.MessageSent, platform, cancellationToken);

        return DispatchOutcome.Sent;
    }

    private async Task<DispatchOutcome> HandleFailureAsync(
        Core.Entities.Message message,
        DeliveryResult result,
        CancellationToken cancellationToken)
    {
        var reason = result.FailureReason ?? DeliveryFailureReason.Unknown;
        var details = result.FailureDetails ?? reason.ToString();

        if (!IsTransient(reason) || message.SendAttempts >= _options.MaxAttempts)
        {
            return await MarkFailedAsync(message, details, cancellationToken);
        }

        // Если платформа сама назвала срок ожидания — он важнее наших расчётов.
        message.NextAttemptAt = timeProvider.GetUtcNow()
                                + (result.RetryAfter ?? _options.BackoffFor(message.SendAttempts));
        message.FailureReason = details;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Сообщение {MessageId} отложено до {NextAttempt}, попытка {Attempt}: {Reason}",
            message.Id,
            message.NextAttemptAt,
            message.SendAttempts,
            reason);

        return DispatchOutcome.Deferred;
    }

    private async Task<DispatchOutcome> MarkFailedAsync(
        Core.Entities.Message message,
        string details,
        CancellationToken cancellationToken)
    {
        message.Status = MessageStatus.Failed;
        message.FailureReason = details;
        message.NextAttemptAt = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        var platform = await dbContext.Dialogs
            .Where(dialog => dialog.Id == message.DialogId)
            .Select(dialog => dialog.Platform)
            .SingleAsync(cancellationToken);

        // Менеджер должен увидеть неудачу сразу, а не обнаружить её через час.
        await NotifyAsync(message, WorkspaceEventKind.MessageSent, platform, cancellationToken);

        logger.LogWarning("Сообщение {MessageId} не отправлено: {Details}", message.Id, details);

        return DispatchOutcome.Failed;
    }

    /// <summary>
    /// Ошибки, которые имеет смысл повторять. Заблокировавший отправителя клиент
    /// или недействительная сессия сами не исправятся.
    /// </summary>
    private static bool IsTransient(DeliveryFailureReason reason) => reason
        is DeliveryFailureReason.RateLimited
        or DeliveryFailureReason.NetworkError;

    private async Task NotifyAsync(
        Core.Entities.Message message,
        WorkspaceEventKind kind,
        MessengerPlatform platform,
        CancellationToken cancellationToken)
    {
        var addressee = await dbContext.Dialogs
            .Where(dialog => dialog.Id == message.DialogId)
            .Select(dialog => new { dialog.ContactId, dialog.MessengerAccount!.ManagerId })
            .SingleAsync(cancellationToken);

        await notifier.NotifyAsync(
            new WorkspaceNotification
            {
                ManagerId = addressee.ManagerId,
                DialogId = message.DialogId,
                ContactId = addressee.ContactId,
                Platform = platform,
                MessageId = message.Id,
                Kind = kind,
            },
            cancellationToken);
    }

    private async Task<MessageContext?> LoadAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages
            .SingleOrDefaultAsync(item => item.Id == messageId && item.Status == MessageStatus.Pending, cancellationToken);

        if (message is null)
        {
            return null;
        }

        var route = await dbContext.Dialogs
            .Where(dialog => dialog.Id == message.DialogId)
            .Select(dialog => new
            {
                dialog.MessengerAccountId,
                dialog.Platform,
                PlatformUserId = dialog.Contact!.Identities
                    .Where(identity => identity.Platform == dialog.Platform)
                    .Select(identity => identity.PlatformUserId)
                    .FirstOrDefault(),
            })
            .SingleAsync(cancellationToken);

        return new MessageContext(message, route.MessengerAccountId, route.Platform, route.PlatformUserId);
    }

    private sealed record MessageContext(
        Core.Entities.Message Message,
        Guid AccountId,
        MessengerPlatform Platform,
        string? PlatformUserId);
}
