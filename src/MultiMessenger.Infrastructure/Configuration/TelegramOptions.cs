using System.ComponentModel.DataAnnotations;

namespace MultiMessenger.Infrastructure.Configuration;

/// <summary>
/// Параметры MTProto-клиента. Секция <c>Telegram</c>.
/// <para>
/// <c>ApiId</c>/<c>ApiHash</c> — одни на весь сервис: изоляция аккаунтов обеспечивается
/// разными файлами сессий, а не разными приложениями Telegram.
/// </para>
/// <para>
/// В отличие от <see cref="MinioOptions"/> и <see cref="StorageOptions"/> эти настройки
/// не проверяются при старте приложения: на этапе 1 Telegram ещё не подключён, и
/// требовать ключи от каждого запуска бессмысленно. Валидация сработает при первом
/// обращении к <c>IOptions&lt;TelegramOptions&gt;</c> — то есть когда коннектор
/// действительно создаётся.
/// </para>
/// </summary>
public sealed class TelegramOptions : IValidatableObject
{
    public const string SectionName = "Telegram";

    [Range(1, int.MaxValue)]
    public int ApiId { get; init; }

    [Required(AllowEmptyStrings = false)]
    public string ApiHash { get; init; } = string.Empty;

    /// <summary>
    /// Ходить ли в Telegram через MTProxy. С сервера в Германии прокси не нужен —
    /// это флаг для запусков из России (локальная разработка, резервный хостинг).
    /// Отдельный флаг, а не «пустой <see cref="MTProxyUrl"/> = выключено», чтобы
    /// адрес прокси можно было держать в конфигурации и включать одним переключателем.
    /// </summary>
    public bool UseProxy { get; init; }

    /// <summary>
    /// Ссылка вида <c>https://t.me/proxy?server=...&amp;port=...&amp;secret=...</c>.
    /// Обязательна, только когда <see cref="UseProxy"/> включён.
    /// </summary>
    public string? MTProxyUrl { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (UseProxy && string.IsNullOrWhiteSpace(MTProxyUrl))
        {
            yield return new ValidationResult(
                $"{nameof(UseProxy)} включён, но {nameof(MTProxyUrl)} не задан.",
                [nameof(MTProxyUrl)]);
        }
    }
}
