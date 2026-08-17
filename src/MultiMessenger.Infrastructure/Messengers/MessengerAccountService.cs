using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Identity;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Configuration;
using MultiMessenger.Infrastructure.Messengers.Telegram;
using MultiMessenger.Infrastructure.Persistence;

namespace MultiMessenger.Infrastructure.Messengers;

/// <summary>
/// Подключение и отключение каналов из интерфейса. Связывает три вещи:
/// запись в базе, живое соединение и незавершённую попытку входа.
/// </summary>
public sealed class MessengerAccountService(
    AppDbContext dbContext,
    IEnumerable<IMessengerConnectorFactory> factories,
    PendingLoginStore pendingLogins,
    AccountConnectionManager connectionManager,
    IMessengerEventSink eventSink,
    IAuditTrail auditTrail,
    IOptions<StorageOptions> storageOptions,
    ILogger<MessengerAccountService> logger)
{
    public Task<List<MessengerAccount>> GetAccountsAsync(Guid managerId, CancellationToken cancellationToken = default) =>
        dbContext.MessengerAccounts
            .Where(account => account.ManagerId == managerId)
            .OrderBy(account => account.Platform)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Начинает подключение канала. Возвращает идентификатор попытки и первый шаг:
    /// у Telegram это ввод кода, у других платформ будет иначе.
    /// </summary>
    public async Task<(Guid LoginId, LoginStep Step)> BeginLoginAsync(
        Guid managerId,
        MessengerPlatform platform,
        string? phoneNumber,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (factories.FirstOrDefault(factory => factory.Platform == platform) is not { } factory)
        {
            return (Guid.Empty, new LoginStep.Failed(LoginFailureReason.Unknown, $"Платформа {platform} пока не поддерживается"));
        }

        if (platform is MessengerPlatform.Telegram or MessengerPlatform.WhatsApp)
        {
            if (!PhoneNumber.TryNormalize(phoneNumber, out var normalized))
            {
                return (Guid.Empty, new LoginStep.Failed(LoginFailureReason.InvalidPhoneNumber, "Номер в нераспознанном формате"));
            }

            phoneNumber = normalized;
        }

        var account = await FindOrCreateAccountAsync(managerId, platform, phoneNumber, cancellationToken);

        // Если канал уже поднят, повторный вход только навредит: лишний перелогин
        // повышает подозрительность аккаунта для платформы.
        if (connectionManager.TryGet(account.Id, out _))
        {
            return (Guid.Empty, new LoginStep.Failed(LoginFailureReason.Unknown, "Канал уже подключён"));
        }

        IMessengerConnector connector;

        try
        {
            // Фабрика читает настройки платформы именно здесь. Незаполненные
            // api_id и api_hash — обычная ситуация на свежем сервере, и сотрудник
            // должен увидеть объяснение, а не страницу с трассировкой стека.
            connector = factory.Create(account.Id, eventSink);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Платформа {Platform} не настроена", platform);

            return (Guid.Empty, new LoginStep.Failed(
                LoginFailureReason.Unknown,
                $"{platform} не настроен на сервере — обратитесь к администратору"));
        }

        var login = pendingLogins.Start(managerId, account.Id, platform, connector);

        var step = await connector.BeginLoginAsync(
            new LoginRequest
            {
                MessengerAccountId = account.Id,
                PhoneNumber = phoneNumber,
            },
            cancellationToken);

        return await AfterStepAsync(login, step, ipAddress, cancellationToken);
    }

    /// <summary>Передаёт код подтверждения или пароль двухфакторной защиты.</summary>
    public async Task<(Guid LoginId, LoginStep Step)> ContinueLoginAsync(
        Guid managerId,
        Guid loginId,
        LoginAnswer answer,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (!pendingLogins.TryGet(loginId, managerId, out var login))
        {
            // Либо истёк таймаут, либо кто-то подставил чужой идентификатор.
            return (Guid.Empty, new LoginStep.Failed(LoginFailureReason.Expired, "Попытка подключения устарела, начните заново"));
        }

        var step = await login.Connector.ContinueLoginAsync(answer, cancellationToken);

        return await AfterStepAsync(login, step, ipAddress, cancellationToken);
    }

    public Task CancelLoginAsync(Guid managerId, Guid loginId)
    {
        return pendingLogins.TryGet(loginId, managerId, out _)
            ? pendingLogins.AbandonAsync(loginId)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Отключает канал: закрывает соединение, удаляет файл сессии и переводит
    /// запись в <see cref="MessengerAccountStatus.Disconnected"/>.
    /// </summary>
    public async Task<bool> DisconnectAsync(
        Guid managerId,
        Guid messengerAccountId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MessengerAccounts
            .SingleOrDefaultAsync(
                candidate => candidate.Id == messengerAccountId && candidate.ManagerId == managerId,
                cancellationToken);

        if (account is null)
        {
            return false;
        }

        await connectionManager.DisconnectAsync(messengerAccountId, cancellationToken);

        // Файл сессии — действующий ключ доступа к аккаунту. Отключили канал —
        // ключ должен исчезнуть, иначе он останется лежать на диске без надобности.
        if (account.Platform is MessengerPlatform.Telegram)
        {
            TelegramSessionStore.DeleteSession(storageOptions.Value.SessionsBasePath, account.Id);
        }

        account.Status = MessengerAccountStatus.Disconnected;
        account.SessionPath = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry
            {
                ManagerId = managerId,
                Action = AuditAction.MessengerAccountDisconnected,
                Subject = account.PhoneNumber,
                EntityType = nameof(MessengerAccount),
                EntityId = account.Id,
                DetailsJson = $$"""{"platform":"{{account.Platform}}"}""",
                IpAddress = ipAddress,
            },
            cancellationToken);

        return true;
    }

    /// <summary>
    /// Обрабатывает результат шага входа: успех переводит канал в рабочее состояние,
    /// отказ убирает попытку вместе с соединением.
    /// </summary>
    private async Task<(Guid LoginId, LoginStep Step)> AfterStepAsync(
        PendingLogin login,
        LoginStep step,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        switch (step)
        {
            case LoginStep.Completed completed:
                await CompleteAsync(login, completed, ipAddress, cancellationToken);
                return (Guid.Empty, step);

            case LoginStep.Failed:
                await pendingLogins.AbandonAsync(login.Id);
                return (Guid.Empty, step);

            default:
                return (login.Id, step);
        }
    }

    private async Task CompleteAsync(
        PendingLogin login,
        LoginStep.Completed completed,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.MessengerAccounts
            .SingleAsync(candidate => candidate.Id == login.MessengerAccountId, cancellationToken);

        account.Status = MessengerAccountStatus.Active;
        account.ConnectedAt = DateTimeOffset.UtcNow;
        account.LastActiveAt = DateTimeOffset.UtcNow;

        if (login.Platform is MessengerPlatform.Telegram)
        {
            account.SessionPath = TelegramSessionStore.GetSessionPath(
                storageOptions.Value.SessionsBasePath, account.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Соединение уже установлено и подписано на апдейты — переподключаться
        // заново незачем, менеджер подключений просто берёт его себе.
        pendingLogins.Release(login.Id);
        await connectionManager.AdoptAsync(account.Id, login.Connector);

        await auditTrail.RecordAsync(
            new AuditEntry
            {
                ManagerId = login.ManagerId,
                Action = AuditAction.MessengerAccountConnected,
                Subject = account.PhoneNumber,
                EntityType = nameof(MessengerAccount),
                EntityId = account.Id,
                DetailsJson = $$"""{"platform":"{{login.Platform}}","platformUserId":"{{completed.Account.PlatformUserId}}"}""",
                IpAddress = ipAddress,
            },
            cancellationToken);
    }

    /// <summary>
    /// На пару «сотрудник + платформа» стоит уникальный индекс, поэтому повторное
    /// подключение переиспользует прежнюю запись, а не заводит вторую.
    /// </summary>
    private async Task<MessengerAccount> FindOrCreateAccountAsync(
        Guid managerId,
        MessengerPlatform platform,
        string? phoneNumber,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.MessengerAccounts.SingleOrDefaultAsync(
            account => account.ManagerId == managerId && account.Platform == platform,
            cancellationToken);

        if (existing is not null)
        {
            existing.PhoneNumber = phoneNumber ?? existing.PhoneNumber;
            existing.Status = MessengerAccountStatus.PendingAuth;
            await dbContext.SaveChangesAsync(cancellationToken);

            return existing;
        }

        var account = new MessengerAccount
        {
            ManagerId = managerId,
            Platform = platform,
            PhoneNumber = phoneNumber,
            Status = MessengerAccountStatus.PendingAuth,
        };

        dbContext.MessengerAccounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);

        return account;
    }
}
