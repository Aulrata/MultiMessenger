namespace MultiMessenger.Web.Security;

public static class AuthorizationPolicies
{
    /// <summary>Рабочий кабинет: доступен и менеджеру, и администратору.</summary>
    public const string Workspace = "Workspace";

    /// <summary>Админка: только администратору.</summary>
    public const string Administration = "Administration";
}
