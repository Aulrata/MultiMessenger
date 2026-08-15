namespace MultiMessenger.Core.Storage;

/// <summary>
/// Построение ключей объектов в хранилище. Вынесено в одно место, чтобы раскладка
/// не расползлась: по ней потом делать выгрузки и разбираться, что занимает место.
/// </summary>
public static class MediaStorageKey
{
    /// <summary>
    /// Формат: <c>media/2026/08/{id}.jpg</c>.
    /// <para>
    /// Разбивка по годам и месяцам нужна не хранилищу, а людям — просматривать
    /// и выгружать плоскую папку на сотни тысяч объектов неудобно. Идентификатор
    /// вложения — Guid v7, поэтому имена внутри месяца идут в порядке появления.
    /// </para>
    /// </summary>
    public static string Create(Guid attachmentId, DateTimeOffset createdAt, string? originalFileName = null)
    {
        var extension = GetSafeExtension(originalFileName);

        return $"media/{createdAt:yyyy}/{createdAt:MM}/{attachmentId}{extension}";
    }

    /// <summary>
    /// Расширение берётся только ради удобства чтения ключей, поэтому всё подозрительное
    /// отбрасывается: имя файла приходит от клиента, и складывать его в ключ как есть нельзя.
    /// </summary>
    private static string GetSafeExtension(string? originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(originalFileName);

        if (extension.Length is < 2 or > 10)
        {
            return string.Empty;
        }

        foreach (var character in extension.AsSpan(1))
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return string.Empty;
            }
        }

        return extension.ToLowerInvariant();
    }
}
