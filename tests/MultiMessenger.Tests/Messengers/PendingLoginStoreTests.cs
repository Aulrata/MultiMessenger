using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Messengers;
using MultiMessenger.Tests.Messaging;

namespace MultiMessenger.Tests.Messengers;

public class PendingLoginStoreTests
{
    private readonly PendingLoginStore _store = new(NullLogger<PendingLoginStore>.Instance);

    [Fact]
    public void StartedLoginIsFoundByItsOwner()
    {
        var managerId = Guid.CreateVersion7();
        var login = _store.Start(managerId, Guid.CreateVersion7(), MessengerPlatform.Telegram, new PhoneAndCodeConnector());

        _store.TryGet(login.Id, managerId, out var found).Should().BeTrue();
        found.MessengerAccountId.Should().Be(login.MessengerAccountId);
    }

    /// <summary>
    /// Идентификатор попытки уходит в браузер скрытым полем. Без проверки владельца
    /// чужой вход можно было бы продолжить, подставив его в форму.
    /// </summary>
    [Fact]
    public void AnotherManagerCannotContinueSomeoneElsesLogin()
    {
        var login = _store.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), MessengerPlatform.Telegram, new PhoneAndCodeConnector());

        _store.TryGet(login.Id, Guid.CreateVersion7(), out _).Should().BeFalse();
    }

    [Fact]
    public void UnknownLoginIsNotFound()
    {
        _store.TryGet(Guid.CreateVersion7(), Guid.CreateVersion7(), out _).Should().BeFalse();
    }

    /// <summary>Успешный вход отдаёт соединение менеджеру подключений, закрывать его нельзя.</summary>
    [Fact]
    public void ReleaseKeepsConnectionAlive()
    {
        var connector = new DisposalTrackingConnector();
        var managerId = Guid.CreateVersion7();
        var login = _store.Start(managerId, Guid.CreateVersion7(), MessengerPlatform.Telegram, connector);

        _store.Release(login.Id);

        connector.Disposed.Should().BeFalse();
        _store.TryGet(login.Id, managerId, out _).Should().BeFalse();
        _store.Count.Should().Be(0);
    }

    /// <summary>А вот при отказе или отмене сокет обязан закрыться.</summary>
    [Fact]
    public async Task AbandonClosesConnection()
    {
        var connector = new DisposalTrackingConnector();
        var login = _store.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), MessengerPlatform.Telegram, connector);

        await _store.AbandonAsync(login.Id);

        connector.Disposed.Should().BeTrue();
        _store.Count.Should().Be(0);
    }

    [Fact]
    public async Task ExpiredLoginsAreRemovedWithTheirConnections()
    {
        var fresh = new DisposalTrackingConnector();
        var stale = new DisposalTrackingConnector();

        _store.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), MessengerPlatform.Telegram, fresh);
        _store.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), MessengerPlatform.Telegram, stale);

        // Сдвигаем «сейчас» вперёд вместо ожидания: время жизни попытки — десять минут.
        var later = DateTimeOffset.UtcNow + PendingLoginStore.Lifetime + TimeSpan.FromSeconds(1);

        var removed = await _store.RemoveExpiredAsync(later);

        removed.Should().Be(2);
        fresh.Disposed.Should().BeTrue();
        stale.Disposed.Should().BeTrue();
        _store.Count.Should().Be(0);
    }

    [Fact]
    public async Task FreshLoginSurvivesCleanup()
    {
        _store.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), MessengerPlatform.Telegram, new DisposalTrackingConnector());

        var removed = await _store.RemoveExpiredAsync(DateTimeOffset.UtcNow.AddMinutes(1));

        removed.Should().Be(0);
        _store.Count.Should().Be(1);
    }

    /// <summary>Ошибка при закрытии одного соединения не должна срывать уборку остальных.</summary>
    [Fact]
    public async Task FailingDisposeDoesNotBreakCleanup()
    {
        _store.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), MessengerPlatform.Telegram, new ThrowingConnector());
        _store.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), MessengerPlatform.Telegram, new DisposalTrackingConnector());

        var later = DateTimeOffset.UtcNow + PendingLoginStore.Lifetime + TimeSpan.FromSeconds(1);

        var cleanup = async () => await _store.RemoveExpiredAsync(later);

        await cleanup.Should().NotThrowAsync();
        _store.Count.Should().Be(0);
    }

    [Fact]
    public async Task DisposeClosesEverything()
    {
        var connector = new DisposalTrackingConnector();
        _store.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), MessengerPlatform.Telegram, connector);

        await _store.DisposeAsync();

        connector.Disposed.Should().BeTrue();
        _store.Count.Should().Be(0);
    }

    private class DisposalTrackingConnector : FakeConnectorBase
    {
        public bool Disposed { get; private set; }

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
            Task.FromResult<LoginStep>(new LoginStep.NeedsVerificationCode(request.PhoneNumber));

        public override Task<LoginStep> ContinueLoginAsync(LoginAnswer answer, CancellationToken cancellationToken = default) =>
            Task.FromResult<LoginStep>(new LoginStep.Failed(LoginFailureReason.Unknown));

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingConnector : DisposalTrackingConnector
    {
        public override ValueTask DisposeAsync() => throw new InvalidOperationException("сокет уже закрыт");
    }
}
