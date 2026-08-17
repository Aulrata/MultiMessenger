using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Messengers;
using MultiMessenger.Infrastructure.Messengers.Inbox;
using MultiMessenger.Tests.Persistence;

namespace MultiMessenger.Tests.Messengers;

/// <summary>
/// Приём входящих проверяется на настоящей базе: вся суть здесь в связях
/// и уникальных индексах, а на подделке контекста они не работают.
/// </summary>
[Collection(PostgresCollection.Name)]
public class InboxServiceTests(PostgresFixture postgres)
{
    private readonly WorkspaceNotifier _notifier = new(NullLogger<WorkspaceNotifier>.Instance);

    [Fact]
    public async Task FirstMessageCreatesContactDialogAndMessage()
    {
        var account = await CreateAccountAsync();
        var incoming = NewIncoming(account.Id, "Здравствуйте, интересует тур", senderName: "Иван");

        var result = await HandleAsync(incoming);

        result.Outcome.Should().Be(InboxOutcome.Stored);

        await using var dbContext = postgres.CreateDbContext();

        var identity = await dbContext.ContactIdentities
            .Include(item => item.Contact)
            .SingleAsync(item => item.PlatformUserId == incoming.PlatformUserId);

        identity.Platform.Should().Be(MessengerPlatform.Telegram);
        identity.DisplayNameOnPlatform.Should().Be("Иван");
        identity.Contact!.DisplayName.Should().Be("Иван");
        identity.Contact.PrimaryPlatform.Should().Be(MessengerPlatform.Telegram);

        var dialog = await dbContext.Dialogs.SingleAsync(item => item.ContactId == identity.ContactId);
        dialog.MessengerAccountId.Should().Be(account.Id);
        dialog.LastMessageAt.Should().BeCloseTo(incoming.OccurredAt, TimeSpan.FromSeconds(1));

        var message = await dbContext.Messages.SingleAsync(item => item.DialogId == dialog.Id);
        message.Direction.Should().Be(MessageDirection.Incoming);
        message.SenderType.Should().Be(SenderType.Client);
        message.Status.Should().Be(MessageStatus.Delivered, "менеджер сообщение ещё не читал");
        message.Text.Should().Be("Здравствуйте, интересует тур");
    }

    /// <summary>Клиент, у которого скрыты имя и username, всё равно должен быть узнаваем.</summary>
    [Fact]
    public async Task ContactWithoutNameFallsBackToPlatformIdentifier()
    {
        var account = await CreateAccountAsync();
        var incoming = NewIncoming(account.Id, "привет", senderName: null);

        await HandleAsync(incoming);

        await using var dbContext = postgres.CreateDbContext();
        var contact = await dbContext.Contacts
            .SingleAsync(item => item.Identities.Any(identity => identity.PlatformUserId == incoming.PlatformUserId));

        contact.DisplayName.Should().Be(incoming.PlatformUserId);
    }

    [Fact]
    public async Task SecondMessageReusesContactAndDialog()
    {
        var account = await CreateAccountAsync();
        var platformUserId = NewPlatformUserId();

        await HandleAsync(NewIncoming(account.Id, "первое", platformUserId: platformUserId));
        await HandleAsync(NewIncoming(account.Id, "второе", platformUserId: platformUserId));

        await using var dbContext = postgres.CreateDbContext();

        (await dbContext.ContactIdentities.CountAsync(item => item.PlatformUserId == platformUserId)).Should().Be(1);

        var dialog = await dbContext.Dialogs.SingleAsync(item => item.MessengerAccountId == account.Id);
        (await dbContext.Messages.CountAsync(item => item.DialogId == dialog.Id)).Should().Be(2);
    }

