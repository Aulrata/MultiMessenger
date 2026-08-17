using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using TL;
using TelegramMessage = TL.Message;

namespace MultiMessenger.Infrastructure.Messengers.Telegram;

/// <summary>
/// Коннектор Telegram поверх WTelegramClient (MTProto).
/// <para>
/// Вход только по номеру телефона. QR-вход библиотека умеет, но её авторы
/// не рекомендуют его для юзерботов — он заметнее для антифрода и повышает
/// риск блокировки аккаунта.
/// </para>
/// </summary>
public sealed class TelegramConnector : IMessengerConnector
{
    private readonly WTelegram.Client _client;
    private readonly IMessengerEventSink _sink;
    private readonly ILogger<TelegramConnector> _logger;
    private readonly TelegramPeerCache _peers;

    private static readonly TimeSpan PeerRefreshInterval = TimeSpan.FromMinutes(1);

    private string? _phoneNumber;
    private bool _updatesSubscribed;
    private DateTimeOffset _peersRefreshedAt = DateTimeOffset.MinValue;

    public TelegramConnector(
        Guid messengerAccountId,
        TelegramConnectorSettings settings,
        IMessengerEventSink sink,
        ILogger<TelegramConnector> logger)
    {
        MessengerAccountId = messengerAccountId;
        _sink = sink;
        _logger = logger;
        _peers = new TelegramPeerCache();

        var sessionPath = TelegramSessionStore.GetSessionPath(settings.SessionsBasePath, messengerAccountId);

        _client = new WTelegram.Client(key => key switch
        {
            "api_id" => settings.ApiId.ToString(),
            "api_hash" => settings.ApiHash,
            "session_pathname" => sessionPath,
            _ => null!,
        });

        // С сервера в Германии прокси не нужен, из России — обязателен.
        if (settings.MTProxyUrl is { Length: > 0 } proxyUrl)
        {
            _client.MTProxyUrl = proxyUrl;
        }
    }

    public MessengerPlatform Platform => MessengerPlatform.Telegram;

    public PlatformCapabilities Capabilities => TelegramCapabilities.Instance;

    public Guid MessengerAccountId { get; }

    // --- Подключение -----------------------------------------------------

