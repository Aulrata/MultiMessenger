using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Configuration;
using MultiMessenger.Infrastructure.Messengers;
using MultiMessenger.Infrastructure.Messengers.Outbox;
using MultiMessenger.Tests.Messaging;
using MultiMessenger.Tests.Persistence;
using MultiMessenger.Tests.Support;
using NSubstitute;

namespace MultiMessenger.Tests.Messengers;

/// <summary>
/// Очередь исходящих: постановка, отправка, паузы, повторы и неудачи.
/// Telegram подменён заготовкой, но база настоящая — статусы и порядок
/// сообщений держатся на ней.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OutboxDispatcherTests(PostgresFixture postgres)
{
    private static readonly OutboxOptions Settings = new()
    {
        DelayBetweenRecipientsSeconds = 25,
        DelayWithinDialogSeconds = 3,
        MaxAttempts = 3,
        RetryBackoffSeconds = 30,
    };

    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceNotifier _notifier = new(NullLogger<WorkspaceNotifier>.Instance);
    private readonly SendingConnectorFactory _factory = new();

    private AccountConnectionManager _connections = null!;
    private OutboxRateLimiter _rateLimiter = null!;

    // --- постановка в очередь ---------------------------------------------

    [Fact]
    public async Task EnqueuedMessageIsVisibleImmediatelyAsPending()
    {
        var scene = await CreateSceneAsync();
        var received = new List<WorkspaceNotification>();
        using var subscription = _notifier.Subscribe(scene.ManagerId, notification =>
        {
            received.Add(notification);
            return Task.CompletedTask;
        });

        var result = await EnqueueAsync(scene.ManagerId, scene.DialogId, "Добрый день!");

        result.Succeeded.Should().BeTrue();

        await using var dbContext = postgres.CreateDbContext();
        var message = await dbContext.Messages.SingleAsync(item => item.Id == result.MessageId);

        message.Status.Should().Be(MessageStatus.Pending);
        message.Direction.Should().Be(MessageDirection.Outgoing);
        message.SenderType.Should().Be(SenderType.Manager);
        message.SentByManagerId.Should().Be(scene.ManagerId);
        message.PlatformMessageId.Should().BeNull("платформа его ещё не видела");

        received.Should().ContainSingle().Which.Kind.Should().Be(WorkspaceEventKind.MessageSent);
    }

    [Fact]
    public async Task EmptyMessageIsRejected()
    {
        var scene = await CreateSceneAsync();

        (await EnqueueAsync(scene.ManagerId, scene.DialogId, "   ")).Failure
            .Should().Be(EnqueueFailure.EmptyMessage);
    }

    /// <summary>В чужой диалог писать нельзя, даже зная его идентификатор.</summary>
    [Fact]
    public async Task StrangerCannotWriteIntoSomeoneElsesDialog()
    {
        var scene = await CreateSceneAsync();

        (await EnqueueAsync(Guid.CreateVersion7(), scene.DialogId, "привет")).Failure
            .Should().Be(EnqueueFailure.DialogNotFound);
    }

    /// <summary>
    /// Если сессия недействительна, сообщение только копилось бы в очереди.
    /// Честнее отказать сразу.
    /// </summary>
    [Fact]
    public async Task ChannelNeedingReauthRefusesNewMessages()
    {
        var scene = await CreateSceneAsync(MessengerAccountStatus.RequiresReauth);

        (await EnqueueAsync(scene.ManagerId, scene.DialogId, "привет")).Failure
            .Should().Be(EnqueueFailure.ChannelUnavailable);
    }

    // --- отправка ---------------------------------------------------------

    [Fact]
    public async Task SentMessageGetsPlatformIdAndStatus()
    {
        var scene = await CreateSceneAsync();
        await ConnectAsync(scene.AccountId);
        var enqueued = await EnqueueAsync(scene.ManagerId, scene.DialogId, "Добрый день!");

        var outcome = await DispatchOneAsync(enqueued.MessageId!.Value);

        outcome.Should().Be(DispatchOutcome.Sent);

        await using var dbContext = postgres.CreateDbContext();
        var message = await dbContext.Messages.SingleAsync(item => item.Id == enqueued.MessageId);

        message.Status.Should().Be(MessageStatus.Sent);
        message.PlatformMessageId.Should().NotBeNullOrEmpty();
        message.FailureReason.Should().BeNull();
        _factory.Sent.Should().ContainSingle().Which.Text.Should().Be("Добрый день!");
    }

    [Fact]
    public async Task MessageWaitsWhileChannelIsOffline()
    {
        var scene = await CreateSceneAsync();
        var enqueued = await EnqueueAsync(scene.ManagerId, scene.DialogId, "привет");

        var outcome = await DispatchOneAsync(enqueued.MessageId!.Value);

        outcome.Should().Be(DispatchOutcome.ChannelOffline);

        await using var dbContext = postgres.CreateDbContext();
        (await dbContext.Messages.SingleAsync(item => item.Id == enqueued.MessageId))
            .Status.Should().Be(MessageStatus.Pending, "сообщение не теряется, а ждёт");
    }

    [Fact]
    public async Task SecondMessageToAnotherRecipientIsThrottled()
    {
        var scene = await CreateSceneAsync();
        await ConnectAsync(scene.AccountId);
        var second = await CreateDialogAsync(scene);

        var first = await EnqueueAsync(scene.ManagerId, scene.DialogId, "первому");
        var next = await EnqueueAsync(scene.ManagerId, second, "второму");

        (await DispatchOneAsync(first.MessageId!.Value)).Should().Be(DispatchOutcome.Sent);
        (await DispatchOneAsync(next.MessageId!.Value)).Should().Be(DispatchOutcome.Throttled);

        _time.Advance(TimeSpan.FromSeconds(25));

        (await DispatchOneAsync(next.MessageId!.Value)).Should().Be(DispatchOutcome.Sent);
    }

    /// <summary>Очередь берёт по одному сообщению на канал — паузу иначе не соблюсти.</summary>
    [Fact]
    public async Task DispatchTakesOneMessagePerAccount()
    {
        var scene = await CreateSceneAsync();
        await ConnectAsync(scene.AccountId);

        var first = await EnqueueAsync(scene.ManagerId, scene.DialogId, "первое");
        var second = await EnqueueAsync(scene.ManagerId, scene.DialogId, "второе");

        // База общая на всю коллекцию тестов, поэтому смотрим только свои сообщения.
        var mine = (await DispatchDueAsync())
            .Where(attempt => attempt.MessageId == first.MessageId || attempt.MessageId == second.MessageId)
            .ToList();

        mine.Should().ContainSingle().Which.Outcome.Should().Be(DispatchOutcome.Sent);
        _factory.Sent.Should().ContainSingle();
    }

    // --- неудачи ----------------------------------------------------------

    /// <summary>Telegram сам называет срок ожидания, и он важнее наших расчётов.</summary>
    [Fact]
    public async Task RateLimitFromPlatformSetsTheirWaitTime()
    {
        var scene = await CreateSceneAsync();
        await ConnectAsync(scene.AccountId);
        _factory.NextResult = messageId => DeliveryResult.Failure(
            messageId, DeliveryFailureReason.RateLimited, "FLOOD_WAIT_120", TimeSpan.FromSeconds(120));

        var enqueued = await EnqueueAsync(scene.ManagerId, scene.DialogId, "привет");

        (await DispatchOneAsync(enqueued.MessageId!.Value)).Should().Be(DispatchOutcome.Deferred);

        await using var dbContext = postgres.CreateDbContext();
        var message = await dbContext.Messages.SingleAsync(item => item.Id == enqueued.MessageId);

        message.Status.Should().Be(MessageStatus.Pending);
        message.SendAttempts.Should().Be(1);
        message.NextAttemptAt.Should().Be(_time.GetUtcNow().AddSeconds(120));
    }

    /// <summary>Заблокировавший отправителя клиент сам не разблокируется — повторы бессмысленны.</summary>
    [Fact]
    public async Task PermanentFailureIsNotRetried()
    {
        var scene = await CreateSceneAsync();
        await ConnectAsync(scene.AccountId);
        _factory.NextResult = messageId => DeliveryResult.Failure(
            messageId, DeliveryFailureReason.RecipientUnavailable, "USER_IS_BLOCKED");

        var enqueued = await EnqueueAsync(scene.ManagerId, scene.DialogId, "привет");

        (await DispatchOneAsync(enqueued.MessageId!.Value)).Should().Be(DispatchOutcome.Failed);

        await using var dbContext = postgres.CreateDbContext();
        var message = await dbContext.Messages.SingleAsync(item => item.Id == enqueued.MessageId);

        message.Status.Should().Be(MessageStatus.Failed);
        message.FailureReason.Should().Be("USER_IS_BLOCKED");
        message.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task TransientFailureGivesUpAfterMaxAttempts()
    {
        var scene = await CreateSceneAsync();
        await ConnectAsync(scene.AccountId);
        _factory.NextResult = messageId => DeliveryResult.Failure(
            messageId, DeliveryFailureReason.NetworkError, "нет связи");

        var enqueued = await EnqueueAsync(scene.ManagerId, scene.DialogId, "привет");
        var messageId = enqueued.MessageId!.Value;

        (await DispatchOneAsync(messageId)).Should().Be(DispatchOutcome.Deferred);
        _time.Advance(TimeSpan.FromHours(1));
        (await DispatchOneAsync(messageId)).Should().Be(DispatchOutcome.Deferred);
        _time.Advance(TimeSpan.FromHours(1));

        (await DispatchOneAsync(messageId)).Should().Be(DispatchOutcome.Failed, "три попытки — предел из настроек");

        await using var dbContext = postgres.CreateDbContext();
        (await dbContext.Messages.SingleAsync(item => item.Id == messageId))
            .SendAttempts.Should().Be(3);
    }

    [Fact]
    public async Task DeferredMessageIsSkippedUntilItsTime()
    {
        var scene = await CreateSceneAsync();
        await ConnectAsync(scene.AccountId);
        _factory.NextResult = messageId => DeliveryResult.Failure(
            messageId, DeliveryFailureReason.NetworkError, "нет связи");

        var enqueued = await EnqueueAsync(scene.ManagerId, scene.DialogId, "привет");
        await DispatchDueAsync();

        _factory.NextResult = null;

        (await MineAsync(enqueued.MessageId!.Value)).Should().BeEmpty("время повтора ещё не пришло");

        _time.Advance(TimeSpan.FromMinutes(5));

        (await MineAsync(enqueued.MessageId!.Value)).Should().ContainSingle()
            .Which.Outcome.Should().Be(DispatchOutcome.Sent);
    }

    /// <summary>Контакт без идентификатора на этой платформе — отправлять физически некуда.</summary>
    [Fact]
    public async Task MissingPlatformIdentityFailsWithoutRetries()
    {
        var scene = await CreateSceneAsync();
        await ConnectAsync(scene.AccountId);

        await using (var setup = postgres.CreateDbContext())
        {
            var identities = setup.ContactIdentities.Where(item => item.ContactId == scene.ContactId);
            setup.ContactIdentities.RemoveRange(identities);
            await setup.SaveChangesAsync();
        }

        var enqueued = await EnqueueAsync(scene.ManagerId, scene.DialogId, "привет");

        (await DispatchOneAsync(enqueued.MessageId!.Value)).Should().Be(DispatchOutcome.Failed);

        await using var dbContext = postgres.CreateDbContext();
        (await dbContext.Messages.SingleAsync(item => item.Id == enqueued.MessageId))
            .FailureReason.Should().Contain("идентификатора");
    }

    [Fact]
    public async Task ManagerSeesFailureImmediately()
    {
        var scene = await CreateSceneAsync();
        await ConnectAsync(scene.AccountId);
        _factory.NextResult = messageId => DeliveryResult.Failure(
            messageId, DeliveryFailureReason.RecipientUnavailable, "USER_IS_BLOCKED");

        var enqueued = await EnqueueAsync(scene.ManagerId, scene.DialogId, "привет");

        var received = new List<WorkspaceNotification>();
        using var subscription = _notifier.Subscribe(scene.ManagerId, notification =>
        {
            received.Add(notification);
            return Task.CompletedTask;
        });

        await DispatchOneAsync(enqueued.MessageId!.Value);

        received.Should().ContainSingle().Which.MessageId.Should().Be(enqueued.MessageId);
    }

    // --- обвязка ---------------------------------------------------------

    private async Task<EnqueueResult> EnqueueAsync(Guid managerId, Guid dialogId, string text)
    {
        await using var dbContext = postgres.CreateDbContext();

        return await new OutboxService(dbContext, _notifier).EnqueueAsync(managerId, dialogId, text);
    }

    private async Task<DispatchOutcome> DispatchOneAsync(Guid messageId) =>
        await NewDispatcher().DispatchOneAsync(messageId);

    private async Task<IReadOnlyList<DispatchAttempt>> DispatchDueAsync() =>
        await NewDispatcher().DispatchDueAsync();

    /// <summary>Проход очереди, отфильтрованный до одного своего сообщения.</summary>
    private async Task<IReadOnlyList<DispatchAttempt>> MineAsync(Guid messageId) =>
        (await DispatchDueAsync()).Where(attempt => attempt.MessageId == messageId).ToList();

    private OutboxDispatcher NewDispatcher()
    {
        _rateLimiter ??= new OutboxRateLimiter(_time);

        return new OutboxDispatcher(
            postgres.CreateDbContext(),
            _connections,
            _rateLimiter,
            _notifier,
            Options.Create(Settings),
            _time,
            NullLogger<OutboxDispatcher>.Instance);
    }

    private async Task ConnectAsync(Guid accountId) =>
        await _connections.ConnectAsync(accountId, MessengerPlatform.Telegram);

    private async Task<Guid> CreateDialogAsync(Scene scene)
    {
        await using var dbContext = postgres.CreateDbContext();

        var contact = new Contact
        {
            DisplayName = "Второй клиент",
            PrimaryPlatform = MessengerPlatform.Telegram,
            Identities =
            [
                new ContactIdentity
                {
                    Platform = MessengerPlatform.Telegram,
                    PlatformUserId = $"tg-{Random.Shared.NextInt64(1, long.MaxValue)}",
                },
            ],
        };

        var dialog = new Dialog
        {
            ContactId = contact.Id,
            MessengerAccountId = scene.AccountId,
            Platform = MessengerPlatform.Telegram,
        };

        dbContext.Contacts.Add(contact);
        dbContext.Dialogs.Add(dialog);
        await dbContext.SaveChangesAsync();

        return dialog.Id;
    }

    private async Task<Scene> CreateSceneAsync(MessengerAccountStatus status = MessengerAccountStatus.Active)
    {
        _connections ??= new AccountConnectionManager(
            [_factory],
            Substitute.For<IMessengerEventSink>(),
            NullLogger<AccountConnectionManager>.Instance);

        await using var dbContext = postgres.CreateDbContext();

        var manager = TestData.NewManager("hash");
        dbContext.Managers.Add(manager);
        await dbContext.SaveChangesAsync();

        // Канал создаёт сама заготовка: на пару «сотрудник + платформа» стоит
        // уникальный индекс, и второй здесь заводить нельзя.
        var chain = await TestData.CreateMediaChainAsync(dbContext, manager);

        chain.Account.Status = status;
        await dbContext.SaveChangesAsync();

        return new Scene(manager.Id, chain.Account.Id, chain.Dialog.Id, chain.Contact.Id);
    }

    private sealed record Scene(Guid ManagerId, Guid AccountId, Guid DialogId, Guid ContactId);

    /// <summary>Коннектор, запоминающий отправленное и умеющий возвращать заданный исход.</summary>
    private sealed class SendingConnectorFactory : IMessengerConnectorFactory
    {
        public List<OutgoingMessage> Sent { get; } = [];

        public Func<Guid, DeliveryResult>? NextResult { get; set; }

        public MessengerPlatform Platform => MessengerPlatform.Telegram;

        public IMessengerConnector Create(Guid messengerAccountId, IMessengerEventSink sink) =>
            new RecordingConnector(this) { MessengerAccountId = messengerAccountId };

        private sealed class RecordingConnector(SendingConnectorFactory owner) : FakeConnectorBase
        {
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

            public override Task<DeliveryResult> SendMessageAsync(
                OutgoingMessage message,
                CancellationToken cancellationToken = default)
            {
                if (owner.NextResult is { } failure)
                {
                    return Task.FromResult(failure(message.MessageId));
                }

                owner.Sent.Add(message);

                return Task.FromResult(DeliveryResult.Success(
                    message.MessageId,
                    Random.Shared.Next(1, int.MaxValue).ToString(),
                    DateTimeOffset.UtcNow));
            }
        }
    }
}
