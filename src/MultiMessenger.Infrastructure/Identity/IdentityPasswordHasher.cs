using Microsoft.AspNetCore.Identity;
using MultiMessenger.Core.Entities;
using CorePasswordVerificationResult = MultiMessenger.Core.Identity.PasswordVerificationResult;
using ICorePasswordHasher = MultiMessenger.Core.Identity.IPasswordHasher;

namespace MultiMessenger.Infrastructure.Identity;

/// <summary>
/// Обёртка над <see cref="PasswordHasher{TUser}"/> из ASP.NET Core Identity.
/// Взят он, а не сторонний BCrypt, по двум причинам: не тянет в проект лишнюю
/// зависимость и умеет сообщать, что хеш пора пересчитать при смене параметров.
/// Полноценный Identity со своими таблицами при этом не подключается —
/// у нас собственная сущность <see cref="Manager"/>.
/// </summary>
public class IdentityPasswordHasher : ICorePasswordHasher
{
    private readonly PasswordHasher<Manager> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(new Manager(), password);

    public CorePasswordVerificationResult Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password))
        {
            return CorePasswordVerificationResult.Failed;
        }

        try
        {
            return _hasher.VerifyHashedPassword(new Manager(), hash, password) switch
            {
                PasswordVerificationResult.Success => CorePasswordVerificationResult.Success,
                PasswordVerificationResult.SuccessRehashNeeded => CorePasswordVerificationResult.SuccessRehashNeeded,
                _ => CorePasswordVerificationResult.Failed,
            };
        }
        catch (FormatException)
        {
            // Хеш в колонке не Base64 — испорченные или подставленные вручную данные.
            // Это неудачный вход, а не повод отдать пятисотую страницу.
            return CorePasswordVerificationResult.Failed;
        }
    }
}
