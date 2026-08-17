using Microsoft.EntityFrameworkCore;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Enums;
using MultiMessenger.Infrastructure.Persistence;

namespace MultiMessenger.Infrastructure.Workspace;

/// <summary>
/// Чтение данных рабочего кабинета.
/// <para>
/// Контекст создаётся фабрикой на каждый вызов, а не берётся из области запроса:
/// в интерактивном Blazor область живёт столько же, сколько открытая страница,
/// и один <c>DbContext</c> на несколько часов накопил бы всё прочитанное в памяти.
/// </para>
/// <para>
/// Каждый запрос ограничен владельцем канала. Проверять права в интерфейсе
/// недостаточно: переписка — персональные данные, и фильтр должен стоять
/// в самом запросе.
/// </para>
/// </summary>
public sealed class WorkspaceQueries(IDbContextFactory<AppDbContext> dbContextFactory, IAuditTrail auditTrail)
{
    private const int DefaultPageSize = 50;

    public async Task<IReadOnlyList<ContactSummary>> GetContactsAsync(
        Guid managerId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var dialogs = await dbContext.Dialogs
            .Where(dialog => dialog.MessengerAccount!.ManagerId == managerId
                             && dialog.Status == DialogStatus.Active)
            .Select(dialog => new
            {
                dialog.Id,
                dialog.ContactId,
                dialog.Platform,
                dialog.LastMessageAt,
                ChannelStatus = dialog.MessengerAccount!.Status,
                ContactName = dialog.Contact!.DisplayName,
                dialog.Contact.CrmOrderNumber,
                Unread = dialog.Messages.Count(message =>
                    message.Direction == MessageDirection.Incoming && message.Status != MessageStatus.Read),
                LastText = dialog.Messages
                    .OrderByDescending(message => message.CreatedAt)
                    .Select(message => message.Text)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return dialogs
            .GroupBy(dialog => dialog.ContactId)
            .Select(group => new ContactSummary
            {
                ContactId = group.Key,
                DisplayName = group.First().ContactName,
                CrmOrderNumber = group.First().CrmOrderNumber,
                LastMessageAt = group.Max(dialog => dialog.LastMessageAt),
                UnreadCount = group.Sum(dialog => dialog.Unread),
                LastMessagePreview = group
                    .OrderByDescending(dialog => dialog.LastMessageAt)
                    .Select(dialog => dialog.LastText)
                    .FirstOrDefault(),
                Dialogs = group
                    .OrderByDescending(dialog => dialog.LastMessageAt)
                    .Select(dialog => new DialogSummary
                    {
                        DialogId = dialog.Id,
                        Platform = dialog.Platform,
                        LastMessageAt = dialog.LastMessageAt,
                        UnreadCount = dialog.Unread,
                        ChannelStatus = dialog.ChannelStatus,
                    })
                    .ToList(),
            })
            // Как в Telegram: свежие разговоры сверху, диалоги без сообщений в конце.
            .OrderByDescending(contact => contact.LastMessageAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// Страница истории. <paramref name="before"/> — время самого старого уже
    /// показанного сообщения; так подгружается всё, что выше.
    /// </summary>
    public async Task<MessagePage> GetMessagesAsync(
        Guid managerId,
        Guid dialogId,
        DateTimeOffset? before = null,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.Messages
            .Where(message => message.DialogId == dialogId
                              && message.Dialog!.MessengerAccount!.ManagerId == managerId);

        if (before is { } boundary)
        {
            query = query.Where(message => message.CreatedAt < boundary);
        }

        // Берём на одно больше запрошенного — так узнаём, есть ли что подгружать,
        // не считая всю переписку.
        var page = await query
            .OrderByDescending(message => message.CreatedAt)
            .Take(pageSize + 1)
            .Select(message => new MessageView
            {
                MessageId = message.Id,
                Direction = message.Direction,
                Text = message.Text,
                Status = message.Status,
                CreatedAt = message.CreatedAt,
                IsEdited = message.IsEdited,
                IsDeleted = message.IsDeleted,
                FailureReason = message.FailureReason,
                AttachmentCount = message.Attachments.Count,
            })
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > pageSize;

        return new MessagePage
        {
            // В интерфейсе снизу самые новые, поэтому возвращаем в прямом порядке.
            Messages = page.Take(pageSize).OrderBy(message => message.CreatedAt).ToList(),
            HasMore = hasMore,
        };
    }

    public async Task<ContactCard?> GetContactCardAsync(
        Guid managerId,
        Guid contactId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Contacts
            .Where(contact => contact.Id == contactId
                              && contact.Dialogs.Any(dialog => dialog.MessengerAccount!.ManagerId == managerId))
            .Select(contact => new ContactCard
            {
                ContactId = contact.Id,
                DisplayName = contact.DisplayName,
                CrmOrderNumber = contact.CrmOrderNumber,
                Notes = contact.Notes,
                PrimaryPlatform = contact.PrimaryPlatform,
                Channels = contact.Identities
                    .Select(identity => new ContactChannel
                    {
                        Platform = identity.Platform,
                        DisplayNameOnPlatform = identity.DisplayNameOnPlatform,
                    })
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Правка карточки: имя, номер заявки, заметки. Фиксируется в журнале аудита.</summary>
    public async Task<bool> UpdateContactCardAsync(
        Guid managerId,
        Guid contactId,
        string displayName,
        string? crmOrderNumber,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var contact = await dbContext.Contacts
            .SingleOrDefaultAsync(
                candidate => candidate.Id == contactId
                             && candidate.Dialogs.Any(dialog => dialog.MessengerAccount!.ManagerId == managerId),
                cancellationToken);

        if (contact is null)
        {
            return false;
        }

        contact.DisplayName = displayName.Trim();
        contact.CrmOrderNumber = string.IsNullOrWhiteSpace(crmOrderNumber) ? null : crmOrderNumber.Trim();
        contact.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry
            {
                ManagerId = managerId,
                Action = AuditAction.ContactUpdated,
                Subject = contact.DisplayName,
                EntityType = nameof(Core.Entities.Contact),
                EntityId = contact.Id,
            },
            cancellationToken);

        return true;
    }

    /// <summary>
    /// Помечает входящие прочитанными — гасит счётчик при открытии диалога.
    /// <para>
    /// Отметка ставится только у нас. Сообщить об этом платформе, чтобы прочтение
    /// увидел клиент и официальное приложение менеджера, — задача этапа 2.10.
    /// </para>
    /// </summary>
    public async Task<int> MarkDialogReadAsync(
        Guid managerId,
        Guid dialogId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Messages
            .Where(message => message.DialogId == dialogId
                              && message.Dialog!.MessengerAccount!.ManagerId == managerId
                              && message.Direction == MessageDirection.Incoming
                              && message.Status != MessageStatus.Read)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(message => message.Status, MessageStatus.Read),
                cancellationToken);
    }
}
