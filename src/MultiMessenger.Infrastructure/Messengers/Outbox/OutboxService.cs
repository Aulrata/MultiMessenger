using Microsoft.EntityFrameworkCore;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Persistence;
using DomainMessage = MultiMessenger.Core.Entities.Message;

namespace MultiMessenger.Infrastructure.Messengers.Outbox;

public enum EnqueueFailure
{
    None,
    DialogNotFound,
    EmptyMessage,
    ChannelUnavailable,
}

public sealed record EnqueueResult(Guid? MessageId, EnqueueFailure Failure)
{
    public bool Succeeded => Failure is EnqueueFailure.None && MessageId is not null;
}

/// <summary>
/// Постановка исходящих в очередь.
/// <para>
/// Сообщение сразу попадает в базу со статусом <see cref="MessageStatus.Pending"/>
/// и тут же показывается менеджеру — так работает Inbox/Outbox из ТЗ. Реальная
/// отправка происходит позже, в <see cref="OutboxWorker"/>: это защищает
/// от потери сообщений при обрыве сети и позволяет соблюдать паузы между отправками.
/// </para>
/// </summary>
public sealed class OutboxService(
    AppDbContext dbContext,
    IWorkspaceNotifier notifier)
{
    public async Task<EnqueueResult> EnqueueAsync(
        Guid managerId,
        Guid dialogId,
        string? text,
        Guid? sentByManagerId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new EnqueueResult(null, EnqueueFailure.EmptyMessage);
        }

        var dialog = await dbContext.Dialogs
            .Include(item => item.MessengerAccount)
            .SingleOrDefaultAsync(item => item.Id == dialogId, cancellationToken);

        // Проверка владельца канала: диалог чужого менеджера трогать нельзя.
        if (dialog?.MessengerAccount is not { } account || account.ManagerId != managerId)
        {
            return new EnqueueResult(null, EnqueueFailure.DialogNotFound);
        }

        if (account.Status is MessengerAccountStatus.RequiresReauth)
        {
            // Отправлять некуда: сессия недействительна, и сообщение просто копилось бы
            // в очереди. Честнее сказать сразу.
            return new EnqueueResult(null, EnqueueFailure.ChannelUnavailable);
        }

        var now = DateTimeOffset.UtcNow;

        var message = new DomainMessage
        {
            DialogId = dialog.Id,
            Direction = MessageDirection.Outgoing,
            SenderType = SenderType.Manager,
            Text = text.Trim(),
            Status = MessageStatus.Pending,
            CreatedAt = now,
            // Кто фактически отправил. Отличается от владельца канала, когда
            // сотрудник работает через мультиаккаунт.
            SentByManagerId = sentByManagerId ?? managerId,
        };

        dbContext.Messages.Add(message);

        if (dialog.LastMessageAt is null || dialog.LastMessageAt < now)
        {
            dialog.LastMessageAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Немедленное отображение в интерфейсе: сообщение уже в базе,
        // хотя платформа его ещё не видела.
        await notifier.NotifyAsync(
            new WorkspaceNotification
            {
                ManagerId = account.ManagerId,
                DialogId = dialog.Id,
                ContactId = dialog.ContactId,
                Platform = dialog.Platform,
                MessageId = message.Id,
                Kind = WorkspaceEventKind.MessageSent,
            },
            cancellationToken);

        return new EnqueueResult(message.Id, EnqueueFailure.None);
    }
}
