using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Entities;

/// <summary>
/// Сотрудник компании — пользователь системы. Самостоятельной регистрации нет,
/// учётные записи заводит администратор.
/// </summary>
public class Manager
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Логин. Совпадает с рабочим номером, к которому привязаны Telegram и WhatsApp.
    /// Хранится в нормализованном виде (только цифры с ведущим плюсом), иначе
    /// «+7 900 123-45-67» и «79001234567» станут разными пользователями.
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public ManagerRole Role { get; set; } = ManagerRole.Manager;

    /// <summary>Деактивация вместо удаления — история переписки должна оставаться целой.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<MessengerAccount> MessengerAccounts { get; set; } = [];
}
