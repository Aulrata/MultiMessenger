using MultiMessenger.Infrastructure;
using MultiMessenger.Infrastructure.Persistence;
using MultiMessenger.Web.Components;
using MultiMessenger.Web.Logging;
using Serilog;

Log.Logger = SerilogConfiguration.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog(SerilogConfiguration.Configure);

    // Секреты локально приезжают из user-secrets (подключены хостом в Development),
    // на сервере — из переменных окружения вида Minio__SecretKey, ConnectionStrings__Postgres.
    builder.Services.AddInfrastructure(builder.Configuration);

    // Add services to the container.
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    var app = builder.Build();

    // Одна строка на HTTP-запрос вместо трёх от стандартного middleware ASP.NET
    app.UseSerilogRequestLogging();

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseHttpsRedirection();

    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    Log.Information("MultiMessenger запускается, окружение {Environment}", app.Environment.EnvironmentName);

    await app.Services.MigrateDatabaseAsync();

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
