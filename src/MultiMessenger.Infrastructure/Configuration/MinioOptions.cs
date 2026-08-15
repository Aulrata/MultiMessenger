using System.ComponentModel.DataAnnotations;

namespace MultiMessenger.Infrastructure.Configuration;

/// <summary>
/// Доступ к S3-совместимому хранилищу (локально — MinIO из docker-compose).
/// Секция <c>Minio</c>. Ключи и endpoint зависят от окружения: локально задаются
/// через user-secrets, на сервере — через переменные окружения <c>Minio__*</c>.
/// </summary>
public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    /// <summary>Хост и порт S3 API: <c>localhost:9000</c> локально, <c>minio:9000</c> внутри docker-сети.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Endpoint { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AccessKey { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>Бакет для вложений: фото, голосовые, документы.</summary>
    [Required(AllowEmptyStrings = false)]
    public string BucketName { get; init; } = string.Empty;

    /// <summary>Локально MinIO поднят по http, на сервере ожидается https.</summary>
    public bool UseSsl { get; init; }
}
