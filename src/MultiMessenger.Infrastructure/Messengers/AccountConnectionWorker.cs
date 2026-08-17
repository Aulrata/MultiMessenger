using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Persistence;

namespace MultiMessenger.Infrastructure.Messengers;

/// <summary>
/// Поднимает при старте приложения все каналы, которые были активны до перезапуска.
/// <para>
/// Отдельная фоновая служба, а не код в <c>Program</c>: подключение к Telegram
/// занимает секунды на канал, и держать из-за этого веб-сервер незапущенным незачем —
/// страница входа должна открываться сразу.
/// </para>
/// </summary>
public sealed class AccountConnectionWorker(
    IServiceScopeFactory scopeFactory,
    AccountConnectionManager connectionManager,
    ILogger<AccountConnectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Хост по умолчанию останавливается, если фоновая служба выбросила исключение.
        // Для веб-приложения это означало бы: один неисправный канал — и в систему
        // не может войти никто. Поэтому наружу отсюда не выходит ничего.
        try
        {
            await RestoreConnectionsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Обычная остановка приложения.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Восстановление подключений прервалось; приложение продолжает работу");
        }
    }

    private async Task RestoreConnectionsAsync(CancellationToken stoppingToken)
    {
        var accounts = await LoadRestorableAccountsAsync(stoppingToken);

        if (accounts.Count == 0)
        {
            logger.LogInformation("Каналов для восстановления нет");
            return;
        }

        logger.LogInformation("Восстанавливаю подключения, каналов: {Count}", accounts.Count);

        foreach (var (accountId, platform) in accounts)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                // Каналы поднимаются по очереди, а не разом: одновременный вход
                // с одного адреса по нескольким аккаунтам — заметный для платформы узор.
                var state = await connectionManager.ConnectAsync(accountId, platform, stoppingToken);

                if (state is not ConnectionState.Connected)
                {
                    logger.LogWarning("Канал {AccountId} не поднялся, состояние {State}", accountId, state);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // Один сломанный канал не должен мешать остальным подняться.
                logger.LogError(exception, "Канал {AccountId} пропущен из-за ошибки", accountId);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Корректное отключение при остановке приложения: иначе платформа увидит
        // обрыв, а не выход, и следующий вход будет выглядеть подозрительнее.
        await connectionManager.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }

    private async Task<List<(Guid AccountId, MessengerPlatform Platform)>> LoadRestorableAccountsAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Только те, что работали до перезапуска, и те, что оборвались. Каналы
        // в PendingAuth и RequiresReauth поднимать бессмысленно: там нужен человек.
        var restorable = new[] { MessengerAccountStatus.Active, MessengerAccountStatus.Disconnected };

        var accounts = await dbContext.MessengerAccounts
            .Where(account => restorable.Contains(account.Status))
            .Select(account => new { account.Id, account.Platform })
            .ToListAsync(cancellationToken);

        return accounts
            .Where(account => connectionManager.SupportsPlatform(account.Platform))
            .Select(account => (account.Id, account.Platform))
            .ToList();
    }
}
