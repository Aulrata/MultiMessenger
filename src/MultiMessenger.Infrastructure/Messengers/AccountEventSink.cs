using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Messengers.Inbox;
using MultiMessenger.Infrastructure.Persistence;

namespace MultiMessenger.Infrastructure.Messengers;

/// <summary>
/// Приёмник событий платформы, сохраняющий их в базу.
/// <para>
/// Живёт как синглтон рядом с коннекторами, поэтому <c>AppDbContext</c> получает
/// не через конструктор, а через собственную область на каждое событие: контекст
/// не потокобезопасен, а апдейты приходят параллельно по всем каналам.
/// </para>
/// <para>
/// Сейчас обрабатываются только события соединения — это и есть задача этапа 2.3.
/// Ветки сообщений заполняются на 2.5, когда появится логика Inbox; до тех пор
/// они пишут в лог, а не молчат, чтобы потерянные события были заметны.
/// </para>
/// </summary>
public sealed class AccountEventSink(
    IServiceScopeFactory scopeFactory,
    ILogger<AccountEventSink> logger) : IMessengerEventSink
{
    public async Task OnMessageReceivedAsync(IncomingMessage message, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<InboxService>();

        var result = await inbox.HandleAsync(message, cancellationToken);

        // Идентификатор клиента на платформе в лог не пишется: логи уезжают
        // в файлы и Seq, а это персональные данные. Для диагностики достаточно
        // канала и номера сообщения.
        logger.LogDebug(
            "Сообщение {PlatformMessageId} по каналу {AccountId}: {Outcome}",
            message.PlatformMessageId,
            message.MessengerAccountId,
            result.Outcome);
    }

    public Task OnMessageEditedAsync(MessageEdited edit, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Сообщение {PlatformMessageId} отредактировано; обработка появится на этапе 2.10",
            edit.PlatformMessageId);

        return Task.CompletedTask;
    }

    public Task OnMessageDeletedAsync(MessageDeleted deletion, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Сообщение {PlatformMessageId} удалено; обработка появится на этапе 2.10",
            deletion.PlatformMessageId);

        return Task.CompletedTask;
    }

    public Task OnReadStatusChangedAsync(ReadStatusChanged change, CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Изменились статусы прочтения по каналу {AccountId}; обработка появится на этапе 2.10",
            change.MessengerAccountId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Единственное, что обрабатывается полностью: состояние канала. От него зависит
    /// дашборд и решение, можно ли отправлять сообщения.
    /// </summary>
    public async Task OnConnectionStateChangedAsync(
        ConnectionStateChanged change,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = await dbContext.MessengerAccounts
            .SingleOrDefaultAsync(candidate => candidate.Id == change.MessengerAccountId, cancellationToken);

        if (account is null)
        {
            logger.LogWarning("Событие соединения по неизвестному каналу {AccountId}", change.MessengerAccountId);
            return;
        }

        account.Status = change.State switch
        {
            ConnectionState.Connected => MessengerAccountStatus.Active,
            ConnectionState.RequiresReauth => MessengerAccountStatus.RequiresReauth,
            _ => MessengerAccountStatus.Disconnected,
        };

        if (change.State is ConnectionState.Connected)
        {
            account.LastActiveAt = change.OccurredAt;
            account.ConnectedAt ??= change.OccurredAt;
        }

        dbContext.AccountHealthLogs.Add(new AccountHealthLog
        {
            MessengerAccountId = account.Id,
            EventType = change.State switch
            {
                ConnectionState.Connected => AccountHealthEventType.Connected,
                ConnectionState.RequiresReauth => AccountHealthEventType.AuthExpired,
                _ => AccountHealthEventType.Disconnected,
            },
            Details = change.Details,
            OccurredAt = change.OccurredAt,
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        if (change.State is ConnectionState.RequiresReauth)
        {
            // Само по себе это не чинится: нужен повторный вход менеджера.
            // На 2.11 отсюда пойдёт уведомление в интерфейс.
            logger.LogWarning(
                "Канал {AccountId} требует повторного входа: {Details}", account.Id, change.Details);
        }
    }
}
