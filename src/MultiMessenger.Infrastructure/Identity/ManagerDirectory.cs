using Microsoft.EntityFrameworkCore;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Identity;
using MultiMessenger.Infrastructure.Persistence;

namespace MultiMessenger.Infrastructure.Identity;

public enum ManagerCreationFailure
{
    None,
    InvalidPhoneNumberFormat,
    PhoneNumberAlreadyUsed,
    FullNameRequired,
    PasswordTooShort,
}

public sealed record ManagerCreationResult(Manager? Manager, ManagerCreationFailure Failure)
{
    public bool Succeeded => Failure is ManagerCreationFailure.None && Manager is not null;
}

/// <summary>Заведение и деактивация сотрудников. Доступно только администратору.</summary>
public class ManagerDirectory(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    IAuditTrail auditTrail)
{
    public const int MinimumPasswordLength = 8;

    public Task<List<Manager>> ListAsync(CancellationToken cancellationToken = default) =>
        dbContext.Managers
            .OrderByDescending(manager => manager.IsActive)
            .ThenBy(manager => manager.FullName)
            .ToListAsync(cancellationToken);

    public async Task<ManagerCreationResult> CreateAsync(
        string? fullName,
        string? phoneNumber,
        string? password,
        ManagerRole role,
        Guid actorManagerId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return new ManagerCreationResult(null, ManagerCreationFailure.FullNameRequired);
        }

        if (!PhoneNumber.TryNormalize(phoneNumber, out var normalizedPhone))
        {
            return new ManagerCreationResult(null, ManagerCreationFailure.InvalidPhoneNumberFormat);
        }

        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
        {
            return new ManagerCreationResult(null, ManagerCreationFailure.PasswordTooShort);
        }

        var phoneTaken = await dbContext.Managers
            .AnyAsync(manager => manager.PhoneNumber == normalizedPhone, cancellationToken);

        if (phoneTaken)
        {
            return new ManagerCreationResult(null, ManagerCreationFailure.PhoneNumberAlreadyUsed);
        }

        var manager = new Manager
        {
            FullName = fullName.Trim(),
            PhoneNumber = normalizedPhone,
            PasswordHash = passwordHasher.Hash(password),
            Role = role,
        };

        dbContext.Managers.Add(manager);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry
            {
                ManagerId = actorManagerId,
                Action = AuditAction.ManagerCreated,
                Subject = manager.PhoneNumber,
                EntityType = nameof(Manager),
                EntityId = manager.Id,
                DetailsJson = $$"""{"role":"{{role}}","fullName":{{System.Text.Json.JsonSerializer.Serialize(manager.FullName)}}}""",
                IpAddress = ipAddress,
            },
            cancellationToken);

        return new ManagerCreationResult(manager, ManagerCreationFailure.None);
    }

    /// <summary>
    /// Деактивация вместо удаления: история переписки и записи аудита должны
    /// пережить увольнение сотрудника.
    /// </summary>
    public async Task<bool> SetActiveAsync(
        Guid managerId,
        bool isActive,
        Guid actorManagerId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var manager = await dbContext.Managers
            .SingleOrDefaultAsync(candidate => candidate.Id == managerId, cancellationToken);

        if (manager is null || manager.IsActive == isActive)
        {
            return false;
        }

        manager.IsActive = isActive;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry
            {
                ManagerId = actorManagerId,
                Action = AuditAction.ManagerDeactivated,
                Subject = manager.PhoneNumber,
                EntityType = nameof(Manager),
                EntityId = manager.Id,
                DetailsJson = $$"""{"isActive":{{(isActive ? "true" : "false")}}}""",
                IpAddress = ipAddress,
            },
            cancellationToken);

        return true;
    }

    public Task<Manager?> FindByIdAsync(Guid managerId, CancellationToken cancellationToken = default) =>
        dbContext.Managers.SingleOrDefaultAsync(manager => manager.Id == managerId, cancellationToken);
}
