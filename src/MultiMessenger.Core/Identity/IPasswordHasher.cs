namespace MultiMessenger.Core.Identity;

/// <summary>
/// Хеширование паролей за абстракцией: доменному слою не положено знать,
/// каким алгоритмом это делается, а сменить алгоритм со временем придётся.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerificationResult Verify(string hash, string password);
}

public enum PasswordVerificationResult
{
    Failed,

    Success,

    /// <summary>
    /// Пароль верный, но хеш посчитан устаревшими параметрами. Повод пересчитать
    /// его прямо во время успешного входа — другого момента, когда известен
    /// открытый пароль, не будет.
    /// </summary>
    SuccessRehashNeeded,
}
