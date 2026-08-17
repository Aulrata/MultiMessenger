using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;

namespace MultiMessenger.Infrastructure.Messengers;

/// <summary>Незавершённая попытка подключения канала.</summary>
public sealed record PendingLogin
{
    public required Guid Id { get; init; }

    public required Guid ManagerId { get; init; }

    public required Guid MessengerAccountId { get; init; }

    public required MessengerPlatform Platform { get; init; }

    public required IMessengerConnector Connector { get; init; }

    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// Хранит попытки входа между запросами.
/// <para>
/// Вход в Telegram растянут на несколько шагов: номер, код, иногда пароль. Между
/// ними проходят HTTP-запросы, а живое соединение с платформой должно сохраняться —
/// код подтверждения привязан именно к нему, и новое соединение его не примет.
/// </para>
/// <para>
/// Брошенные попытки убирает <see cref="PendingLoginCleanupWorker"/>: за каждой
/// стоит открытый сокет, и копить их нельзя.
/// </para>
/// </summary>
public sealed class PendingLoginStore(ILogger<PendingLoginStore> logger) : IAsyncDisposable
{
    /// <summary>
    /// Сколько живёт незавершённая попытка. Код подтверждения Telegram истекает
    /// быстрее, так что запас взят с избытком — на неспешного пользователя.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, PendingLogin> _logins = new();

    public int Count => _logins.Count;

    public PendingLogin Start(
        Guid managerId,
        Guid messengerAccountId,
        MessengerPlatform platform,
        IMessengerConnector connector)
    {
        var login = new PendingLogin
        {
            Id = Guid.CreateVersion7(),
            ManagerId = managerId,
            MessengerAccountId = messengerAccountId,
            Platform = platform,
            Connector = connector,
            StartedAt = DateTimeOffset.UtcNow,
        };

        _logins[login.Id] = login;

        return login;
    }

    /// <summary>
    /// Находит попытку, начатую этим же сотрудником. Проверка владельца обязательна:
    /// идентификатор попытки уходит в интерфейс, и без неё чужой вход можно было бы
    /// продолжить, подобрав его.
    /// </summary>
    public bool TryGet(Guid loginId, Guid managerId, out PendingLogin login)
    {
        if (_logins.TryGetValue(loginId, out var found) && found.ManagerId == managerId)
        {
            login = found;
            return true;
        }

        login = null!;
        return false;
    }

    /// <summary>Убирает попытку, не трогая соединение: оно переходит менеджеру подключений.</summary>
    public void Release(Guid loginId) => _logins.TryRemove(loginId, out _);

    /// <summary>Убирает попытку вместе с соединением — при отказе или отмене.</summary>
    public async Task AbandonAsync(Guid loginId)
    {
        if (_logins.TryRemove(loginId, out var login))
        {
            await DisposeConnectorAsync(login);
        }
    }

    public async Task<int> RemoveExpiredAsync(DateTimeOffset now)
    {
        var expired = _logins.Values.Where(login => now - login.StartedAt > Lifetime).ToList();

        foreach (var login in expired)
        {
            if (_logins.TryRemove(login.Id, out _))
            {
                logger.LogInformation(
                    "Попытка подключения канала {AccountId} брошена и очищена", login.MessengerAccountId);

                await DisposeConnectorAsync(login);
            }
        }

        return expired.Count;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var login in _logins.Values.ToList())
        {
            await AbandonAsync(login.Id);
        }
    }

    private async Task DisposeConnectorAsync(PendingLogin login)
    {
        try
        {
            await login.Connector.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception, "Не удалось закрыть соединение брошенной попытки {LoginId}", login.Id);
        }
    }
}
