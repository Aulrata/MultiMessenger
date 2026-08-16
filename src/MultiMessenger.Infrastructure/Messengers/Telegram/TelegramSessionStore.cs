namespace MultiMessenger.Infrastructure.Messengers.Telegram;

/// <summary>
/// Расположение файлов сессий.
/// <para>
/// Файл сессии равносилен полному доступу к аккаунту менеджера, поэтому имя
/// собирается из идентификатора канала, а не из номера телефона: по содержимому
/// каталога не должно быть видно, чьи это номера.
/// </para>
/// </summary>
public static class TelegramSessionStore
{
    public static string GetSessionPath(string basePath, Guid messengerAccountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        var directory = Path.GetFullPath(basePath);
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, $"{messengerAccountId:N}.session");
    }

    /// <summary>
    /// Удаляет сессию — при отключении канала или после настоящего выхода из аккаунта.
    /// Оставлять её нельзя: это действующий ключ доступа.
    /// </summary>
    public static void DeleteSession(string basePath, Guid messengerAccountId)
    {
        var path = GetSessionPath(basePath, messengerAccountId);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
