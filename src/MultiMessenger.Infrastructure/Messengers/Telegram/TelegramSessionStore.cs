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

        var path = Path.GetFullPath(Path.Combine(directory, $"{messengerAccountId:N}.session"));

        // Имя собрано из Guid и вырваться из каталога не может. Проверка стоит здесь
        // не поэтому: файл сессии равносилен полному доступу к аккаунту менеджера,
        // и если однажды имя начнут собирать из чего-то другого, ошибка вскроется
        // сразу, а не когда сессии окажутся не там, где их ждут.
        if (Path.GetDirectoryName(path) != Path.TrimEndingDirectorySeparator(directory))
        {
            throw new InvalidOperationException($"Путь к файлу сессии вышел за пределы каталога {directory}");
        }

        return path;
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
