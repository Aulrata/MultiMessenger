using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Persistence;
using DomainMessage = MultiMessenger.Core.Entities.Message;

namespace MultiMessenger.Infrastructure.Messengers.Inbox;

public enum InboxOutcome
{
    /// <summary>Сообщение сохранено.</summary>
    Stored,

    /// <summary>Уже было в базе — пришло повторно.</summary>
    Duplicate,

    /// <summary>Канал неизвестен: запись удалили, а апдейт по нему ещё летел.</summary>
    UnknownAccount,
}

public sealed record InboxResult(InboxOutcome Outcome, Guid? MessageId = null, Guid? DialogId = null);

/// <summary>
/// Приём входящих: событие платформы превращается в контакт, диалог и сообщение.
/// <para>
/// Всё, что приходит от коннекторов, сначала попадает в базу и только потом
/// показывается в интерфейсе. Интерфейс никогда не работает с платформой напрямую —
/// это даёт единую точку правды и защищает от потери сообщений при сбоях сети.
/// </para>
/// </summary>
public sealed class InboxService(
    AppDbContext dbContext,
    IWorkspaceNotifier notifier,
    ILogger<InboxService> logger)
{
    public async Task<InboxResult> HandleAsync(IncomingMessage message, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MessengerAccounts
            .SingleOrDefaultAsync(candidate => candidate.Id == message.MessengerAccountId, cancellationToken);

        if (account is null)
        {
            logger.LogWarning(
                "Сообщение по неизвестному каналу {AccountId} отброшено", message.MessengerAccountId);

            return new InboxResult(InboxOutcome.UnknownAccount);
        }

        // Два входящих от нового клиента могут прийти одновременно и оба попытаться
        // создать контакт. Уникальный индекс на паре «платформа + идентификатор»
        // не даст задвоить, но вторая транзакция упадёт — её нужно просто повторить,
        // и на второй попытке контакт уже найдётся.
        try
        {
            return await StoreAsync(account, message, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogInformation(
                exception,
                "Столкновение при сохранении сообщения {PlatformMessageId}, повторяю",
                message.PlatformMessageId);

            dbContext.ChangeTracker.Clear();

            return await StoreAsync(account, message, cancellationToken);
        }
    }

    private async Task<InboxResult> StoreAsync(
        MessengerAccount account,
        IncomingMessage message,
        CancellationToken cancellationToken)
    {
        var contact = await FindOrCreateContactAsync(account.Platform, message, cancellationToken);
        var dialog = await FindOrCreateDialogAsync(account, contact, cancellationToken);

        // Ключ дедупликации из ТЗ. Пригодится и своим сообщениям, отправленным
        // с телефона менеджера: тот же апдейт прилетит и сюда.
        var alreadyStored = await dbContext.Messages.AnyAsync(
            candidate => candidate.DialogId == dialog.Id
                         && candidate.PlatformMessageId == message.PlatformMessageId,
            cancellationToken);

        if (alreadyStored)
        {
            return new InboxResult(InboxOutcome.Duplicate, DialogId: dialog.Id);
        }

        var isIncoming = message.Direction is MessageDirection.Incoming;

        var stored = new DomainMessage
        {
            DialogId = dialog.Id,
            Direction = message.Direction,
            SenderType = isIncoming ? SenderType.Client : SenderType.Manager,
            PlatformMessageId = message.PlatformMessageId,
            Text = message.Text,
            // Входящее доставлено нам, но менеджер его ещё не читал — по этому
            // и считается счётчик непрочитанных.
            Status = isIncoming ? MessageStatus.Delivered : MessageStatus.Sent,
            CreatedAt = message.OccurredAt,
        };

        dbContext.Messages.Add(stored);

        // Список диалогов сортируется по этому полю. Сообщение из подгружаемой
        // истории не должно поднимать диалог наверх, поэтому только вперёд.
        if (dialog.LastMessageAt is null || dialog.LastMessageAt < message.OccurredAt)
        {
            dialog.LastMessageAt = message.OccurredAt;
        }

        // «Платформа последнего входящего» — куда по умолчанию отвечать. Своё
        // сообщение с телефона на это не влияет.
        if (isIncoming)
        {
            contact.PrimaryPlatform = account.Platform;
        }

        account.LastActiveAt = message.OccurredAt;

        if (message.Attachments.Count > 0)
        {
            // Скачивание в MinIO появится на этапе 2.9. Само сообщение сохраняется
            // уже сейчас, чтобы в переписке не было дыр, а ссылку на файл можно
            // будет собрать заново из собеседника и номера сообщения.
            logger.LogInformation(
                "Сообщение {PlatformMessageId} содержит вложений: {Count}; загрузка появится на этапе 2.9",
                message.PlatformMessageId,
                message.Attachments.Count);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await notifier.NotifyAsync(
            new WorkspaceNotification
            {
                ManagerId = account.ManagerId,
                DialogId = dialog.Id,
                ContactId = contact.Id,
                Platform = account.Platform,
                MessageId = stored.Id,
                Kind = isIncoming ? WorkspaceEventKind.MessageReceived : WorkspaceEventKind.MessageSent,
            },
            cancellationToken);

        return new InboxResult(InboxOutcome.Stored, stored.Id, dialog.Id);
    }

    private async Task<Contact> FindOrCreateContactAsync(
        MessengerPlatform platform,
        IncomingMessage message,
        CancellationToken cancellationToken)
    {
        var identity = await dbContext.ContactIdentities
            .Include(candidate => candidate.Contact)
            .SingleOrDefaultAsync(
                candidate => candidate.Platform == platform
                             && candidate.PlatformUserId == message.PlatformUserId,
                cancellationToken);

        if (identity?.Contact is { } existing)
        {
            // Имя на платформе клиент может сменить; в карточке контакта имя
            // правит менеджер, поэтому обновляем только платформенное.
            if (!string.IsNullOrWhiteSpace(message.SenderDisplayName)
                && identity.DisplayNameOnPlatform != message.SenderDisplayName)
            {
                identity.DisplayNameOnPlatform = message.SenderDisplayName;
            }

            return existing;
        }

        var contact = new Contact
        {
            // Пока менеджер не переименовал контакт, показываем то, что известно
            // от платформы. Идентификатор — последнее средство: у клиента может
            // быть скрыто и имя, и username.
            DisplayName = message.SenderDisplayName is { Length: > 0 } name ? name : message.PlatformUserId,
            PrimaryPlatform = platform,
            Identities =
            [
                new ContactIdentity
                {
                    Platform = platform,
                    PlatformUserId = message.PlatformUserId,
                    DisplayNameOnPlatform = message.SenderDisplayName,
                },
            ],
        };

        dbContext.Contacts.Add(contact);

        return contact;
    }

    private async Task<Dialog> FindOrCreateDialogAsync(
        MessengerAccount account,
        Contact contact,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Dialogs.SingleOrDefaultAsync(
            candidate => candidate.ContactId == contact.Id && candidate.MessengerAccountId == account.Id,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var dialog = new Dialog
        {
            ContactId = contact.Id,
            MessengerAccountId = account.Id,
            Platform = account.Platform,
        };

        dbContext.Dialogs.Add(dialog);

        return dialog;
    }
}
