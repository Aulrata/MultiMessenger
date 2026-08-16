namespace MultiMessenger.Core.Storage;

/// <summary>
/// Хранилище бинарных файлов: фото, голосовые, документы.
/// <para>
/// Абстракция существует ради переезда: сегодня за ней self-hosted MinIO, завтра
/// может оказаться внешний S3. Бизнес-логика об этом знать не должна.
/// </para>
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(Stream content, string key, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает содержимое или <c>null</c>, если объекта нет. Результат обязателен
    /// к освобождению: под ним живёт открытое сетевое соединение.
    /// </summary>
    Task<StoredFile?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Временная прямая ссылка на объект в хранилище.
    /// <para>
    /// В интерфейсе переписки не используется: медиа отдаётся через собственный
    /// эндпоинт <c>/media/{id}</c>, где проверяются права доступа. Ссылка нужна для
    /// служебных задач — выгрузок, отладки, будущей интеграции с внешними системами.
    /// </para>
    /// </summary>
    Task<Uri> GetUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record StoredFile(Stream Content, string ContentType, long SizeBytes) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await Content.DisposeAsync();
}
