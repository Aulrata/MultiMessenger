using Microsoft.AspNetCore.DataProtection;
using MultiMessenger.Infrastructure.Configuration;

namespace MultiMessenger.Web.Security;

public static class DataProtectionSetup
{
    /// <summary>
    /// Кладёт ключи Data Protection в постоянное хранилище, если путь задан.
    /// <para>
    /// По умолчанию ASP.NET хранит их в профиле пользователя внутри контейнера,
    /// а выкатка контейнер пересоздаёт. Без этой настройки каждое обновление
    /// разлогинивало бы всех менеджеров и ломало открытые формы.
    /// </para>
    /// <para>
    /// Имя приложения задаётся явно: иначе оно выводится из пути к сборке,
    /// и ключи, записанные одной версией образа, не подойдут следующей.
    /// </para>
    /// </summary>
    public static IServiceCollection AddPersistentDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var keysPath = configuration[$"{StorageOptions.SectionName}:{nameof(StorageOptions.DataProtectionKeysPath)}"];

        var dataProtection = services.AddDataProtection()
            .SetApplicationName("MultiMessenger");

        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            var directory = Directory.CreateDirectory(keysPath);
            dataProtection.PersistKeysToFileSystem(directory);
        }

        return services;
    }
}
