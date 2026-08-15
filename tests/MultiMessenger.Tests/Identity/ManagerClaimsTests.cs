using System.Security.Claims;
using FluentAssertions;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Infrastructure.Identity;

namespace MultiMessenger.Tests.Identity;

public class ManagerClaimsTests
{
    /// <summary>
    /// Роли вложены: администратор обязан проходить проверку рабочего кабинета,
    /// иначе он не сможет вести собственную переписку.
    /// </summary>
    [Fact]
    public void AdminGetsBothRoles()
    {
        var principal = ManagerClaims.CreatePrincipal(NewManager(ManagerRole.Admin));

        principal.IsInRole(nameof(ManagerRole.Manager)).Should().BeTrue();
        principal.IsInRole(nameof(ManagerRole.Admin)).Should().BeTrue();
        principal.IsAdmin().Should().BeTrue();
    }

    [Fact]
    public void ManagerDoesNotGetAdminRole()
    {
        var principal = ManagerClaims.CreatePrincipal(NewManager(ManagerRole.Manager));

        principal.IsInRole(nameof(ManagerRole.Manager)).Should().BeTrue();
        principal.IsInRole(nameof(ManagerRole.Admin)).Should().BeFalse();
        principal.IsAdmin().Should().BeFalse();
    }

    [Fact]
    public void IdentityCarriesManagerIdAndName()
    {
        var manager = NewManager(ManagerRole.Manager);

        var principal = ManagerClaims.CreatePrincipal(manager);

        principal.GetManagerId().Should().Be(manager.Id);
        principal.GetFullName().Should().Be(manager.FullName);
        principal.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void AnonymousPrincipalHasNoManagerId()
    {
        new ClaimsPrincipal(new ClaimsIdentity()).GetManagerId().Should().BeNull();
    }

    private static Manager NewManager(ManagerRole role) => new()
    {
        FullName = "Иванова Мария Сергеевна",
        PhoneNumber = "+79004445566",
        PasswordHash = "hash",
        Role = role,
    };
}
