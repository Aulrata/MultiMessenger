using Microsoft.EntityFrameworkCore;
using MultiMessenger.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace MultiMessenger.Tests.Persistence;

/// <summary>
/// Поднимает пустой PostgreSQL в контейнере на время прогона. Проверять модель
/// на разработческой БД бессмысленно: там миграции уже применены, и ошибка
/// «работает только на существующей схеме» останется незамеченной.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    // Версия образа совпадает с docker-compose: тестировать на другой мажорной
    // версии Postgres, чем работает прод, — верный способ пропустить различие в поведении.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
