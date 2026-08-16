using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MultiMessenger.Infrastructure.Configuration;

public static class ConfigurationServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует все типизированные настройки приложения.
    /// <para>
    /// Настройки, без которых сервис бесполезен (хранилище, пути к сессиям, БД),
    /// проверяются на старте: лучше не подняться сразу с понятной ошибкой, чем
    /// упасть через час на первом входящем сообщении.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMultiMessengerOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Бросит с подробным сообщением, если строка подключения не задана.
        DatabaseSettings.GetRequiredConnectionString(configuration);

        services.AddOptions<MinioOptions>()
            .Bind(configuration.GetSection(MinioOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Без ValidateOnStart — см. комментарий в TelegramOptions.
        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateDataAnnotations();

        // Без валидации вовсе: на работающей системе секции Seed не должно быть.
        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName));

        return services;
    }
}
