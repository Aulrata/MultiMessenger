namespace MultiMessenger.Infrastructure.Configuration;

/// <summary>
/// Учётные данные первого администратора. Секция <c>Seed</c>.
/// <para>
/// Используются ровно один раз — когда в базе нет ни одного сотрудника.
/// Валидации при старте нет намеренно: на уже работающей системе секция
/// не нужна и должна спокойно отсутствовать.
/// </para>
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string? AdminPhoneNumber { get; init; }

    public string? AdminPassword { get; init; }

    public string AdminFullName { get; init; } = "Администратор";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AdminPhoneNumber) && !string.IsNullOrWhiteSpace(AdminPassword);
}
