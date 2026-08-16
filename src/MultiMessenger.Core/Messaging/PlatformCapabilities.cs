namespace MultiMessenger.Core.Messaging;

/// <summary>Как выглядит подключение аккаунта с точки зрения пользователя.</summary>
public enum LoginMethod
{
    /// <summary>Номер телефона, затем код подтверждения и, возможно, пароль 2FA. Telegram.</summary>
    PhoneAndCode,

    /// <summary>QR-код, который сканируют телефоном. WhatsApp.</summary>
    QrCode,

    /// <summary>Готовый токен бота, полученный на стороне платформы. MAX.</summary>
    BotToken,
}

/// <summary>
/// Что платформа умеет и в каких пределах. Заполняется каждым коннектором за себя.
/// <para>
/// Нужно, чтобы интерфейс не превращался в набор проверок вида
/// «если Telegram, то показать кнопку правки». Вместо этого он спрашивает
/// у коннектора, что доступно, и одинаково работает со всеми платформами.
/// </para>
/// <para>
/// Конкретные значения окон живут в реализациях, а не здесь: это факты о платформах,
/// они меняются, и Core не должен о них знать.
/// </para>
/// </summary>
public sealed record PlatformCapabilities
{
    public required LoginMethod LoginMethod { get; init; }

    /// <summary>
    /// Держится ли постоянное соединение. У Telegram и WhatsApp — да, у MAX нет:
    /// он работает ботом через входящий вебхук.
    /// </summary>
    public required bool RequiresPersistentConnection { get; init; }

    /// <summary>Можно ли загрузить переписку, которая была до подключения.</summary>
    public required bool SupportsHistoryBackfill { get; init; }

    /// <summary>
    /// Насколько глубоко доступна история. <c>null</c> при
    /// <see cref="SupportsHistoryBackfill"/> означает «вся доступная».
    /// У WhatsApp окно ограничено протоколом, у MAX истории нет вовсе.
    /// </summary>
    public TimeSpan? HistoryWindow { get; init; }

    public required bool SupportsEditing { get; init; }

    /// <summary>Сколько времени после отправки сообщение ещё можно исправить.</summary>
    public TimeSpan? EditWindow { get; init; }

    /// <summary>Умеет ли платформа удалять сообщение у собеседника, а не только у себя.</summary>
    public required bool SupportsDeleteForEveryone { get; init; }

    public TimeSpan? DeleteForEveryoneWindow { get; init; }

    /// <summary>Истекло ли окно, в течение которого сообщение можно править.</summary>
    public bool CanEdit(DateTimeOffset sentAt, DateTimeOffset now) =>
        SupportsEditing && IsWithin(EditWindow, sentAt, now);

    /// <summary>Истекло ли окно, в течение которого сообщение можно удалить у всех.</summary>
    public bool CanDeleteForEveryone(DateTimeOffset sentAt, DateTimeOffset now) =>
        SupportsDeleteForEveryone && IsWithin(DeleteForEveryoneWindow, sentAt, now);

    private static bool IsWithin(TimeSpan? window, DateTimeOffset sentAt, DateTimeOffset now) =>
        window is null || now - sentAt <= window.Value;
}
