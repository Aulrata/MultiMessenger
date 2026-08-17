using System.Runtime.CompilerServices;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;

namespace MultiMessenger.Tests.Messaging;

/// <summary>
/// Заготовки коннекторов под три способа входа, которые встретятся на этапах 2 и 3.
/// Существуют ради одной проверки: подходит ли <see cref="IMessengerConnector"/>
/// всем платформам без изменений. Если для WhatsApp или MAX пришлось бы менять
/// интерфейс, это стало бы видно здесь, а не в разгар третьего этапа.
/// </summary>
public abstract class FakeConnectorBase : IMessengerConnector
{
    public abstract MessengerPlatform Platform { get; }

    public abstract PlatformCapabilities Capabilities { get; }

    public Guid MessengerAccountId { get; init; } = Guid.CreateVersion7();

    public abstract Task<LoginStep> BeginLoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    public abstract Task<LoginStep> ContinueLoginAsync(LoginAnswer answer, CancellationToken cancellationToken = default);

    // Виртуальные намеренно: наследники подменяют поведение соединения,
    // а вызовы идут через интерфейс — сокрытие через new там не сработает.
    public virtual Task<ConnectionState> ConnectExistingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ConnectionState.Connected);

    public virtual Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    // Виртуальные по той же причине, что и методы соединения: наследники подменяют
    // поведение, а вызовы идут через интерфейс — сокрытие через new там не работает.
    public virtual Task<DeliveryResult> SendMessageAsync(OutgoingMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeliveryResult.Success(message.MessageId, "platform-1", DateTimeOffset.UtcNow));

    public virtual Task<DeliveryResult> EditMessageAsync(EditMessageRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeliveryResult.Success(request.MessageId, request.PlatformMessageId, DateTimeOffset.UtcNow));

    public virtual Task<DeliveryResult> DeleteMessageAsync(DeleteMessageRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeliveryResult.Success(request.MessageId, request.PlatformMessageId, DateTimeOffset.UtcNow));

    public virtual Task<Stream> DownloadAttachmentAsync(string platformFileReference, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream("файл"u8.ToArray()));

    public virtual async IAsyncEnumerable<IncomingMessage> EnumerateHistoryAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected static LoginStep.Completed CompletedWith(string platformUserId) =>
        new(new PlatformAccountInfo { PlatformUserId = platformUserId });
}

/// <summary>Telegram: номер → код → пароль двухфакторной защиты.</summary>
public sealed class PhoneAndCodeConnector : FakeConnectorBase
{
    private enum Stage { NotStarted, AwaitingCode, AwaitingPassword, Done }

    private Stage _stage = Stage.NotStarted;

    public override MessengerPlatform Platform => MessengerPlatform.Telegram;

    public override PlatformCapabilities Capabilities => new()
    {
        LoginMethod = LoginMethod.PhoneAndCode,
        RequiresPersistentConnection = true,
        SupportsHistoryBackfill = true,
        HistoryWindow = null,
        SupportsEditing = true,
        EditWindow = TimeSpan.FromHours(48),
        SupportsDeleteForEveryone = true,
        DeleteForEveryoneWindow = null,
    };

    public override Task<LoginStep> BeginLoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Task.FromResult<LoginStep>(new LoginStep.Failed(LoginFailureReason.InvalidPhoneNumber));
        }

        _stage = Stage.AwaitingCode;

        return Task.FromResult<LoginStep>(new LoginStep.NeedsVerificationCode(request.PhoneNumber));
    }

    public override Task<LoginStep> ContinueLoginAsync(LoginAnswer answer, CancellationToken cancellationToken = default)
    {
        LoginStep step = (_stage, answer) switch
        {
            (Stage.AwaitingCode, LoginAnswer.VerificationCode { Code: "12345" }) => Next(
                Stage.AwaitingPassword, new LoginStep.NeedsTwoFactorPassword("подсказка")),
            (Stage.AwaitingCode, LoginAnswer.VerificationCode) => new LoginStep.Failed(LoginFailureReason.InvalidCode),
            (Stage.AwaitingPassword, LoginAnswer.TwoFactorPassword { Password: "секрет" }) => Next(
                Stage.Done, CompletedWith("tg-1001")),
            (Stage.AwaitingPassword, LoginAnswer.TwoFactorPassword) => new LoginStep.Failed(LoginFailureReason.InvalidPassword),
            _ => new LoginStep.Failed(LoginFailureReason.Unknown, "неожиданный ответ на текущем шаге"),
        };

        return Task.FromResult(step);
    }

    private LoginStep Next(Stage stage, LoginStep step)
    {
        _stage = stage;
        return step;
    }
}

/// <summary>WhatsApp: QR-код, который сканируют телефоном, с ожиданием подтверждения.</summary>
public sealed class QrCodeConnector(int pollsBeforeSuccess = 2) : FakeConnectorBase
{
    private int _polls;

    public override MessengerPlatform Platform => MessengerPlatform.WhatsApp;

    public override PlatformCapabilities Capabilities => new()
    {
        LoginMethod = LoginMethod.QrCode,
        RequiresPersistentConnection = true,
        SupportsHistoryBackfill = true,
        // История ограничена окном, которое отдаёт протокол WhatsApp Web.
        HistoryWindow = TimeSpan.FromDays(90),
        SupportsEditing = true,
        EditWindow = TimeSpan.FromMinutes(15),
        SupportsDeleteForEveryone = true,
        DeleteForEveryoneWindow = TimeSpan.FromHours(2),
    };

    public override Task<LoginStep> BeginLoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoginStep>(new LoginStep.NeedsQrScan("wa-qr-payload", DateTimeOffset.UtcNow.AddMinutes(1)));

    public override Task<LoginStep> ContinueLoginAsync(LoginAnswer answer, CancellationToken cancellationToken = default)
    {
        if (answer is not LoginAnswer.CheckStatus)
        {
            return Task.FromResult<LoginStep>(
                new LoginStep.Failed(LoginFailureReason.Unknown, "QR-вход не принимает ответов пользователя"));
        }

        _polls++;

        LoginStep step = _polls >= pollsBeforeSuccess
            ? CompletedWith("wa-79001234567@s.whatsapp.net")
            : new LoginStep.NeedsQrScan("wa-qr-payload", DateTimeOffset.UtcNow.AddMinutes(1));

        return Task.FromResult(step);
    }
}

/// <summary>MAX: токен бота, вход завершается сразу после его проверки.</summary>
public sealed class BotTokenConnector : FakeConnectorBase
{
    public override MessengerPlatform Platform => MessengerPlatform.Max;

    public override PlatformCapabilities Capabilities => new()
    {
        LoginMethod = LoginMethod.BotToken,
        // Постоянного соединения нет: события приходят входящим вебхуком.
        RequiresPersistentConnection = false,
        // Бот видит только то, что написано после его подключения.
        SupportsHistoryBackfill = false,
        SupportsEditing = true,
        EditWindow = null,
        SupportsDeleteForEveryone = true,
        DeleteForEveryoneWindow = null,
    };

    public override Task<LoginStep> BeginLoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        LoginStep step = request.BotToken == "valid-token"
            ? CompletedWith("max-bot-42")
            : new LoginStep.Failed(LoginFailureReason.InvalidBotToken);

        return Task.FromResult(step);
    }

    public override Task<LoginStep> ContinueLoginAsync(LoginAnswer answer, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoginStep>(new LoginStep.Failed(LoginFailureReason.Unknown, "вход уже завершён"));
}
