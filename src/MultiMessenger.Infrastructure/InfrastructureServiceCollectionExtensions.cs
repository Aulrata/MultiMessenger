using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Identity;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Core.Storage;
using MultiMessenger.Infrastructure.Messengers;
using MultiMessenger.Infrastructure.Messengers.Inbox;
using MultiMessenger.Infrastructure.Messengers.Telegram;
using MultiMessenger.Infrastructure.Auditing;
using MultiMessenger.Infrastructure.Configuration;
using MultiMessenger.Infrastructure.Identity;
using MultiMessenger.Infrastructure.Media;
using MultiMessenger.Infrastructure.Persistence;
using MultiMessenger.Infrastructure.Storage;

namespace MultiMessenger.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMultiMessengerOptions(configuration);

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(DatabaseSettings.GetRequiredConnectionString(configuration)));

        services.AddScoped<IAuditTrail, EfAuditTrail>();

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddScoped<ManagerAuthenticator>();
        services.AddScoped<ManagerDirectory>();

        services.AddSingleton<IAmazonS3>(provider =>
            MinioSetup.CreateClient(provider.GetRequiredService<IOptions<MinioOptions>>().Value));
        services.AddScoped<IFileStorage, MinioFileStorage>();
        services.AddScoped<MediaAccessResolver>();

        // Фабрики коннекторов — синглтоны: сами по себе они без состояния,
        // состояние живёт в созданных ими подключениях.
        services.AddSingleton<IMessengerConnectorFactory, TelegramConnectorFactory>();

        // Живые подключения и приёмник событий переживают запросы, поэтому синглтоны.
        // Область для работы с БД они создают себе сами на каждое событие.
        services.AddSingleton<IMessengerEventSink, AccountEventSink>();
        services.AddSingleton<AccountConnectionManager>();
        services.AddSingleton<PendingLoginStore>();
        services.AddHostedService<AccountConnectionWorker>();
        services.AddHostedService<PendingLoginCleanupWorker>();

        services.AddSingleton<IWorkspaceNotifier, WorkspaceNotifier>();

        services.AddScoped<MessengerAccountService>();
        services.AddScoped<InboxService>();

        return services;
    }
}
