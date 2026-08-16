using Microsoft.EntityFrameworkCore;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Identity;
using MultiMessenger.Infrastructure.Persistence;

namespace MultiMessenger.Infrastructure.Identity;

public class ManagerAuthenticator(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    IAuditTrail auditTrail)
{
    public async Task<SignInResult> AuthenticateAsync(
        string? phoneNumber,
        string? password,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (!PhoneNumber.TryNormalize(phoneNumber, out var normalizedPhone))
        {
            await RecordFailureAsync(phoneNumber, "invalid-phone-format", ipAddress, cancellationToken);
            return SignInResult.Failed(SignInFailure.InvalidPhoneNumberFormat);
        }

        var manager = await dbContext.Managers
            .SingleOrDefaultAsync(candidate => candidate.PhoneNumber == normalizedPhone, cancellationToken);

        if (manager is null)
        {
            await RecordFailureAsync(normalizedPhone, "unknown-manager", ipAddress, cancellationToken);
            return SignInResult.Failed(SignInFailure.UnknownManagerOrWrongPassword);
        }

        var verification = passwordHasher.Verify(manager.PasswordHash, password ?? string.Empty);

        if (verification is PasswordVerificationResult.Failed)
        {
            await RecordFailureAsync(normalizedPhone, "wrong-password", ipAddress, cancellationToken);
            return SignInResult.Failed(SignInFailure.UnknownManagerOrWrongPassword);
        }

        // Проверка активности идёт после пароля: иначе форма входа подсказывала бы,
        // что такой сотрудник существует, любому, кто переберёт номера.
        if (!manager.IsActive)
        {
            await RecordFailureAsync(normalizedPhone, "deactivated", ipAddress, cancellationToken);
            return SignInResult.Failed(SignInFailure.Deactivated);
        }

        if (verification is PasswordVerificationResult.SuccessRehashNeeded)
        {
            manager.PasswordHash = passwordHasher.Hash(password!);
        }

        await auditTrail.RecordAsync(
            new AuditEntry
            {
                ManagerId = manager.Id,
                Action = AuditAction.ManagerSignedIn,
                Subject = manager.PhoneNumber,
                IpAddress = ipAddress,
            },
            cancellationToken);

        return SignInResult.Success(manager);
    }

    private Task RecordFailureAsync(string? subject, string reason, string? ipAddress, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction.ManagerSignInFailed,
                Subject = subject,
                DetailsJson = $$"""{"reason":"{{reason}}"}""",
                IpAddress = ipAddress,
            },
            cancellationToken);
}
