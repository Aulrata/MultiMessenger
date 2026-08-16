using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MultiMessenger.Infrastructure.Messengers;

/// <summary>
/// Убирает брошенные попытки подключения. За каждой стоит открытый сокет
/// к платформе, поэтому оставлять их до перезапуска приложения нельзя.
/// </summary>
public sealed class PendingLoginCleanupWorker(
    PendingLoginStore store,
    ILogger<PendingLoginCleanupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var removed = await store.RemoveExpiredAsync(DateTimeOffset.UtcNow);

                if (removed > 0)
                {
                    logger.LogInformation("Очищено брошенных попыток подключения: {Count}", removed);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Обычная остановка приложения.
        }
        catch (Exception exception)
        {
            // Хост по умолчанию останавливается на исключении из фоновой службы.
            // Уборка попыток входа этого не стоит.
            logger.LogError(exception, "Очистка попыток подключения прервалась");
        }
    }
}
