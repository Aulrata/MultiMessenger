using Serilog;

namespace MultiMessenger.Web.Logging;

public static class SerilogConfiguration
{
    private const string SeqServerUrlKey = "Seq:ServerUrl";
    private const string SeqApiKeyKey = "Seq:ApiKey";

    /// <summary>
    /// Логгер на время старта приложения. Нужен, чтобы ошибки конфигурации
    /// (не задана строка подключения, не хватает ключей MinIO) попадали в лог,
    /// а не терялись — полноценный логгер к этому моменту ещё не собран.
    /// </summary>
    public static Serilog.ILogger CreateBootstrapLogger() =>
        new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

    /// <summary>
    /// Sink'и и уровни описаны в секции <c>Serilog</c> файла appsettings, чтобы менять их
    /// без пересборки. Seq подключается только если задан <c>Seq:ServerUrl</c> — по умолчанию
    /// пусто, и приложение не пытается достучаться до несуществующего сервера.
    /// </summary>
    public static void Configure(HostBuilderContext context, IServiceProvider services, LoggerConfiguration logger)
    {
        logger
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services);

        var seqServerUrl = context.Configuration[SeqServerUrlKey];

        if (!string.IsNullOrWhiteSpace(seqServerUrl))
        {
            logger.WriteTo.Seq(seqServerUrl, apiKey: context.Configuration[SeqApiKeyKey]);
        }
    }
}
