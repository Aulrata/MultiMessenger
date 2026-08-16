using System.Security.Claims;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;

namespace MultiMessenger.Infrastructure.Identity;

/// <summary>
/// Преобразование сотрудника в набор claims. Лежит в Infrastructure, а не в Web:
/// зависимостей от ASP.NET здесь нет — только <see cref="ClaimsPrincipal"/> из BCL,
/// зато логика вложенности ролей покрывается тестами без ссылки на веб-проект.
/// </summary>
public static class ManagerClaims
{
    public const string AuthenticationType = "MultiMessenger";

    /// <summary>
    /// Роли вложены: администратор получает и <see cref="ManagerRole.Admin"/>,
    /// и <see cref="ManagerRole.Manager"/>. Иначе каждую проверку рабочего кабинета
    /// пришлось бы писать как «Manager,Admin», и однажды кто-то забудет второе.
    /// </summary>
    public static ClaimsPrincipal CreatePrincipal(Manager manager)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, manager.Id.ToString()),
            new(ClaimTypes.Name, manager.FullName),
            new(ClaimTypes.MobilePhone, manager.PhoneNumber),
            new(ClaimTypes.Role, nameof(ManagerRole.Manager)),
        };

        if (manager.Role is ManagerRole.Admin)
        {
            claims.Add(new Claim(ClaimTypes.Role, nameof(ManagerRole.Admin)));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType));
    }

    public static Guid? GetManagerId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var managerId) ? managerId : null;
    }

    public static string? GetFullName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name);

    public static bool IsAdmin(this ClaimsPrincipal principal) =>
        principal.IsInRole(nameof(ManagerRole.Admin));
}
