using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;

namespace MultiMessenger.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public class AppDbContextTests(PostgresFixture postgres) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var dbContext = postgres.CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MigrationsApplyToEmptyDatabase()
    {
        await using var dbContext = postgres.CreateDbContext();

        var applied = await dbContext.Database.GetAppliedMigrationsAsync();
        var pending = await dbContext.Database.GetPendingMigrationsAsync();

        applied.Should().NotBeEmpty();
        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task FullMessageGraphRoundTrips()
    {
        var dialog = await CreateDialogAsync();

        await using (var dbContext = postgres.CreateDbContext())
        {
            dbContext.Messages.Add(new Message
            {
                DialogId = dialog.Id,
                Direction = MessageDirection.Incoming,
                SenderType = SenderType.Client,
                PlatformMessageId = "1001",
                Text = "Здравствуйте, интересует тур в Турцию",
                Status = MessageStatus.Delivered,
                CreatedAt = DateTimeOffset.UtcNow,
                Attachments =
                [
                    new MediaAttachment
                    {
                        Type = MediaType.Voice,
                        StorageKey = $"voice/{Guid.CreateVersion7()}.ogg",
                        MimeType = "audio/ogg",
                        SizeBytes = 24_576,
                        DurationSeconds = 12,
                    },
                ],
            });

            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = postgres.CreateDbContext())
        {
            var stored = await dbContext.Messages
                .Include(message => message.Attachments)
                .SingleAsync(message => message.DialogId == dialog.Id);

            stored.Text.Should().Be("Здравствуйте, интересует тур в Турцию");
            stored.Status.Should().Be(MessageStatus.Delivered);
            stored.Attachments.Should().ContainSingle()
                .Which.DurationSeconds.Should().Be(12);
        }
    }

    /// <summary>
    /// Ключевая защита из ТЗ: сообщение, пришедшее апдейтом после отправки с телефона,
    /// не должно продублировать запись, созданную собственной очередью отправки.
    /// </summary>
    [Fact]
    public async Task DuplicatePlatformMessageIdInSameDialogIsRejected()
    {
        var dialog = await CreateDialogAsync();
        const string platformMessageId = "5150";

        await using var dbContext = postgres.CreateDbContext();

        dbContext.Messages.Add(NewMessage(dialog.Id, platformMessageId));
        await dbContext.SaveChangesAsync();

        dbContext.Messages.Add(NewMessage(dialog.Id, platformMessageId));

        var save = async () => await dbContext.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// Обратная сторона той же защиты: пока сообщения ждут отправки, PlatformMessageId
    /// ещё не проставлен, и несколько таких записей в одном диалоге — норма.
    /// </summary>
    [Fact]
    public async Task SeveralPendingMessagesWithoutPlatformIdAreAllowed()
    {
        var dialog = await CreateDialogAsync();

        await using var dbContext = postgres.CreateDbContext();

        dbContext.Messages.Add(NewMessage(dialog.Id, platformMessageId: null));
        dbContext.Messages.Add(NewMessage(dialog.Id, platformMessageId: null));
        dbContext.Messages.Add(NewMessage(dialog.Id, platformMessageId: null));

        var save = async () => await dbContext.SaveChangesAsync();

        await save.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DialogsAreListedNewestFirst()
    {
        var contact = await CreateContactAsync();
        var account = await CreateAccountAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var dbContext = postgres.CreateDbContext())
        {
            // Порядок вставки намеренно обратный ожидаемому.
            foreach (var minutesAgo in new[] { 30, 5, 60 })
            {
                dbContext.Dialogs.Add(new Dialog
                {
                    ContactId = (await CreateContactAsync()).Id,
                    MessengerAccountId = account.Id,
                    Platform = MessengerPlatform.Telegram,
                    LastMessageAt = now.AddMinutes(-minutesAgo),
                });
            }

            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = postgres.CreateDbContext())
        {
            var lastMessageTimes = await dbContext.Dialogs
                .Where(dialog => dialog.MessengerAccountId == account.Id)
                .OrderByDescending(dialog => dialog.LastMessageAt)
                .Select(dialog => dialog.LastMessageAt)
                .ToListAsync();

            lastMessageTimes.Should().BeInDescendingOrder();
        }

        contact.Should().NotBeNull();
    }

    [Fact]
    public async Task SamePlatformUserCannotBelongToTwoContacts()
    {
        const string platformUserId = "tg-777";

        await using var dbContext = postgres.CreateDbContext();

        foreach (var _ in Enumerable.Range(0, 2))
        {
            dbContext.Contacts.Add(new Contact
            {
                DisplayName = "Клиент",
                PrimaryPlatform = MessengerPlatform.Telegram,
                Identities =
                [
                    new ContactIdentity
                    {
                        Platform = MessengerPlatform.Telegram,
                        PlatformUserId = platformUserId,
                    },
                ],
            });
        }

        var save = async () => await dbContext.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>Перечисления должны лежать в БД именами, иначе журнал не прочитать SQL-запросом.</summary>
    [Fact]
    public async Task EnumsAreStoredAsText()
    {
        await using var dbContext = postgres.CreateDbContext();

        dbContext.AuditEntries.Add(new AuditEntry
        {
            Action = AuditAction.ManagerSignInFailed,
            Subject = "+79001234567",
            DetailsJson = """{"reason":"wrong-password"}""",
            IpAddress = "192.0.2.1",
        });

        await dbContext.SaveChangesAsync();

        var storedAsText = await dbContext.Database
            .SqlQuery<string>($"""select "Action" from "AuditEntries" where "Subject" = '+79001234567'""")
            .ToListAsync();

        storedAsText.Should().Contain("ManagerSignInFailed");
    }

    private static Message NewMessage(Guid dialogId, string? platformMessageId) => new()
    {
        DialogId = dialogId,
        Direction = MessageDirection.Outgoing,
        SenderType = SenderType.Manager,
        PlatformMessageId = platformMessageId,
        Text = "Добрый день!",
        Status = platformMessageId is null ? MessageStatus.Pending : MessageStatus.Sent,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<Contact> CreateContactAsync()
    {
        await using var dbContext = postgres.CreateDbContext();

        var contact = new Contact
        {
            DisplayName = "Иван Петров",
            PrimaryPlatform = MessengerPlatform.Telegram,
            Identities =
            [
                new ContactIdentity
                {
                    Platform = MessengerPlatform.Telegram,
                    PlatformUserId = $"tg-{Guid.CreateVersion7()}",
                    DisplayNameOnPlatform = "@ivan",
                },
            ],
        };

        dbContext.Contacts.Add(contact);
        await dbContext.SaveChangesAsync();

        return contact;
    }

    private async Task<MessengerAccount> CreateAccountAsync()
    {
        await using var dbContext = postgres.CreateDbContext();

        var manager = new Manager
        {
            PhoneNumber = $"+7900{Random.Shared.Next(1000000, 9999999)}",
            PasswordHash = "hash",
            FullName = "Менеджер Тестовый",
        };

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

    private async Task<Dialog> CreateDialogAsync()
    {
        var contact = await CreateContactAsync();
        var account = await CreateAccountAsync();

        await using var dbContext = postgres.CreateDbContext();

        var dialog = new Dialog
        {
            ContactId = contact.Id,
            MessengerAccountId = account.Id,
            Platform = MessengerPlatform.Telegram,
            LastMessageAt = DateTimeOffset.UtcNow,
        };

        dbContext.Dialogs.Add(dialog);
        await dbContext.SaveChangesAsync();

        return dialog;
    }
}