    public async Task<LoginStep> BeginLoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return new LoginStep.Failed(LoginFailureReason.InvalidPhoneNumber, "не указан номер телефона");
        }

        _phoneNumber = request.PhoneNumber;

        return await StepAsync(() => _client.Login(request.PhoneNumber));
    }

    public async Task<LoginStep> ContinueLoginAsync(LoginAnswer answer, CancellationToken cancellationToken = default)
    {
        var value = answer switch
        {
            LoginAnswer.VerificationCode code => code.Code,
            LoginAnswer.TwoFactorPassword password => password.Password,
            // Telegram отвечает синхронно, опрашивать состояние нечего.
            LoginAnswer.CheckStatus => null,
            _ => null,
        };

        if (value is null)
        {
            return new LoginStep.Failed(LoginFailureReason.Unknown, "неожиданный ответ на текущем шаге входа");
        }

        return await StepAsync(() => _client.Login(value));
    }

    public async Task<ConnectionState> ConnectExistingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Login с номером на существующей сессии просто восстанавливает её
            // и возвращает null. Непустой ответ означает, что сессия больше не годится.
            var pending = await _client.Login(_phoneNumber);

            if (pending is not null)
            {
                await ReportConnectionAsync(ConnectionState.RequiresReauth, $"Telegram запросил {pending}");
                return ConnectionState.RequiresReauth;
            }

            SubscribeToUpdates();
            await ReportConnectionAsync(ConnectionState.Connected);

            return ConnectionState.Connected;
        }
        catch (RpcException exception) when (IsAuthFailure(exception))
        {
            await ReportConnectionAsync(ConnectionState.RequiresReauth, exception.Message);
            return ConnectionState.RequiresReauth;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Не удалось поднять сессию Telegram для канала {AccountId}", MessengerAccountId);
            await ReportConnectionAsync(ConnectionState.Disconnected, exception.Message);

            return ConnectionState.Disconnected;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _client.OnUpdates -= HandleUpdatesAsync;
        _updatesSubscribed = false;

        await _client.DisposeAsync();
        await ReportConnectionAsync(ConnectionState.Disconnected, "отключён по команде");
    }

    // --- Отправка --------------------------------------------------------

    public async Task<DeliveryResult> SendMessageAsync(
        OutgoingMessage message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var peer = await ResolvePeerAsync(message.PlatformUserId);
            TelegramMessage sent;

            if (message.Attachments.Count == 0)
            {
                sent = await _client.SendMessageAsync(peer, message.Text ?? string.Empty);
            }
            else
            {
                // Альбомы пока не отправляем: очередь ставит по одному вложению
                // на сообщение, а несколько файлов разом — задача не первой очереди.
                var attachment = message.Attachments[0];

                await using var content = await attachment.OpenAsync(cancellationToken);
                var uploaded = await _client.UploadFileAsync(content, attachment.FileName);

                sent = await _client.SendMediaAsync(
                    peer,
                    message.Text ?? string.Empty,
                    uploaded,
                    attachment.ContentType);
            }

            return DeliveryResult.Success(message.MessageId, sent.id.ToString(), sent.date);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ToFailure(message.MessageId, exception);
        }
    }

    public async Task<DeliveryResult> EditMessageAsync(
        EditMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var peer = await ResolvePeerAsync(request.PlatformUserId);

            await _client.Messages_EditMessage(peer, int.Parse(request.PlatformMessageId), request.NewText);

            return DeliveryResult.Success(request.MessageId, request.PlatformMessageId, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ToFailure(request.MessageId, exception);
        }
    }

    public async Task<DeliveryResult> DeleteMessageAsync(
        DeleteMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var peer = await ResolvePeerAsync(request.PlatformUserId);

            // DeleteMessages удаляет у всех участников — ровно то поведение,
            // которое выбрано для сервиса.
            await _client.DeleteMessages(peer, [int.Parse(request.PlatformMessageId)]);

            return DeliveryResult.Success(request.MessageId, request.PlatformMessageId, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ToFailure(request.MessageId, exception);
        }
    }

    // --- Вложения --------------------------------------------------------

    public async Task<Stream> DownloadAttachmentAsync(
        string platformFileReference,
        CancellationToken cancellationToken = default)
    {
        if (!TelegramMessageNormalizer.TryParseFileReference(platformFileReference, out var peerUserId, out var messageId, out _))
        {
            throw new ArgumentException($"Не разобрать ссылку на вложение: {platformFileReference}", nameof(platformFileReference));
        }

        var peer = await ResolvePeerAsync(peerUserId.ToString());
        var messages = await _client.GetMessages(peer, [new InputMessageID { id = messageId }]);

        if (messages.Messages.FirstOrDefault() is not TelegramMessage { media: { } media })
        {
            throw new InvalidOperationException($"Сообщение {messageId} не содержит вложения");
        }

        // Файл собирается в памяти: вложения мессенджеров невелики, а поток
        // сразу уходит в MinIO, где всё равно нужен размер.
        var buffer = new MemoryStream();

        switch (media)
        {
            case MessageMediaDocument { document: Document document }:
                await _client.DownloadFileAsync(document, buffer);
                break;
            case MessageMediaPhoto { photo: Photo photo }:
                await _client.DownloadFileAsync(photo, buffer);
                break;
            default:
                throw new InvalidOperationException($"Тип вложения {media.GetType().Name} не поддерживается");
        }

        buffer.Position = 0;

        return buffer;
    }

    // --- История ---------------------------------------------------------

    public async IAsyncEnumerable<IncomingMessage> EnumerateHistoryAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var dialogs = await _client.Messages_GetAllDialogs();

        foreach (var (userId, user) in dialogs.users)
        {
            if (user is not User { access_hash: var accessHash } resolved || resolved.IsBot)
            {
                continue;
            }

            _peers.Remember(userId, accessHash);
            var peer = new InputPeerUser(userId, accessHash);

            // Telegram отдаёт историю страницами от новых к старым.
            var offsetId = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var page = await _client.Messages_GetHistory(peer, offset_id: offsetId, limit: 100);

                if (page.Messages.Length == 0)
                {
                    break;
                }

                // Нормализатор возвращает null для групп и служебных сообщений;
                // OfType заодно отбрасывает их и снимает возможную null-ссылку.
                var normalizedPage = page.Messages
                    .OfType<TelegramMessage>()
                    .Select(item => TelegramMessageNormalizer.ToIncomingMessage(item, MessengerAccountId))
                    .OfType<IncomingMessage>();

                foreach (var normalized in normalizedPage)
                {
                    yield return normalized;
                }

                offsetId = page.Messages[^1].ID;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_updatesSubscribed)
        {
            _client.OnUpdates -= HandleUpdatesAsync;
        }

        await _client.DisposeAsync();
    }

    // --- Внутреннее ------------------------------------------------------

    private async Task<LoginStep> StepAsync(Func<Task<string?>> login)
    {
        try
        {
            var pending = await login();

            if (pending is null)
            {
                SubscribeToUpdates();
                await ReportConnectionAsync(ConnectionState.Connected);

                return new LoginStep.Completed(new PlatformAccountInfo
                {
                    PlatformUserId = _client.UserId.ToString(),
                    DisplayName = _client.User?.username ?? _client.User?.first_name,
                    PhoneNumber = _phoneNumber,
                });
            }

            return pending switch
            {
                "verification_code" => new LoginStep.NeedsVerificationCode(_phoneNumber),
                "password" => new LoginStep.NeedsTwoFactorPassword(null),
                // Регистрация нового аккаунта из системы не поддерживается:
                // номера менеджеров заводятся заранее и живут годами.
                "name" => new LoginStep.Failed(LoginFailureReason.Unknown, "номер не зарегистрирован в Telegram"),
                _ => new LoginStep.Failed(LoginFailureReason.Unknown, $"Telegram запросил {pending}"),
            };
        }
        catch (RpcException exception)
        {
            return new LoginStep.Failed(ToLoginFailure(exception), exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Ошибка входа в Telegram для канала {AccountId}", MessengerAccountId);

            return new LoginStep.Failed(LoginFailureReason.NetworkError, exception.Message);
        }
    }

    private void SubscribeToUpdates()
    {
        if (_updatesSubscribed)
        {
            return;
        }

        // Подписка только на OnUpdates. OnOwnUpdates намеренно не трогаем:
        // результат собственных вызовов уже возвращается из них самих,
        // и вторая обработка создала бы дубли.
        _client.OnUpdates += HandleUpdatesAsync;
        _updatesSubscribed = true;
    }

    private async Task HandleUpdatesAsync(UpdatesBase updates)
    {
        foreach (var (userId, user) in updates.Users)
        {
            if (user is User { access_hash: var accessHash })
            {
                _peers.Remember(userId, accessHash);
            }
        }

        foreach (var update in updates.UpdateList)
        {
            try
            {
                await HandleUpdateAsync(update);
            }
            catch (Exception exception)
            {
                // Одно неудачное событие не должно останавливать обработку остальных
                // и тем более рвать соединение.
                _logger.LogError(exception, "Не удалось обработать апдейт {Update}", update.GetType().Name);
            }
        }
    }

    private async Task HandleUpdateAsync(Update update)
    {
        switch (update)
        {
            case UpdateNewMessage { message: TelegramMessage message }:
                if (TelegramMessageNormalizer.ToIncomingMessage(message, MessengerAccountId) is { } incoming)
                {
                    await _sink.OnMessageReceivedAsync(incoming);
                }

                break;

            case UpdateEditMessage { message: TelegramMessage edited }:
                if (edited.peer_id is PeerUser editPeer)
                {
                    await _sink.OnMessageEditedAsync(new MessageEdited
                    {
                        MessengerAccountId = MessengerAccountId,
                        PlatformUserId = editPeer.user_id.ToString(),
                        PlatformMessageId = edited.id.ToString(),
                        NewText = edited.message,
                        OccurredAt = edited.edit_date == default ? edited.date : edited.edit_date,
                    });
                }

                break;

            case UpdateDeleteMessages deleted:
                // Telegram не сообщает, в каком диалоге удалено, — только номера
                // сообщений. Собеседника ищет уже приёмник: номер сообщения
                // уникален в пределах аккаунта.
                foreach (var messageId in deleted.messages)
                {
                    await _sink.OnMessageDeletedAsync(new MessageDeleted
                    {
                        MessengerAccountId = MessengerAccountId,
                        PlatformUserId = string.Empty,
                        PlatformMessageId = messageId.ToString(),
                        OccurredAt = DateTimeOffset.UtcNow,
                    });
                }

                break;

            // Менеджер прочитал переписку в официальном приложении — гасим счётчик.
            case UpdateReadHistoryInbox { peer: PeerUser inboxPeer }:
                await _sink.OnReadStatusChangedAsync(new ReadStatusChanged
                {
                    MessengerAccountId = MessengerAccountId,
                    PlatformUserId = inboxPeer.user_id.ToString(),
                    ReadUpTo = DateTimeOffset.UtcNow,
                    ReadByCounterparty = false,
                    OccurredAt = DateTimeOffset.UtcNow,
                });

                break;

            // Клиент прочитал наши сообщения — обновляем их статус.
            case UpdateReadHistoryOutbox { peer: PeerUser outboxPeer }:
                await _sink.OnReadStatusChangedAsync(new ReadStatusChanged
                {
                    MessengerAccountId = MessengerAccountId,
                    PlatformUserId = outboxPeer.user_id.ToString(),
                    ReadUpTo = DateTimeOffset.UtcNow,
                    ReadByCounterparty = true,
                    OccurredAt = DateTimeOffset.UtcNow,
                });

                break;
        }
    }

    /// <summary>
    /// Собирает адресата для MTProto. Кроме идентификатора нужен <c>access_hash</c>,
    /// без которого протокол обращаться к пользователю не позволяет.
    /// <para>
    /// Хеш приходит в словаре <c>Users</c> вместе с апдейтами, но не всегда: личные
    /// сообщения Telegram присылает компактной формой <c>updateShortMessage</c>,
    /// где этого словаря нет вовсе. Получить сообщение её хватает, а ответить — нет,
    /// поэтому при промахе кеша хеши перечитываются из списка диалогов: любой,
    /// кто написал, там присутствует.
    /// </para>
    /// </summary>
    private async Task<InputPeer> ResolvePeerAsync(string platformUserId)
    {
        var userId = long.Parse(platformUserId);

        if (_peers.TryGet(userId, out var accessHash))
        {
            return new InputPeerUser(userId, accessHash);
        }

        await RefreshPeersAsync();

        if (_peers.TryGet(userId, out accessHash))
        {
            return new InputPeerUser(userId, accessHash);
        }

        throw new InvalidOperationException(
            $"Не удалось определить access_hash пользователя {userId}: его нет ни в апдейтах, ни в списке диалогов");
    }

    /// <summary>
    /// Перечитывает хеши доступа из списка диалогов. Обращение к API не бесплатное,
    /// поэтому не чаще раза в минуту: при промахе на каждое сообщение из очереди
    /// это иначе превратилось бы в поток запросов.
    /// </summary>
    private async Task RefreshPeersAsync()
    {
        var now = DateTimeOffset.UtcNow;

        if (now - _peersRefreshedAt < PeerRefreshInterval)
        {
            return;
        }

        _peersRefreshedAt = now;

        var dialogs = await _client.Messages_GetAllDialogs();

        foreach (var (userId, user) in dialogs.users)
        {
            if (user is User { access_hash: var hash })
            {
                _peers.Remember(userId, hash);
            }
        }

        _logger.LogDebug(
            "Обновлены хеши доступа по каналу {AccountId}, пользователей: {Count}",
            MessengerAccountId,
            dialogs.users.Count);
    }

    private Task ReportConnectionAsync(ConnectionState state, string? details = null) =>
        _sink.OnConnectionStateChangedAsync(new ConnectionStateChanged
        {
            MessengerAccountId = MessengerAccountId,
            State = state,
            OccurredAt = DateTimeOffset.UtcNow,
            Details = details,
        });

    private static bool IsAuthFailure(RpcException exception) =>
        exception.Code == 401 || exception.Message.Contains("AUTH_KEY", StringComparison.Ordinal);

    private static LoginFailureReason ToLoginFailure(RpcException exception) => exception.Message switch
    {
        var message when message.Contains("PHONE_NUMBER_INVALID", StringComparison.Ordinal) => LoginFailureReason.InvalidPhoneNumber,
        var message when message.Contains("PHONE_CODE_INVALID", StringComparison.Ordinal) => LoginFailureReason.InvalidCode,
        var message when message.Contains("PHONE_CODE_EXPIRED", StringComparison.Ordinal) => LoginFailureReason.Expired,
        var message when message.Contains("PASSWORD_HASH_INVALID", StringComparison.Ordinal) => LoginFailureReason.InvalidPassword,
        var message when message.Contains("FLOOD_WAIT", StringComparison.Ordinal) => LoginFailureReason.RateLimited,
        var message when message.Contains("BANNED", StringComparison.Ordinal) => LoginFailureReason.AccountBanned,
        _ => LoginFailureReason.Unknown,
    };

    /// <summary>
    /// Перевод исключения платформы в исход доставки. Отдельного внимания стоит
    /// FLOOD_WAIT: Telegram прямо говорит, сколько ждать, и это значение важнее
    /// любых наших пауз в очереди.
    /// </summary>
    private DeliveryResult ToFailure(Guid messageId, Exception exception)
    {
        if (exception is RpcException rpc)
        {
            if (rpc.Message.Contains("FLOOD_WAIT", StringComparison.Ordinal))
            {
                return DeliveryResult.Failure(
                    messageId,
                    DeliveryFailureReason.RateLimited,
                    rpc.Message,
                    rpc.X > 0 ? TimeSpan.FromSeconds(rpc.X) : null);
            }

            var reason = rpc.Message switch
            {
                var m when m.Contains("MESSAGE_ID_INVALID", StringComparison.Ordinal) => DeliveryFailureReason.MessageNotFound,
                var m when m.Contains("MESSAGE_EDIT_TIME_EXPIRED", StringComparison.Ordinal) => DeliveryFailureReason.WindowExpired,
                var m when m.Contains("USER_IS_BLOCKED", StringComparison.Ordinal) => DeliveryFailureReason.RecipientUnavailable,
                var m when m.Contains("PEER_ID_INVALID", StringComparison.Ordinal) => DeliveryFailureReason.RecipientUnavailable,
                _ when rpc.Code == 401 => DeliveryFailureReason.AuthenticationExpired,
                _ => DeliveryFailureReason.Unknown,
            };

            return DeliveryResult.Failure(messageId, reason, rpc.Message);
        }

        _logger.LogWarning(exception, "Операция с сообщением {MessageId} не удалась", messageId);

        return DeliveryResult.Failure(messageId, DeliveryFailureReason.NetworkError, exception.Message);
    }
}
