using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Enums;
using MultiMessenger.Infrastructure.Auditing;
using MultiMessenger.Infrastructure.Identity;
using MultiMessenger.Tests.Persistence;

namespace MultiMessenger.Tests.Identity;

[Collection(PostgresCollection.Name)]
public class ManagerDirectoryTests(PostgresFixture postgres)
{
    private const string ValidPassword = "Manager-Password-2026";
    private const string TestIpAddress = "203.0.113.9";

    private readonly IdentityPasswordHasher _hasher = new();
    private readonly Guid _adminId = Guid.CreateVersion7();

    [Fact]
    public async Task ManagerIsCreatedWithNormalizedPhoneAndHashedPassword()
    {
        var phoneNumber = TestData.NewPhoneNumber();
        var nationalNotation = "8" + phoneNumber[2..];

        var result = await CreateAsync("Иванова Мария Сергеевна", nationalNotation, ValidPassword);

        result.Succeeded.Should().BeTrue();
        result.Manager!.PhoneNumber.Should().Be(phoneNumber, "номер сохраняется в едином формате");
        result.Manager.PasswordHash.Should().NotContain(ValidPassword);
        result.Manager.IsActive.Should().BeTrue();

        _hasher.Verify(result.Manager.PasswordHash, ValidPassword)
            .Should().Be(Core.Identity.PasswordVerificationResult.Success);
    }

    [Fact]
    public async Task CreationIsRecordedInAudit()
    {
        var result = await CreateAsync("Петров Пётр", TestData.NewPhoneNumber(), ValidPassword);

        await using var dbContext = postgres.CreateDbContext();
        var audit = await dbContext.AuditEntries
            .SingleAsync(entry => entry.EntityId == result.Manager!.Id && entry.Action == AuditAction.ManagerCreated);

        audit.ManagerId.Should().Be(_adminId, "в журнале должен быть тот, кто завёл учётную запись");
        audit.Subject.Should().Be(result.Manager!.PhoneNumber);
        audit.EntityType.Should().Be("Manager");
        audit.IpAddress.Should().Be(TestIpAddress);
        audit.DetailsJson.Should().Contain("Петров Пётр");
    }

    /// <summary>
    /// Занятость номера проверяется после нормализации: «8 900…» и «+7 900…» —
    /// один и тот же сотрудник, а не два.
    /// </summary>
    [Fact]
    public async Task SameNumberInDifferentNotationIsRejectedAsDuplicate()
    {
        var phoneNumber = TestData.NewPhoneNumber();
        await CreateAsync("Первый", phoneNumber, ValidPassword);

        var duplicate = await CreateAsync("Второй", "8" + phoneNumber[2..], ValidPassword);

        duplicate.Succeeded.Should().BeFalse();
        duplicate.Failure.Should().Be(ManagerCreationFailure.PhoneNumberAlreadyUsed);
    }

    [Theory]
    [InlineData("", "Корректное имя обязательно", ManagerCreationFailure.FullNameRequired)]
    [InlineData("   ", "Пробелы именем не считаются", ManagerCreationFailure.FullNameRequired)]
    public async Task EmptyFullNameIsRejected(string fullName, string _, ManagerCreationFailure expected)
    {
        var result = await CreateAsync(fullName, TestData.NewPhoneNumber(), ValidPassword);

        result.Failure.Should().Be(expected);
    }

