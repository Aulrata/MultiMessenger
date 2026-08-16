using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Messengers;
using MultiMessenger.Tests.Messaging;
using NSubstitute;

namespace MultiMessenger.Tests.Messengers;

public class AccountConnectionManagerTests
{
    private readonly IMessengerEventSink _sink = Substitute.For<IMessengerEventSink>();

    [Fact]
    public async Task ConnectedAccountBecomesAvailable()
    {
        var manager = NewManager(out _);
        var accountId = Guid.CreateVersion7();

        var state = await manager.ConnectAsync(accountId, MessengerPlatform.Telegram);

        state.Should().Be(ConnectionState.Connected);
        manager.TryGet(accountId, out var connector).Should().BeTrue();
        connector.Should().NotBeNull();
        manager.ConnectedAccounts.Should().ContainSingle();
    }

    /// <summary>
    /// Два коннектора на один канал означали бы две сессии Telegram. Повторный
    /// вызов обязан вернуть уже поднятое соединение, а не создавать новое.
    /// </summary>
    [Fact]
    public async Task SecondConnectDoesNotCreateAnotherConnector()
    {
        var manager = NewManager(out var factory);
        var accountId = Guid.CreateVersion7();

        await manager.ConnectAsync(accountId, MessengerPlatform.Telegram);
        await manager.ConnectAsync(accountId, MessengerPlatform.Telegram);

        factory.CreatedCount.Should().Be(1);
    }

