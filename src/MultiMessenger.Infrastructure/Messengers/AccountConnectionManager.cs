using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;

namespace MultiMessenger.Infrastructure.Messengers;

/// <summary>
/// Держит живые подключения каналов: «идентификатор канала → активный коннектор».
/// <para>
/// Единственный на приложение. Коннектор — это открытое соединение с Telegram
/// и файл сессии на диске; два экземпляра на один аккаунт означали бы две сессии,
/// а частые перелогины повышают подозрительность для антифрода платформы.
/// Поэтому подключение каждого канала защищено собственным замком.
/// </para>
/// </summary>
public sealed class AccountConnectionManager(
    IEnumerable<IMessengerConnectorFactory> factories,
    IMessengerEventSink eventSink,
    ILogger<AccountConnectionManager> logger) : IAsyncDisposable
{
    private readonly Dictionary<MessengerPlatform, IMessengerConnectorFactory> _factories =
        factories.ToDictionary(factory => factory.Platform);

    private readonly ConcurrentDictionary<Guid, IMessengerConnector> _connectors = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    private bool _disposed;

    public IReadOnlyCollection<Guid> ConnectedAccounts => _connectors.Keys.ToArray();

    public bool TryGet(Guid messengerAccountId, out IMessengerConnector connector) =>
        _connectors.TryGetValue(messengerAccountId, out connector!);

    public bool SupportsPlatform(MessengerPlatform platform) => _factories.ContainsKey(platform);

    /// <summary>
    /// Поднимает канал по сохранённой сессии. Повторный вызов для уже подключённого
    /// канала ничего не делает и возвращает <see cref="ConnectionState.Connected"/>.
    /// </summary>
    public async Task<ConnectionState> ConnectAsync(
        Guid messengerAccountId,
        MessengerPlatform platform,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_factories.TryGetValue(platform, out var factory))
        {
            logger.LogError("Нет коннектора для платформы {Platform}", platform);
            return ConnectionState.Disconnected;
        }

        var accountLock = _locks.GetOrAdd(messengerAccountId, _ => new SemaphoreSlim(1, 1));
        await accountLock.WaitAsync(cancellationToken);

        try
        {
            if (_connectors.ContainsKey(messengerAccountId))
            {
                return ConnectionState.Connected;
            }

            IMessengerConnector connector;

            try
            {
                // Создание тоже под защитой: фабрика Telegram именно здесь читает
                // настройки, и при незаполненных api_id/api_hash падает отсюда.
                connector = factory.Create(messengerAccountId, eventSink);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Не удалось создать коннектор {Platform} для канала {AccountId}",
                    platform,
                    messengerAccountId);

                return ConnectionState.Disconnected;
            }

            ConnectionState state;

            try
            {
                state = await connector.ConnectExistingAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Канал {AccountId} не удалось подключить", messengerAccountId);
                await connector.DisposeAsync();

                return ConnectionState.Disconnected;
            }

            if (state is not ConnectionState.Connected)
            {
                // Неподключившийся коннектор в словаре не оставляем: иначе отправка
                // решит, что канал жив, и сообщения будут теряться молча.
                await connector.DisposeAsync();
                return state;
            }

            _connectors[messengerAccountId] = connector;
            logger.LogInformation("Канал {AccountId} ({Platform}) подключён", messengerAccountId, platform);

            return ConnectionState.Connected;
        }
        finally
        {
            accountLock.Release();
        }
    }

    /// <summary>
    /// Берёт под управление коннектор, который уже прошёл вход через интерфейс.
    /// Нужен флоу подключения из 2.4: там соединение устанавливается в процессе
    /// диалога с пользователем, и переподключаться заново незачем.
    /// </summary>
    public async Task AdoptAsync(Guid messengerAccountId, IMessengerConnector connector)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connectors.TryRemove(messengerAccountId, out var previous))
        {
            await previous.DisposeAsync();
        }

        _connectors[messengerAccountId] = connector;
        logger.LogInformation("Канал {AccountId} подключён через интерфейс", messengerAccountId);
    }

    public async Task DisconnectAsync(Guid messengerAccountId, CancellationToken cancellationToken = default)
    {
        if (!_connectors.TryRemove(messengerAccountId, out var connector))
        {
            return;
        }

        try
        {
            await connector.DisconnectAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ошибка при отключении канала {AccountId}", messengerAccountId);
        }
        finally
        {
            await connector.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var accountId in _connectors.Keys.ToArray())
        {
            await DisconnectAsync(accountId);
        }

        foreach (var accountLock in _locks.Values)
        {
            accountLock.Dispose();
        }
    }
}