    /// <summary>
    /// Ключевая защита из ТЗ: один и тот же апдейт может прийти дважды —
    /// при переподключении или из-за догрузки истории.
    /// </summary>
    [Fact]
    public async Task SameplatformMessageIdIsStoredOnce()
    {
        var account = await CreateAccountAsync();
        var incoming = NewIncoming(account.Id, "дубль");

        var first = await HandleAsync(incoming);
        var second = await HandleAsync(incoming);

        first.Outcome.Should().Be(InboxOutcome.Stored);
        second.Outcome.Should().Be(InboxOutcome.Duplicate);

        await using var dbContext = postgres.CreateDbContext();
        (await dbContext.Messages.CountAsync(item => item.PlatformMessageId == incoming.PlatformMessageId))
            .Should().Be(1);
    }

    /// <summary>
    /// Сообщение, отправленное менеджером с телефона, приходит тем же апдейтом
    /// с флагом out_. Без него в истории появились бы дыры (раздел 5.6 ТЗ).
    /// </summary>
    [Fact]
    public async Task MessageSentFromPhoneIsStoredAsOutgoing()
    {
        var account = await CreateAccountAsync();
        var incoming = NewIncoming(account.Id, "отвечаю с телефона") with { Direction = MessageDirection.Outgoing };

        await HandleAsync(incoming);

        await using var dbContext = postgres.CreateDbContext();
        var message = await dbContext.Messages.SingleAsync(item => item.PlatformMessageId == incoming.PlatformMessageId);

        message.Direction.Should().Be(MessageDirection.Outgoing);
        message.SenderType.Should().Be(SenderType.Manager);
        message.Status.Should().Be(MessageStatus.Sent);
    }

    /// <summary>Своё сообщение с телефона не меняет платформу для ответа по умолчанию.</summary>
    [Fact]
    public async Task OutgoingMessageDoesNotChangePrimaryPlatform()
    {
        var account = await CreateAccountAsync();
        var platformUserId = NewPlatformUserId();

        await HandleAsync(NewIncoming(account.Id, "входящее", platformUserId: platformUserId));

        await using (var setup = postgres.CreateDbContext())
        {
            var contact = await setup.Contacts
                .SingleAsync(item => item.Identities.Any(identity => identity.PlatformUserId == platformUserId));
            contact.PrimaryPlatform = MessengerPlatform.WhatsApp;
            await setup.SaveChangesAsync();
        }

        await HandleAsync(NewIncoming(account.Id, "исходящее", platformUserId: platformUserId)
            with { Direction = MessageDirection.Outgoing });

        await using var dbContext = postgres.CreateDbContext();
        var stored = await dbContext.Contacts
            .SingleAsync(item => item.Identities.Any(identity => identity.PlatformUserId == platformUserId));

        stored.PrimaryPlatform.Should().Be(MessengerPlatform.WhatsApp);
    }