    /// <summary>
    /// То же самое при гонке: фоновый воркер и подключение через интерфейс
    /// вполне могут стартовать одновременно.
    /// </summary>
    [Fact]
    public async Task ConcurrentConnectsCreateSingleConnector()
    {
        var manager = NewManager(out var factory);
        var accountId = Guid.CreateVersion7();

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => manager.ConnectAsync(accountId, MessengerPlatform.Telegram)));

        factory.CreatedCount.Should().Be(1);
        manager.ConnectedAccounts.Should().ContainSingle();
    }

    /// <summary>
    /// Неподключившийся канал не должен попадать в словарь: иначе очередь решит,
    /// что он жив, и сообщения будут теряться без следа.
    /// </summary>
    [Fact]
    public async Task FailedConnectionIsNotRegistered()
    {
        var manager = NewManager(out var factory);
        factory.NextState = ConnectionState.RequiresReauth;
        var accountId = Guid.CreateVersion7();

        var state = await manager.ConnectAsync(accountId, MessengerPlatform.Telegram);

        state.Should().Be(ConnectionState.RequiresReauth);
        manager.TryGet(accountId, out _).Should().BeFalse();
        factory.LastCreated!.Disposed.Should().BeTrue("неудачный коннектор нужно освободить");
    }

    [Fact]
    public async Task ThrowingConnectorIsNotRegistered()
    {
        var manager = NewManager(out var factory);
        factory.ThrowOnConnect = true;

        var state = await manager.ConnectAsync(Guid.CreateVersion7(), MessengerPlatform.Telegram);

        state.Should().Be(ConnectionState.Disconnected);
        manager.ConnectedAccounts.Should().BeEmpty();
    }

    /// <summary>
    /// Фабрика Telegram читает настройки в момент создания и падает, если
    /// api_id и api_hash не заполнены. Это не должно ронять ни менеджера,
    /// ни, тем более, всё приложение.
    /// </summary>
    [Fact]
    public async Task ThrowingFactoryIsReportedInsteadOfCrashing()
    {
        var manager = NewManager(out var factory);
        factory.ThrowOnCreate = true;

        var state = await manager.ConnectAsync(Guid.CreateVersion7(), MessengerPlatform.Telegram);

        state.Should().Be(ConnectionState.Disconnected);
        manager.ConnectedAccounts.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownPlatformIsReportedInsteadOfCrashing()
    {
        var manager = NewManager(out _);

        manager.SupportsPlatform(MessengerPlatform.Max).Should().BeFalse();

        var state = await manager.ConnectAsync(Guid.CreateVersion7(), MessengerPlatform.Max);

        state.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task DisconnectRemovesAndDisposesConnector()
    {
        var manager = NewManager(out var factory);
        var accountId = Guid.CreateVersion7();
        await manager.ConnectAsync(accountId, MessengerPlatform.Telegram);

        await manager.DisconnectAsync(accountId);

        manager.TryGet(accountId, out _).Should().BeFalse();
        factory.LastCreated!.Disconnected.Should().BeTrue();
        factory.LastCreated.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisconnectingUnknownAccountIsHarmless()
    {
        var manager = NewManager(out _);

        var disconnect = async () => await manager.DisconnectAsync(Guid.CreateVersion7());

        await disconnect.Should().NotThrowAsync();
    }

    /// <summary>Канал, подключённый через интерфейс, переходит под управление менеджера.</summary>
    [Fact]
    public async Task AdoptedConnectorReplacesPrevious()
    {
        var manager = NewManager(out var factory);
        var accountId = Guid.CreateVersion7();
        await manager.ConnectAsync(accountId, MessengerPlatform.Telegram);
        var first = factory.LastCreated!;

        var second = new RecordingConnector(accountId);
        await manager.AdoptAsync(accountId, second);

        first.Disposed.Should().BeTrue("прежнее соединение нужно закрыть, а не бросить");
        manager.TryGet(accountId, out var current).Should().BeTrue();
        current.Should().BeSameAs(second);
    }

    [Fact]
    public async Task DisposeClosesEveryConnection()
    {
        var manager = NewManager(out var factory);
        await manager.ConnectAsync(Guid.CreateVersion7(), MessengerPlatform.Telegram);
        await manager.ConnectAsync(Guid.CreateVersion7(), MessengerPlatform.Telegram);

        await manager.DisposeAsync();

        factory.Created.Should().OnlyContain(connector => connector.Disposed);
        manager.ConnectedAccounts.Should().BeEmpty();
    }

    private AccountConnectionManager NewManager(out RecordingConnectorFactory factory)
    {
        factory = new RecordingConnectorFactory();

        return new AccountConnectionManager(
            [factory],
            _sink,
            NullLogger<AccountConnectionManager>.Instance);
    }

    private sealed class RecordingConnectorFactory : IMessengerConnectorFactory
    {
        public List<RecordingConnector> Created { get; } = [];

        public RecordingConnector? LastCreated => Created.LastOrDefault();

        public int CreatedCount => Created.Count;

        public ConnectionState NextState { get; set; } = ConnectionState.Connected;

        public bool ThrowOnConnect { get; set; }

        public bool ThrowOnCreate { get; set; }

        public MessengerPlatform Platform => MessengerPlatform.Telegram;

        public IMessengerConnector Create(Guid messengerAccountId, IMessengerEventSink sink)
        {
            if (ThrowOnCreate)
            {
                throw new InvalidOperationException("настройки платформы не заполнены");
            }

            var connector = new RecordingConnector(messengerAccountId)
            {
                MessengerAccountId = messengerAccountId,
                State = NextState,
                ThrowOnConnect = ThrowOnConnect,
            };

            lock (Created)
            {
                Created.Add(connector);
            }

            return connector;
        }
    }

    private sealed class RecordingConnector(Guid accountId) : FakeConnectorBase
    {
        public ConnectionState State { get; init; } = ConnectionState.Connected;

        public bool ThrowOnConnect { get; init; }

        public bool Disposed { get; private set; }

        public bool Disconnected { get; private set; }

        public override MessengerPlatform Platform => MessengerPlatform.Telegram;

        public override PlatformCapabilities Capabilities => new()
        {
            LoginMethod = LoginMethod.PhoneAndCode,
            RequiresPersistentConnection = true,
            SupportsHistoryBackfill = true,
            SupportsEditing = true,
            SupportsDeleteForEveryone = true,
        };

        public override Task<LoginStep> BeginLoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<LoginStep>(new LoginStep.Failed(LoginFailureReason.Unknown));

        public override Task<LoginStep> ContinueLoginAsync(LoginAnswer answer, CancellationToken cancellationToken = default) =>
            Task.FromResult<LoginStep>(new LoginStep.Failed(LoginFailureReason.Unknown));

        public override async Task<ConnectionState> ConnectExistingAsync(CancellationToken cancellationToken = default)
        {
            // Небольшая задержка, чтобы в тесте на гонку задачи реально пересеклись.
            await Task.Delay(10, cancellationToken);

            return ThrowOnConnect
                ? throw new InvalidOperationException("соединение не установлено")
                : State;
        }

        public override Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            Disconnected = true;
            return Task.CompletedTask;
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
