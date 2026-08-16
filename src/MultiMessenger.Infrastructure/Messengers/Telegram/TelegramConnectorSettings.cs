namespace MultiMessenger.Infrastructure.Messengers.Telegram;

/// <summary>
/// Готовые к употреблению настройки одного подключения: собираются из
/// <c>TelegramOptions</c> и <c>StorageOptions</c>, чтобы коннектор не разбирался
/// с конфигурацией сам.
/// </summary>
public sealed record TelegramConnectorSettings
{
    public required int ApiId { get; init; }

    public required string ApiHash { get; init; }

    public required string SessionsBasePath { get; init; }

    /// <summary>Пусто — подключаться напрямую, без прокси.</summary>
    public string? MTProxyUrl { get; init; }
}
