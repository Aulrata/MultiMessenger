using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Identity;
using MultiMessenger.Infrastructure.Configuration;
using MultiMessenger.Infrastructure.Persistence;

namespace MultiMessenger.Infrastructure.Identity;

public static class AdminSeeder
{
    /// <summary>
    /// Создаёт первого администратора, если в базе нет ни одного сотрудника.
    /// <para>
    /// Пароль берётся из конфигурации, а не генерируется: сгенерированный пришлось бы
    /// куда-то показать, а единственное доступное место — лог, куда паролям попадать
    /// не следует. Если секция не заполнена, приложение поднимется и объяснит в логе,
    /// что делать, — но войти будет некому.
    /// </para>
    /// </summary>
    public static async Task SeedFirstAdminAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminSeeder));

        if (await dbContext.Managers.AnyAsync(cancellationToken))
        {
            return;
        }

        var seedOptions = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;

        if (!seedOptions.IsConfigured)
        {
            logger.LogError(
                "В базе нет ни одного сотрудника, а секция Seed не заполнена — войти в систему невозможно. " +
                "Задайте Seed:AdminPhoneNumber и Seed:AdminPassword (локально через user-secrets, " +
                "на сервере через переменные Seed__AdminPhoneNumber и Seed__AdminPassword) и перезапустите приложение");
            return;
        }

        if (!PhoneNumber.TryNormalize(seedOptions.AdminPhoneNumber, out var normalizedPhone))
        {
            logger.LogError(
                "Seed:AdminPhoneNumber содержит номер в нераспознанном формате, администратор не создан");
            return;
        }

        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        dbContext.Managers.Add(new Manager
        {
            FullName = seedOptions.AdminFullName,
            PhoneNumber = normalizedPhone,
            PasswordHash = passwordHasher.Hash(seedOptions.AdminPassword!),
            Role = ManagerRole.Admin,
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Создан первый администратор {PhoneNumber}. Смените пароль после первого входа " +
            "и уберите секцию Seed из конфигурации",
            normalizedPhone);
    }
}
