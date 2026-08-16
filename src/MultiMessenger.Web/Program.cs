using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Infrastructure;
using MultiMessenger.Infrastructure.Identity;
using MultiMessenger.Infrastructure.Persistence;
using MultiMessenger.Infrastructure.Storage;
using MultiMessenger.Web.Components;
using MultiMessenger.Web.Endpoints;
using MultiMessenger.Web.Logging;
using MultiMessenger.Web.Security;
using Serilog;

Log.Logger = SerilogConfiguration.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog(SerilogConfiguration.Configure);

    // Секреты локально приезжают из user-secrets (подключены хостом в Development),
    // на сервере — из переменных окружения вида Minio__SecretKey, ConnectionStrings__Postgres.
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddManagerAuthentication();
    builder.Services.AddPersistentDataProtection(builder.Configuration);

    // Живость и готовность разделены: контейнеру нужно знать, что процесс не завис,
    // а балансировщику и деплою — что приложение реально способно работать.
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"])
        .AddCheck<MinioHealthCheck>("minio", tags: ["ready"]);

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
    builder.Services.AddCascadingAuthenticationState();

    var app = builder.Build();

    // Одна строка на HTTP-запрос вместо трёх от стандартного middleware ASP.NET
    app.UseSerilogRequestLogging();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseHttpsRedirection();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.MapMediaEndpoints();

    // Процесс жив и отвечает. Без обращений к БД и хранилищу: при недоступной базе
    // контейнер перезапускать бессмысленно, перезапуск её не починит.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
        .AllowAnonymous();

    // Зависимости на месте, можно пускать трафик. Эту ручку дёргает деплой.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
        })
        .AllowAnonymous();

    // Выход — обязательно POST с antiforgery-токеном: по GET-ссылке чужой сайт
    // мог бы разлогинивать сотрудника картинкой.
    app.MapPost(AuthenticationSetup.LogoutPath, async (
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IAuditTrail auditTrail) =>
    {
        await antiforgery.ValidateRequestAsync(httpContext);

        var managerId = httpContext.User.GetManagerId();

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        if (managerId is not null)
        {
            await auditTrail.RecordAsync(new AuditEntry
            {
                ManagerId = managerId,
                Action = AuditAction.ManagerSignedOut,
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            });
        }

        return Results.LocalRedirect(AuthenticationSetup.LoginPath);
    });

    Log.Information("MultiMessenger запускается, окружение {Environment}", app.Environment.EnvironmentName);

    await app.Services.MigrateDatabaseAsync();
    await app.Services.EnsureMediaBucketAsync();
    await app.Services.SeedFirstAdminAsync();

    await app.RunAsync();
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "Приложение остановлено: ошибка при старте");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
