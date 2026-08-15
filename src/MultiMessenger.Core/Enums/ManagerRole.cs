namespace MultiMessenger.Core.Enums;

public enum ManagerRole
{
    /// <summary>Обычный сотрудник: видит только свои диалоги и свои каналы.</summary>
    Manager,

    /// <summary>Доступ к админке: создание менеджеров, дашборд состояния аккаунтов.</summary>
    Admin,
}
