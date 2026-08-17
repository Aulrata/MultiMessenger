using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Infrastructure.Auditing;
using MultiMessenger.Infrastructure.Workspace;
using MultiMessenger.Tests.Persistence;
using DomainMessage = MultiMessenger.Core.Entities.Message;

namespace MultiMessenger.Tests.Workspace;

/// <summary>
/// Запросы рабочего кабинета. Проверяются на настоящей базе: главное здесь —
/// счётчики, сортировка и ограничение выборки владельцем канала.
/// </summary>
[Collection(PostgresCollection.Name)]
public class WorkspaceQueriesTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ContactsAreSortedByLatestMessageFirst()
    {
        var scene = await CreateSceneAsync();
        var now = DateTimeOffset.UtcNow;

        var older = await AddDialogWithMessageAsync(scene, "Давний клиент", now.AddHours(-5));
        var newer = await AddDialogWithMessageAsync(scene, "Свежий клиент", now);

        var contacts = await NewQueries().GetContactsAsync(scene.ManagerId);

        contacts.Select(contact => contact.ContactId)
            .Should().ContainInOrder(newer.ContactId, older.ContactId);
    }

    [Fact]
    public async Task UnreadCounterCountsOnlyUnreadIncoming()
    {
        var scene = await CreateSceneAsync();
        var dialog = await AddDialogWithMessageAsync(scene, "Клиент", DateTimeOffset.UtcNow);

        await AddMessagesAsync(dialog.DialogId,
            Incoming("непрочитанное", MessageStatus.Delivered),
            Incoming("прочитанное", MessageStatus.Read),
            Outgoing("своё", MessageStatus.Sent));

        var contact = (await NewQueries().GetContactsAsync(scene.ManagerId))
            .Single(item => item.ContactId == dialog.ContactId);

        contact.UnreadCount.Should().Be(2, "первое сообщение диалога тоже входящее и непрочитанное");
    }

    [Fact]
    public async Task OpeningDialogClearsUnreadCounter()
    {
        var scene = await CreateSceneAsync();
        var dialog = await AddDialogWithMessageAsync(scene, "Клиент", DateTimeOffset.UtcNow);
        await AddMessagesAsync(dialog.DialogId, Incoming("ещё одно", MessageStatus.Delivered));

        var marked = await NewQueries().MarkDialogReadAsync(scene.ManagerId, dialog.DialogId);

        marked.Should().Be(2);

        var contact = (await NewQueries().GetContactsAsync(scene.ManagerId))
            .Single(item => item.ContactId == dialog.ContactId);

        contact.UnreadCount.Should().Be(0);
    }

    /// <summary>Переписка — персональные данные, фильтр стоит в самом запросе.</summary>
    [Fact]
    public async Task ManagerSeesOnlyOwnDialogs()
    {
        var mine = await CreateSceneAsync();
        var strangers = await CreateSceneAsync();
        await AddDialogWithMessageAsync(strangers, "Чужой клиент", DateTimeOffset.UtcNow);

        var contacts = await NewQueries().GetContactsAsync(mine.ManagerId);

        contacts.Should().NotContain(contact => contact.DisplayName == "Чужой клиент");
    }

    [Fact]
    public async Task MessagesOfAnotherManagerAreNotReturned()
    {
        var mine = await CreateSceneAsync();
        var strangers = await CreateSceneAsync();
        var foreign = await AddDialogWithMessageAsync(strangers, "Чужой клиент", DateTimeOffset.UtcNow);

        var page = await NewQueries().GetMessagesAsync(mine.ManagerId, foreign.DialogId);

        page.Messages.Should().BeEmpty();
    }

    /// <summary>
    /// Клиент, писавший с двух платформ, — один контакт с двумя подчатами
    /// (см. docs/Интерфейс_переписки.md).
    /// </summary>
    [Fact]
    public async Task ContactWithTwoPlatformsHasTwoDialogs()
    {
        var scene = await CreateSceneAsync();
        var dialog = await AddDialogWithMessageAsync(scene, "Двухканальный", DateTimeOffset.UtcNow);
        await AddSecondPlatformDialogAsync(scene, dialog.ContactId);

        var contact = (await NewQueries().GetContactsAsync(scene.ManagerId))
            .Single(item => item.ContactId == dialog.ContactId);

        contact.HasSeveralPlatforms.Should().BeTrue();
        contact.Dialogs.Select(item => item.Platform)
            .Should().BeEquivalentTo([MessengerPlatform.Telegram, MessengerPlatform.WhatsApp]);
        contact.UnreadCount.Should().Be(2, "счётчик на свёрнутой строке суммирует все каналы");
    }

    // --- история ---------------------------------------------------------

    [Fact]
    public async Task MessagesComeOldestFirstWithinPage()
    {
        var scene = await CreateSceneAsync();
        var dialog = await AddDialogWithMessageAsync(scene, "Клиент", DateTimeOffset.UtcNow.AddMinutes(-10));
        await AddMessagesAsync(dialog.DialogId,
            Incoming("второе", MessageStatus.Read, DateTimeOffset.UtcNow.AddMinutes(-5)),
            Incoming("третье", MessageStatus.Read, DateTimeOffset.UtcNow));

        var page = await NewQueries().GetMessagesAsync(scene.ManagerId, dialog.DialogId);

        page.Messages.Select(message => message.Text).Should().ContainInOrder("второе", "третье");
    }

    [Fact]
    public async Task PageReportsWhetherOlderMessagesExist()
    {
        var scene = await CreateSceneAsync();
        var dialog = await AddDialogWithMessageAsync(scene, "Болтливый", DateTimeOffset.UtcNow.AddHours(-1));

        var extra = Enumerable.Range(0, 5)
            .Select(index => Incoming($"сообщение {index}", MessageStatus.Read, DateTimeOffset.UtcNow.AddMinutes(index)))
            .ToArray();
        await AddMessagesAsync(dialog.DialogId, extra);

        var firstPage = await NewQueries().GetMessagesAsync(scene.ManagerId, dialog.DialogId, pageSize: 3);

        firstPage.Messages.Should().HaveCount(3);
        firstPage.HasMore.Should().BeTrue();

        var older = await NewQueries().GetMessagesAsync(
            scene.ManagerId, dialog.DialogId, before: firstPage.Messages[0].CreatedAt, pageSize: 3);

        older.Messages.Should().HaveCount(3);
        older.Messages.Select(message => message.Text)
            .Should().NotIntersectWith(firstPage.Messages.Select(message => message.Text));
    }

    [Fact]
    public async Task FailedMessageCarriesItsReason()
    {
        var scene = await CreateSceneAsync();
        var dialog = await AddDialogWithMessageAsync(scene, "Клиент", DateTimeOffset.UtcNow);
        await AddMessagesAsync(dialog.DialogId, new DomainMessage
        {
            DialogId = dialog.DialogId,
            Direction = MessageDirection.Outgoing,
            SenderType = SenderType.Manager,
            Text = "не ушло",
            Status = MessageStatus.Failed,
            FailureReason = "USER_IS_BLOCKED",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var page = await NewQueries().GetMessagesAsync(scene.ManagerId, dialog.DialogId);

        page.Messages.Should().Contain(message => message.FailureReason == "USER_IS_BLOCKED");
    }

    // --- карточка контакта -------------------------------------------------

    [Fact]
    public async Task ContactCardIsReturnedWithChannels()
    {
        var scene = await CreateSceneAsync();
        var dialog = await AddDialogWithMessageAsync(scene, "Иван Петров", DateTimeOffset.UtcNow);

        var card = await NewQueries().GetContactCardAsync(scene.ManagerId, dialog.ContactId);

        card.Should().NotBeNull();
        card!.DisplayName.Should().Be("Иван Петров");
        card.Channels.Should().ContainSingle().Which.Platform.Should().Be(MessengerPlatform.Telegram);
    }

    [Fact]
    public async Task CardEditIsSavedAndAudited()
    {
        var scene = await CreateSceneAsync();
        var dialog = await AddDialogWithMessageAsync(scene, "Было", DateTimeOffset.UtcNow);

        var saved = await NewQueries().UpdateContactCardAsync(
            scene.ManagerId, dialog.ContactId, "Стало", "UON-1234", "Летит 12 июля");

        saved.Should().BeTrue();

        await using var dbContext = postgres.CreateDbContext();
        var contact = await dbContext.Contacts.SingleAsync(item => item.Id == dialog.ContactId);

        contact.DisplayName.Should().Be("Стало");
        contact.CrmOrderNumber.Should().Be("UON-1234");
        contact.Notes.Should().Be("Летит 12 июля");

        (await dbContext.AuditEntries.AnyAsync(entry =>
            entry.EntityId == dialog.ContactId && entry.Action == AuditAction.ContactUpdated))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EmptyNameIsNotSaved()
    {
        var scene = await CreateSceneAsync();
        var dialog = await AddDialogWithMessageAsync(scene, "Имя", DateTimeOffset.UtcNow);

        (await NewQueries().UpdateContactCardAsync(scene.ManagerId, dialog.ContactId, "   ", null, null))
            .Should().BeFalse();
    }

    [Fact]
    public async Task StrangerCannotEditSomeoneElsesContact()
    {
        var owner = await CreateSceneAsync();
        var dialog = await AddDialogWithMessageAsync(owner, "Клиент", DateTimeOffset.UtcNow);

        (await NewQueries().UpdateContactCardAsync(Guid.CreateVersion7(), dialog.ContactId, "Взлом", null, null))
            .Should().BeFalse();
    }

    [Fact]
    public async Task BlankCrmAndNotesBecomeNull()
    {
        var scene = await CreateSceneAsync();
        var dialog = await AddDialogWithMessageAsync(scene, "Клиент", DateTimeOffset.UtcNow);

        await NewQueries().UpdateContactCardAsync(scene.ManagerId, dialog.ContactId, "Клиент", "   ", "");

        await using var dbContext = postgres.CreateDbContext();
        var contact = await dbContext.Contacts.SingleAsync(item => item.Id == dialog.ContactId);

        contact.CrmOrderNumber.Should().BeNull();
        contact.Notes.Should().BeNull();
    }

    // --- обвязка ---------------------------------------------------------

    private WorkspaceQueries NewQueries() =>
        new(new TestDbContextFactory(postgres), new EfAuditTrail(postgres.CreateDbContext()));

    private static DomainMessage Incoming(string text, MessageStatus status, DateTimeOffset? at = null) => new()
    {
        Direction = MessageDirection.Incoming,
        SenderType = SenderType.Client,
        Text = text,
        Status = status,
        CreatedAt = at ?? DateTimeOffset.UtcNow,
        PlatformMessageId = Random.Shared.Next(1, int.MaxValue).ToString(),
    };

    private static DomainMessage Outgoing(string text, MessageStatus status) => new()
    {
        Direction = MessageDirection.Outgoing,
        SenderType = SenderType.Manager,
        Text = text,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        PlatformMessageId = Random.Shared.Next(1, int.MaxValue).ToString(),
    };

    private async Task AddMessagesAsync(Guid dialogId, params DomainMessage[] messages)
    {
        await using var dbContext = postgres.CreateDbContext();

        foreach (var message in messages)
        {
            message.DialogId = dialogId;
            dbContext.Messages.Add(message);
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task<(Guid ContactId, Guid DialogId)> AddDialogWithMessageAsync(
        Scene scene,
        string contactName,
        DateTimeOffset lastMessageAt)
    {
        await using var dbContext = postgres.CreateDbContext();

        var contact = new Contact
        {
            DisplayName = contactName,
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
            MessengerAccountId = scene.TelegramAccountId,
            Platform = MessengerPlatform.Telegram,
            LastMessageAt = lastMessageAt,
            Messages = [Incoming("первое", MessageStatus.Delivered, lastMessageAt)],
        };

        dbContext.Contacts.Add(contact);
        dbContext.Dialogs.Add(dialog);
        await dbContext.SaveChangesAsync();

        return (contact.Id, dialog.Id);
    }

    private async Task AddSecondPlatformDialogAsync(Scene scene, Guid contactId)
    {
        await using var dbContext = postgres.CreateDbContext();

        var account = new MessengerAccount
        {
            ManagerId = scene.ManagerId,
            Platform = MessengerPlatform.WhatsApp,
            PhoneNumber = "+79001234567",
            Status = MessengerAccountStatus.Active,
        };

        var dialog = new Dialog
        {
            ContactId = contactId,
            MessengerAccountId = account.Id,
            Platform = MessengerPlatform.WhatsApp,
            LastMessageAt = DateTimeOffset.UtcNow,
            Messages = [Incoming("из вотсапа", MessageStatus.Delivered)],
        };

        dbContext.MessengerAccounts.Add(account);
        dbContext.ContactIdentities.Add(new ContactIdentity
        {
            ContactId = contactId,
            Platform = MessengerPlatform.WhatsApp,
            PlatformUserId = $"wa-{Random.Shared.NextInt64(1, long.MaxValue)}",
        });
        dbContext.Dialogs.Add(dialog);
        await dbContext.SaveChangesAsync();
    }

    private async Task<Scene> CreateSceneAsync()
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

        return new Scene(manager.Id, account.Id);
    }

    private sealed record Scene(Guid ManagerId, Guid TelegramAccountId);

    /// <summary>Фабрика контекстов поверх тестового контейнера.</summary>
    private sealed class TestDbContextFactory(PostgresFixture postgres)
        : IDbContextFactory<Infrastructure.Persistence.AppDbContext>
    {
        public Infrastructure.Persistence.AppDbContext CreateDbContext() => postgres.CreateDbContext();
    }
}
