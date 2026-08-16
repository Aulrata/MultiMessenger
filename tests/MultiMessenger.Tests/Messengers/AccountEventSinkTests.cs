using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Messengers;
using MultiMessenger.Infrastructure.Persistence;
using MultiMessenger.Tests.Persistence;

namespace MultiMessenger.Tests.Messengers;

/// <summary>
/// От состояния канала зависит дашборд и решение, можно ли отправлять сообщения.
/// Поэтому проверяется на настоящей базе, вместе с записью в журнал состояний.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AccountEventSinkTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ConnectionMarksAccountActiveAndLogsEvent()
    {
        var account = await CreateAccountAsync(MessengerAccountStatus.Disconnected);
        var occurredAt = DateTimeOffset.UtcNow;

        await NewSink().OnConnectionStateChangedAsync(new ConnectionStateChanged
        {
            MessengerAccountId = account.Id,
            State = ConnectionState.Connected,
            OccurredAt = occurredAt,
        });

        await using var dbContext = postgres.CreateDbContext();

        var stored = await dbContext.MessengerAccounts.SingleAsync(item => item.Id == account.Id);
        stored.Status.Should().Be(MessengerAccountStatus.Active);
        stored.LastActiveAt.Should().BeCloseTo(occurredAt, TimeSpan.FromSeconds(1));
        stored.ConnectedAt.Should().NotBeNull();

        var log = await dbContext.AccountHealthLogs.SingleAsync(item => item.MessengerAccountId == account.Id);
        log.EventType.Should().Be(AccountHealthEventType.Connected);
    }

    /// <summary>
    /// Настоящий выход из аккаунта — не то же самое, что обрыв: переподключение
    /// его не починит, нужен повторный вход менеджера.
    /// </summary>
    [Fact]
    public async Task ReauthRequirementIsRecordedSeparatelyFromDisconnect()
    {
        var account = await CreateAccountAsync(MessengerAccountStatus.Active);

        await NewSink().OnConnectionStateChangedAsync(new ConnectionStateChanged
        {
            MessengerAccountId = account.Id,
            State = ConnectionState.RequiresReauth,
            OccurredAt = DateTimeOffset.UtcNow,
            Details = "AUTH_KEY_UNREGISTERED",
        });

        await using var dbContext = postgres.CreateDbContext();

        (await dbContext.MessengerAccounts.SingleAsync(item => item.Id == account.Id))
            .Status.Should().Be(MessengerAccountStatus.RequiresReauth);

        var log = await dbContext.AccountHealthLogs.SingleAsync(item => item.MessengerAccountId == account.Id);
        log.EventType.Should().Be(AccountHealthEventType.AuthExpired);
        log.Details.Should().Be("AUTH_KEY_UNREGISTERED");
    }

    [Fact]
    public async Task DisconnectKeepsConnectedAtButChangesStatus()
    {
        var account = await CreateAccountAsync(MessengerAccountStatus.Active);
        var sink = NewSink();

        await sink.OnConnectionStateChangedAsync(Change(account.Id, ConnectionState.Connected));
        await sink.OnConnectionStateChangedAsync(Change(account.Id, ConnectionState.Disconnected, "обрыв связи"));

        await using var dbContext = postgres.CreateDbContext();

        var stored = await dbContext.MessengerAccounts.SingleAsync(item => item.Id == account.Id);
        stored.Status.Should().Be(MessengerAccountStatus.Disconnected);
        stored.ConnectedAt.Should().NotBeNull("время первого подключения — история, его не стирают");

        var events = await dbContext.AccountHealthLogs
            .Where(item => item.MessengerAccountId == account.Id)
            .OrderBy(item => item.OccurredAt)
            .Select(item => item.EventType)
            .ToListAsync();

        events.Should().Equal(AccountHealthEventType.Connected, AccountHealthEventType.Disconnected);
    }

    /// <summary>
    /// Апдейт может прийти по каналу, который только что удалили. Это повод
    /// написать в лог, а не уронить обработку остальных событий.
    /// </summary>
    [Fact]
    public async Task EventForUnknownAccountIsIgnored()
    {
        var handle = async () => await NewSink().OnConnectionStateChangedAsync(
            Change(Guid.CreateVersion7(), ConnectionState.Connected));

        await handle.Should().NotThrowAsync();
    }

    private static ConnectionStateChanged Change(Guid accountId, ConnectionState state, string? details = null) => new()
    {
        MessengerAccountId = accountId,
        State = state,
        OccurredAt = DateTimeOffset.UtcNow,
        Details = details,
    };

    private AccountEventSink NewSink()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(postgres.ConnectionString));

        return new AccountEventSink(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AccountEventSink>.Instance);
    }

    private async Task<MessengerAccount> CreateAccountAsync(MessengerAccountStatus status)
    {
        await using var dbContext = postgres.CreateDbContext();

        var manager = TestData.NewManager("hash");
        var account = new MessengerAccount
        {
            ManagerId = manager.Id,
            Platform = MessengerPlatform.Telegram,
            PhoneNumber = manager.PhoneNumber,
            Status = status,
        };

        dbContext.Managers.Add(manager);
        dbContext.MessengerAccounts.Add(account);
        await dbContext.SaveChangesAsync();

        return account;
    }
}
