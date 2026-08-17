using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MultiMessenger.Core.Messaging;

namespace MultiMessenger.Infrastructure.Messengers;

/// <summary>
/// Оповещение в пределах одного процесса. Приложение работает одним экземпляром,
/// поэтому очередь сообщений между узлами не нужна; появится вторая реплика —
/// эту реализацию придётся заменить, интерфейс останется тем же.
/// </summary>
public sealed class WorkspaceNotifier(ILogger<WorkspaceNotifier> logger) : IWorkspaceNotifier
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Func<WorkspaceNotification, Task>>> _subscribers = new();

    public int SubscriberCount => _subscribers.Values.Sum(handlers => handlers.Count);

    public async Task NotifyAsync(WorkspaceNotification notification, CancellationToken cancellationToken = default)
    {
        if (!_subscribers.TryGetValue(notification.ManagerId, out var handlers))
        {
            // Никто не смотрит — нормальная ситуация: менеджер мог закрыть браузер.
            // Сообщение уже в базе и покажется, когда он вернётся.
            return;
        }

        foreach (var handler in handlers.Values)
        {
            try
            {
                await handler(notification);
            }
            catch (Exception exception)
            {
                // Отвалившаяся страница не должна мешать остальным и тем более
                // ломать обработку входящего сообщения.
                logger.LogWarning(exception, "Подписчик не смог обработать оповещение");
            }
        }
    }

    public IDisposable Subscribe(Guid managerId, Func<WorkspaceNotification, Task> handler)
    {
        var handlers = _subscribers.GetOrAdd(managerId, _ => new ConcurrentDictionary<Guid, Func<WorkspaceNotification, Task>>());
        var token = Guid.CreateVersion7();
        handlers[token] = handler;

        return new Subscription(() =>
        {
            handlers.TryRemove(token, out _);

            // Пустой словарь оставлять незачем: сотрудников десять, а страниц
            // за день открывается много.
            if (handlers.IsEmpty)
            {
                _subscribers.TryRemove(managerId, out _);
            }
        });
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            unsubscribe();
        }
    }
}