    [Fact]
    public async Task MalformedPhoneNumberIsRejected()
    {
        var result = await CreateAsync("Иванов", "12-34", ValidPassword);

        result.Failure.Should().Be(ManagerCreationFailure.InvalidPhoneNumberFormat);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task ShortPasswordIsRejected(string password)
    {
        var result = await CreateAsync("Иванов", TestData.NewPhoneNumber(), password);

        result.Failure.Should().Be(ManagerCreationFailure.PasswordTooShort);
    }

    [Fact]
    public async Task FailedCreationLeavesNothingBehind()
    {
        var phoneNumber = TestData.NewPhoneNumber();

        await CreateAsync("Иванов", phoneNumber, "short");

        await using var dbContext = postgres.CreateDbContext();
        (await dbContext.Managers.AnyAsync(manager => manager.PhoneNumber == phoneNumber))
            .Should().BeFalse();
    }

    [Fact]
    public async Task DeactivationKeepsTheRecordAndIsAudited()
    {
        var created = await CreateAsync("Сидоров", TestData.NewPhoneNumber(), ValidPassword);
        var managerId = created.Manager!.Id;

        var changed = await SetActiveAsync(managerId, isActive: false);

        changed.Should().BeTrue();

        await using var dbContext = postgres.CreateDbContext();

        var stored = await dbContext.Managers.SingleAsync(manager => manager.Id == managerId);
        stored.IsActive.Should().BeFalse("сотрудник отключается, а не удаляется");
        stored.FullName.Should().Be("Сидоров");

        var audit = await dbContext.AuditEntries
            .SingleAsync(entry => entry.EntityId == managerId && entry.Action == AuditAction.ManagerDeactivated);
        audit.DetailsJson.Should().Contain("false");
    }

    [Fact]
    public async Task DeactivatedManagerCanBeTurnedBackOn()
    {
        var created = await CreateAsync("Сидоров", TestData.NewPhoneNumber(), ValidPassword);
        var managerId = created.Manager!.Id;

        await SetActiveAsync(managerId, isActive: false);
        await SetActiveAsync(managerId, isActive: true);

        await using var dbContext = postgres.CreateDbContext();
        (await dbContext.Managers.SingleAsync(manager => manager.Id == managerId))
            .IsActive.Should().BeTrue();
    }

    /// <summary>Повторное отключение уже отключённого не должно плодить записи в журнале.</summary>
    [Fact]
    public async Task RepeatedDeactivationChangesNothing()
    {
        var created = await CreateAsync("Сидоров", TestData.NewPhoneNumber(), ValidPassword);
        var managerId = created.Manager!.Id;

        await SetActiveAsync(managerId, isActive: false);
        var second = await SetActiveAsync(managerId, isActive: false);

        second.Should().BeFalse();

        await using var dbContext = postgres.CreateDbContext();
        (await dbContext.AuditEntries.CountAsync(entry =>
                entry.EntityId == managerId && entry.Action == AuditAction.ManagerDeactivated))
            .Should().Be(1);
    }

    [Fact]
    public async Task UnknownManagerCannotBeToggled()
    {
        (await SetActiveAsync(Guid.CreateVersion7(), isActive: false)).Should().BeFalse();
    }

    [Fact]
    public async Task ActiveManagersAreListedBeforeDisabledOnes()
    {
        var disabled = await CreateAsync("Яковлев", TestData.NewPhoneNumber(), ValidPassword);
        await SetActiveAsync(disabled.Manager!.Id, isActive: false);
        await CreateAsync("Абрамов", TestData.NewPhoneNumber(), ValidPassword);

        await using var dbContext = postgres.CreateDbContext();
        var directory = new ManagerDirectory(dbContext, _hasher, new EfAuditTrail(dbContext));

        var all = await directory.ListAsync();

        var lastActiveIndex = all.FindLastIndex(manager => manager.IsActive);
        var firstDisabledIndex = all.FindIndex(manager => !manager.IsActive);

        firstDisabledIndex.Should().BeGreaterThan(lastActiveIndex, "отключённые уходят в конец списка");
    }

    private async Task<ManagerCreationResult> CreateAsync(string fullName, string phoneNumber, string password)
    {
        await using var dbContext = postgres.CreateDbContext();
        var directory = new ManagerDirectory(dbContext, _hasher, new EfAuditTrail(dbContext));

        return await directory.CreateAsync(
            fullName, phoneNumber, password, ManagerRole.Manager, _adminId, TestIpAddress);
    }

    private async Task<bool> SetActiveAsync(Guid managerId, bool isActive)
    {
        await using var dbContext = postgres.CreateDbContext();
        var directory = new ManagerDirectory(dbContext, _hasher, new EfAuditTrail(dbContext));

        return await directory.SetActiveAsync(managerId, isActive, _adminId, TestIpAddress);
    }
}
