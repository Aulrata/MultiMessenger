namespace MultiMessenger.Core.Messaging;

/// <summary>
/// Начало подключения аккаунта. Набор заполненных полей зависит от платформы:
/// Telegram и WhatsApp работают с номером, MAX — с токеном бота.
/// </summary>
public sealed record LoginRequest
{
    /// <summary>Аккаунт, к которому относится попытка входа.</summary>
    public required Guid MessengerAccountId { get; init; }

    public string? PhoneNumber { get; init; }

    public string? BotToken { get; init; }
}

/// <summary>
/// Что система должна сделать дальше, чтобы довести вход до конца.
/// <para>
/// Вход описан как состояние, а не как жёсткая последовательность
/// «номер → код → пароль»: у WhatsApp это QR-код, у MAX вход завершается сразу
/// после проверки токена. Интерфейс реагирует на состояние и одинаково
/// обслуживает все три платформы.
/// </para>
/// </summary>
public abstract record LoginStep
{
    /// <summary>Ждём код подтверждения, который платформа прислала пользователю.</summary>
    public sealed record NeedsVerificationCode(string? SentTo) : LoginStep;

    /// <summary>Включена двухфакторная защита, нужен пароль.</summary>
    public sealed record NeedsTwoFactorPassword(string? Hint) : LoginStep;

    /// <summary>
    /// Нужно показать QR-код и дождаться сканирования. Содержимое отдаётся строкой,
    /// картинку рисует интерфейс: отдавать из коннектора готовое изображение
    /// значило бы тащить в него знание о представлении.
    /// </summary>
    public sealed record NeedsQrScan(string Payload, DateTimeOffset ExpiresAt) : LoginStep;

    public sealed record Completed(PlatformAccountInfo Account) : LoginStep;

    public sealed record Failed(LoginFailureReason Reason, string? Details = null) : LoginStep;
}

/// <summary>Ответ пользователя на очередной шаг входа.</summary>
public abstract record LoginAnswer
{
    public sealed record VerificationCode(string Code) : LoginAnswer;

    public sealed record TwoFactorPassword(string Password) : LoginAnswer;

    /// <summary>
    /// Ответа как такового нет — интерфейс просто спрашивает, не изменилось ли состояние.
    /// Так работает ожидание сканирования QR-кода.
    /// </summary>
    public sealed record CheckStatus : LoginAnswer;
}

public enum LoginFailureReason
{
    InvalidPhoneNumber,
    InvalidCode,
    InvalidPassword,
    InvalidBotToken,

    /// <summary>Пользователь не успел: код или QR-код истёк.</summary>
    Expired,

    /// <summary>Платформа временно отказывает — слишком частые попытки.</summary>
    RateLimited,

    /// <summary>Аккаунт заблокирован платформой.</summary>
    AccountBanned,

    NetworkError,

    Unknown,
}

/// <summary>Что известно об аккаунте после успешного подключения.</summary>
public sealed record PlatformAccountInfo
{
    public required string PlatformUserId { get; init; }

    public string? DisplayName { get; init; }

    public string? PhoneNumber { get; init; }
}
