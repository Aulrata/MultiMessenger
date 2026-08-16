using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MultiMessenger.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>
    /// Применяет ожидающие миграции при старте, чтобы деплой сводился к перезапуску
    /// контейнера без ручных шагов.
    /// <para>
    /// Работает, пока экземпляр приложения один. Если когда-нибудь появится вторая
    /// реплика, миграции придётся вынести в отдельный шаг деплоя — параллельный
    /// <c>Migrate()</c> из двух процессов приводит к гонке.
    /// </para>
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseMigrator));

        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("База данных актуальна, миграций к применению нет");
            return;
        }

        logger.LogInformation("Применяются миграции: {Migrations}", string.Join(", ", pending));

        await dbContext.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Миграции применены, всего {Count}", pending.Count);
    }
}
