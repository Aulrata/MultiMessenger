using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Entities;

/// <summary>
/// Вложение сообщения. Сам файл лежит в MinIO, в БД — только ссылка:
/// временные URL мессенджеров истекают, поэтому интерфейс отдаёт медиа
/// исключительно через собственный эндпоинт.
/// </summary>
public class MediaAttachment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MessageId { get; set; }

    public Message? Message { get; set; }

    public MediaType Type { get; set; }

    /// <summary>Ключ объекта в MinIO.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Оригинальное имя файла — актуально для документов.</summary>
    public string? FileName { get; set; }

    public string? MimeType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Длительность голосового или видео. Нужна для отрисовки дорожки в интерфейсе.</summary>
    public int? DurationSeconds { get; set; }
}
