using MultiMessenger.Core.Entities;

namespace MultiMessenger.Infrastructure.Identity;

public enum SignInFailure
{
    None,
    InvalidPhoneNumberFormat,
    UnknownManagerOrWrongPassword,
    Deactivated,
}

/// <summary>
/// Результат проверки учётных данных.
/// <para>
/// Несуществующий номер и неверный пароль намеренно неразличимы снаружи:
/// иначе форма входа превращается в способ узнать, кто работает в компании.
/// В журнале аудита при этом фиксируется всё, что известно.
/// </para>
/// </summary>
public sealed record SignInResult(Manager? Manager, SignInFailure Failure)
{
    public bool Succeeded => Failure is SignInFailure.None && Manager is not null;

    public static SignInResult Success(Manager manager) => new(manager, SignInFailure.None);

    public static SignInResult Failed(SignInFailure failure) => new(null, failure);
}
