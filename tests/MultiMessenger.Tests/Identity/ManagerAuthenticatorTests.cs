using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Entities;
using MultiMessenger.Infrastructure.Auditing;
using MultiMessenger.Infrastructure.Identity;
using MultiMessenger.Infrastructure.Persistence;
using MultiMessenger.Tests.Persistence;
// Имя SignInResult занято и в ASP.NET Core Identity, откуда берётся PasswordHasher
// для проверки пересчёта устаревшего хеша.
using SignInResult = MultiMessenger.Infrastructure.Identity.SignInResult;

namespace MultiMessenger.Tests.Identity;

[Collection(PostgresCollection.Name)]
public class ManagerAuthenticatorTests(PostgresFixture postgres)
{
    private const string CorrectPassword = "Correct-Password-2026";
    private const string TestIpAddress = "203.0.113.7";

    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public async Task CorrectCredentialsLetTheManagerIn()
    {
        var manager = await CreateManagerAsync();

        var result = await AuthenticateAsync(manager.PhoneNumber, CorrectPassword);

        result.Succeeded.Should().BeTrue();
        result.Manager!.Id.Should().Be(manager.Id);

        (await LastAuditActionAsync(manager.Id)).Should().Be(AuditAction.ManagerSignedIn);
    }

    /// <summary>
    /// Номер вводят как придётся, а в базе он лежит в одном формате.
    /// Без нормализации сотрудник не смог бы войти, набрав привычную восьмёрку.
    /// </summary>
    [Theory]
    [InlineData("8{0}")]
    [InlineData("+7{0}")]
    [InlineData("7{0}")]
    public async Task PhoneNumberIsAcceptedInAnyNotation(string template)
    {
        var manager = await CreateManagerAsync();
        var nationalPart = manager.PhoneNumber[2..];

        var result = await AuthenticateAsync(string.Format(template, nationalPart), CorrectPassword);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task WrongPasswordIsRejectedAndLogged()
    {
        var manager = await CreateManagerAsync();

        var result = await AuthenticateAsync(manager.PhoneNumber, "Wrong-Password-2026");

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(SignInFailure.UnknownManagerOrWrongPassword);

        var audit = await LastAuditEntryAsync(manager.PhoneNumber);
        audit.Action.Should().Be(AuditAction.ManagerSignInFailed);
        audit.DetailsJson.Should().Contain("wrong-password");
        audit.IpAddress.Should().Be(TestIpAddress);
    }

    /// <summary>
    /// Несуществующий номер и неверный пароль обязаны быть неразличимы снаружи,
    /// иначе форма входа превращается в способ узнать, кто работает в компании.
    /// В журнале при этом причины разные.
    /// </summary>
    [Fact]
    public async Task UnknownNumberLooksExactlyLikeWrongPassword()
    {
        var manager = await CreateManagerAsync();
        var strangerNumber = TestData.NewPhoneNumber();

        var wrongPassword = await AuthenticateAsync(manager.PhoneNumber, "Wrong-Password-2026");
        var unknownNumber = await AuthenticateAsync(strangerNumber, CorrectPassword);

        unknownNumber.Failure.Should().Be(wrongPassword.Failure);

        (await LastAuditEntryAsync(strangerNumber)).DetailsJson.Should().Contain("unknown-manager");
    }

    [Fact]
    public async Task MalformedPhoneNumberIsRejectedBeforeTouchingTheDatabase()
    {
        var result = await AuthenticateAsync("не телефон", CorrectPassword);

        result.Failure.Should().Be(SignInFailure.InvalidPhoneNumberFormat);

        (await LastAuditEntryAsync("не телефон")).DetailsJson.Should().Contain("invalid-phone-format");
    }

    [Fact]
    public async Task DeactivatedManagerCannotSignIn()
    {
        var manager = await CreateManagerAsync(isActive: false);

        var result = await AuthenticateAsync(manager.PhoneNumber, CorrectPassword);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(SignInFailure.Deactivated);

        (await LastAuditEntryAsync(manager.PhoneNumber)).DetailsJson.Should().Contain("deactivated");
    }

    /// <summary>
    /// Отключённому сотруднику сообщают об отключении только после проверки пароля.
    /// Иначе перебор номеров показывал бы, какие из них заведены в системе.
    /// </summary>
    [Fact]
    public async Task DeactivatedManagerWithWrongPasswordGetsTheGenericAnswer()
    {
        var manager = await CreateManagerAsync(isActive: false);

        var result = await AuthenticateAsync(manager.PhoneNumber, "Wrong-Password-2026");

        result.Failure.Should().Be(SignInFailure.UnknownManagerOrWrongPassword);
    }

    /// <summary>
    /// Успешный вход — единственный момент, когда известен открытый пароль,
    /// значит и единственная возможность пересчитать устаревший хеш.
    /// </summary>
    [Fact]
    public async Task OutdatedHashIsUpgradedOnSuccessfulSignIn()
    {
        var legacyHasher = new PasswordHasher<Manager>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2,
        }));

        var manager = TestData.NewManager(legacyHasher.HashPassword(new Manager(), CorrectPassword));
        var legacyHash = manager.PasswordHash;

        await using (var dbContext = postgres.CreateDbContext())
        {
            dbContext.Managers.Add(manager);
            await dbContext.SaveChangesAsync();
        }

        var result = await AuthenticateAsync(manager.PhoneNumber, CorrectPassword);
        result.Succeeded.Should().BeTrue();

        await using (var dbContext = postgres.CreateDbContext())
        {
            var stored = await dbContext.Managers.SingleAsync(candidate => candidate.Id == manager.Id);

            stored.PasswordHash.Should().NotBe(legacyHash, "хеш должен быть пересчитан новым алгоритмом");
            _hasher.Verify(stored.PasswordHash, CorrectPassword)
                .Should().Be(Core.Identity.PasswordVerificationResult.Success);
        }
    }

    private async Task<Manager> CreateManagerAsync(bool isActive = true)
    {
        var manager = TestData.NewManager(_hasher.Hash(CorrectPassword), isActive: isActive);

        await using var dbContext = postgres.CreateDbContext();
        dbContext.Managers.Add(manager);
        await dbContext.SaveChangesAsync();

        return manager;
    }

    private async Task<SignInResult> AuthenticateAsync(string? phoneNumber, string password)
    {
        await using var dbContext = postgres.CreateDbContext();
        var authenticator = new ManagerAuthenticator(dbContext, _hasher, new EfAuditTrail(dbContext));

        return await authenticator.AuthenticateAsync(phoneNumber, password, TestIpAddress);
    }

    private async Task<AuditAction> LastAuditActionAsync(Guid managerId)
    {
        await using var dbContext = postgres.CreateDbContext();

        return await dbContext.AuditEntries
            .Where(entry => entry.ManagerId == managerId)
            .OrderByDescending(entry => entry.OccurredAt)
            .Select(entry => entry.Action)
            .FirstAsync();
    }

    private async Task<AuditEntry> LastAuditEntryAsync(string subject)
    {
        await using var dbContext = postgres.CreateDbContext();

        return await dbContext.AuditEntries
            .Where(entry => entry.Subject == subject)
            .OrderByDescending(entry => entry.OccurredAt)
            .FirstAsync();
    }
}
