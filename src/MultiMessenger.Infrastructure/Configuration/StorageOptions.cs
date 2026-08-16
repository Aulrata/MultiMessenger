using System.ComponentModel.DataAnnotations;

namespace MultiMessenger.Infrastructure.Configuration;

/// <summary>
/// Пути к данным на диске. Секция <c>Storage</c>.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Директория с файлами сессий Telegram/WhatsApp: <c>./sessions</c> локально,
    /// <c>/data/sessions</c> в контейнере (примонтированный volume).
    /// Относительный путь разрешается от корня контента приложения.
    /// Содержимое равносильно полному доступу к аккаунтам менеджеров — в git не попадает
    /// (см. .gitignore), на сервере права на директорию ограничиваются средствами ОС.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string SessionsBasePath { get; init; } = string.Empty;
}
