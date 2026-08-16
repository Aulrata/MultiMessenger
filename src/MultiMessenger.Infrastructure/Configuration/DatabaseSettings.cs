using Microsoft.Extensions.Configuration;

namespace MultiMessenger.Infrastructure.Configuration;

/// <summary>
/// Строка подключения к PostgreSQL живёт в стандартной секции <c>ConnectionStrings</c>,
/// а не в отдельном options-классе, — так её понимают <c>dotnet ef</c> и остальная инфраструктура.
/// Здесь только имя ключа и явная проверка, чтобы приложение падало на старте с внятным
/// сообщением, а не с <c>ArgumentNullException</c> где-то внутри Npgsql.
/// </summary>
public static class DatabaseSettings
{
    public const string ConnectionStringName = "Postgres";

    /// <summary>Переменная окружения, которой строка задаётся на сервере.</summary>
    public const string EnvironmentVariableName = "ConnectionStrings__Postgres";

    public static string GetRequiredConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Не задана строка подключения ConnectionStrings:{ConnectionStringName}. " +
                $"Локально: dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" \"Host=localhost;...\" " +
                $"--project src/MultiMessenger.Web. " +
                $"На сервере: переменная окружения {EnvironmentVariableName}.");
        }

        return connectionString;
    }
}
