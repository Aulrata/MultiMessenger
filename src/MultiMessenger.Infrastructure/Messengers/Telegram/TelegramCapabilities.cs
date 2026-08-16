using MultiMessenger.Core.Messaging;

namespace MultiMessenger.Infrastructure.Messengers.Telegram;

public static class TelegramCapabilities
{
    /// <summary>
    /// Возможности Telegram. Значения окон — факты о платформе, а не наши решения,
    /// и меняться они могут без предупреждения. Здесь стоят консервативные оценки:
    /// лучше не показать кнопку, чем показать и получить отказ платформы.
    /// </summary>
    public static readonly PlatformCapabilities Instance = new()
    {
        LoginMethod = LoginMethod.PhoneAndCode,

        RequiresPersistentConnection = true,

        // Полная история переписки доступна к выгрузке.
        SupportsHistoryBackfill = true,
        HistoryWindow = null,

        SupportsEditing = true,
        EditWindow = TimeSpan.FromHours(48),

        // DeleteMessages в WTelegramClient удаляет у всех участников.
        SupportsDeleteForEveryone = true,
        DeleteForEveryoneWindow = null,
    };
}
