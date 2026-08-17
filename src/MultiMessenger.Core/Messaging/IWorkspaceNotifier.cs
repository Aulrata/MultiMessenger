using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Messaging;

/// <summary>Что произошло в рабочем кабинете конкретного сотрудника.</summary>
public sealed record WorkspaceNotification
{
    /// <summary>Кому адресовано. Рассылать всем нельзя: переписка — персональные данные.</summary>
    public required Guid ManagerId { get; init; }

    public required Guid DialogId { get; init; }

    public required Guid ContactId { get; init; }

    public required MessengerPlatform Platform { get; init; }

    /// <summary>Идентификатор появившегося или изменившегося сообщения.</summary>
    public Guid? MessageId { get; init; }

    public required WorkspaceEventKind Kind { get; init; }
}

public enum WorkspaceEventKind
{
    MessageReceived,
    MessageSent,
    MessageEdited,
    MessageDeleted,
    ReadStatusChanged,
}

/// <summary>
/// Оповещение интерфейса о новых событиях в переписке.
/// <para>
/// Адресное, а не широковещательное: у каждого менеджера свои клиенты, и показывать
/// чужие сообщения нельзя ни на секунду. Подписчики — компоненты Blazor,
/// живущие столько же, сколько открытая страница.
/// </para>
/// </summary>
public interface IWorkspaceNotifier
{
    Task NotifyAsync(WorkspaceNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Подписывает обработчик на события конкретного сотрудника. Возврат
    /// освобождается при закрытии страницы — иначе подписки будут копиться.
    /// </summary>
    IDisposable Subscribe(Guid managerId, Func<WorkspaceNotification, Task> handler);
}
