using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Infrastructure.Auditing;
using MultiMessenger.Infrastructure.Configuration;
using MultiMessenger.Infrastructure.Persistence;

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

        return services;
    }
}
