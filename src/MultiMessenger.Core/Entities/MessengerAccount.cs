using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Entities;

/// <summary>
/// Канал менеджера на конкретной платформе. Для Telegram и WhatsApp за ним стоит
/// файл сессии и живое соединение, для MAX — токен бота и входящий вебхук.
/// </summary>
public class MessengerAccount
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ManagerId { get; set; }

    public Manager? Manager { get; set; }

    public MessengerPlatform Platform { get; set; }

    /// <summary>Для Telegram и WhatsApp — рабочий номер менеджера. Для MAX не заполняется.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Только для MAX: токен бота, полученный через @MasterBot.</summary>
    public string? BotToken { get; set; }

    /// <summary>
    /// Путь к файлу сессии относительно <c>Storage:SessionsBasePath</c>. Null для MAX,
    /// у которого нет постоянного соединения.
    /// </summary>
    public string? SessionPath { get; set; }

    public MessengerAccountStatus Status { get; set; } = MessengerAccountStatus.PendingAuth;

    /// <summary>Время последней активности — для дашборда состояния аккаунтов.</summary>
    public DateTimeOffset? LastActiveAt { get; set; }

    public DateTimeOffset? ConnectedAt { get; set; }

    public ICollection<Dialog> Dialogs { get; set; } = [];

    public ICollection<AccountHealthLog> HealthLogs { get; set; } = [];
}
