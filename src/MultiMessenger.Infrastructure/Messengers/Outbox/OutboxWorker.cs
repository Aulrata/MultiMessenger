using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiMessenger.Infrastructure.Configuration;

namespace MultiMessenger.Infrastructure.Messengers.Outbox;

/// <summary>
/// Фоновая служба очереди исходящих. Вся логика — в <see cref="OutboxDispatcher"/>;
/// здесь только таймер и защита от того, чтобы исключение не остановило приложение.
/// </summary>
public sealed class OutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);

        logger.LogInformation(
            "Очередь исходящих запущена: пауза между получателями {Between} с, внутри диалога {Within} с",
            options.Value.DelayBetweenRecipientsSeconds,
            options.Value.DelayWithinDialogSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();

                await dispatcher.DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Ошибка одного прохода не должна ни останавливать очередь,
                // ни тем более ронять хост вместе с веб-интерфейсом.
                logger.LogError(exception, "Проход очереди исходящих завершился ошибкой");
            }
        }
    }
}