    /// <summary>
    /// Список диалогов сортируется по LastMessageAt. Догрузка старой истории
    /// не должна поднимать диалог наверх.
    /// </summary>
    [Fact]
    public async Task OlderMessageDoesNotMoveDialogUp()
    {
        var account = await CreateAccountAsync();
        var platformUserId = NewPlatformUserId();
        var now = DateTimeOffset.UtcNow;

        await HandleAsync(NewIncoming(account.Id, "свежее", platformUserId: platformUserId) with { OccurredAt = now });
        await HandleAsync(NewIncoming(account.Id, "старое", platformUserId: platformUserId)
            with { OccurredAt = now.AddDays(-30) });

        await using var dbContext = postgres.CreateDbContext();
        var dialog = await dbContext.Dialogs.SingleAsync(item => item.MessengerAccountId == account.Id);

        dialog.LastMessageAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task MessageWithAttachmentIsStillStored()
    {
        var account = await CreateAccountAsync();
        var incoming = NewIncoming(account.Id, "договор во вложении") with
        {
            Attachments =
            [
                new IncomingAttachment
                {
                    Type = MediaType.Document,
                    PlatformFileReference = "777:42:0",
                    FileName = "договор.pdf",
                    MimeType = "application/pdf",
                },
            ],
        };

        var result = await HandleAsync(incoming);

        result.Outcome.Should().Be(InboxOutcome.Stored, "в переписке не должно быть дыр из-за неготовой загрузки медиа");

        await using var dbContext = postgres.CreateDbContext();
        var message = await dbContext.Messages.SingleAsync(item => item.PlatformMessageId == incoming.PlatformMessageId);
        message.Text.Should().Be("договор во вложении");
    }

    /// <summary>Апдейт может прийти по каналу, который только что удалили.</summary>
    [Fact]
    public async Task MessageForUnknownAccountIsDropped()
    {
        var result = await HandleAsync(NewIncoming(Guid.CreateVersion7(), "в пустоту"));

        result.Outcome.Should().Be(InboxOutcome.UnknownAccount);
    }

    [Fact]
    public async Task OwningManagerIsNotified()
    {
        var account = await CreateAccountAsync();
        var received = new List<WorkspaceNotification>();

        using var subscription = _notifier.Subscribe(account.ManagerId, notification =>
        {
            received.Add(notification);
            return Task.CompletedTask;
        });

        await HandleAsync(NewIncoming(account.Id, "новое сообщение"));

        var notification = received.Should().ContainSingle().Subject;
        notification.Kind.Should().Be(WorkspaceEventKind.MessageReceived);
        notification.Platform.Should().Be(MessengerPlatform.Telegram);
        notification.MessageId.Should().NotBeNull();
    }

    /// <summary>Переписка — персональные данные, чужому подписчику она уходить не должна.</summary>
    [Fact]
    public async Task OtherManagersAreNotNotified()
    {
        var account = await CreateAccountAsync();
        var stranger = Guid.CreateVersion7();
        var received = new List<WorkspaceNotification>();

        using var subscription = _notifier.Subscribe(stranger, notification =>
        {
            received.Add(notification);
            return Task.CompletedTask;
        });

        await HandleAsync(NewIncoming(account.Id, "чужое сообщение"));

        received.Should().BeEmpty();
    }

    [Fact]
    public async Task AccountActivityTimeIsRefreshed()
    {
        var account = await CreateAccountAsync();
        var occurredAt = DateTimeOffset.UtcNow;

        await HandleAsync(NewIncoming(account.Id, "привет") with { OccurredAt = occurredAt });

        await using var dbContext = postgres.CreateDbContext();
        var stored = await dbContext.MessengerAccounts.SingleAsync(item => item.Id == account.Id);

        stored.LastActiveAt.Should().BeCloseTo(occurredAt, TimeSpan.FromSeconds(1));
    }

    // --- обвязка ---------------------------------------------------------

    private async Task<InboxResult> HandleAsync(IncomingMessage message)
    {
        await using var dbContext = postgres.CreateDbContext();
        var service = new InboxService(dbContext, _notifier, NullLogger<InboxService>.Instance);

        return await service.HandleAsync(message);
    }

    private static string NewPlatformUserId() => $"tg-{Random.Shared.NextInt64(1, long.MaxValue)}";

    private static IncomingMessage NewIncoming(
        Guid accountId,
        string text,
        string? platformUserId = null,
        string? senderName = "Клиент") => new()
    {
        MessengerAccountId = accountId,
        PlatformUserId = platformUserId ?? NewPlatformUserId(),
        PlatformMessageId = Random.Shared.Next(1, int.MaxValue).ToString(),
        Direction = MessageDirection.Incoming,
        Text = text,
        SenderDisplayName = senderName,
        OccurredAt = DateTimeOffset.UtcNow,
    };

    private async Task<MessengerAccount> CreateAccountAsync()
    {
        await using var dbContext = postgres.CreateDbContext();

        var manager = TestData.NewManager("hash");
        var account = new MessengerAccount
        {
            ManagerId = manager.Id,
            Platform = MessengerPlatform.Telegram,
            PhoneNumber = manager.PhoneNumber,
            Status = MessengerAccountStatus.Active,
        };

        dbContext.Managers.Add(manager);
        dbContext.MessengerAccounts.Add(account);
        await dbContext.SaveChangesAsync();

        return account;
    }
}
