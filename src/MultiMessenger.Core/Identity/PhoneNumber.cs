using System.Text;

namespace MultiMessenger.Core.Identity;

/// <summary>
/// Нормализация рабочих номеров. Номер — это логин в систему и одновременно
/// идентификатор аккаунта в Telegram и WhatsApp, поэтому «+7 900 123-45-67»,
/// «8 (900) 1234567» и «79001234567» обязаны схлопываться в одну строку.
/// Иначе один сотрудник заведётся дважды и не сможет войти.
/// </summary>
public static class PhoneNumber
{
    private const int RussianNationalLength = 10;
    private const int MinInternationalLength = 11;
    private const int MaxInternationalLength = 15;

    /// <summary>
    /// Приводит номер к формату E.164 (<c>+79001234567</c>).
    /// Российские номера принимаются в национальных вариантах записи —
    /// с восьмёркой и без кода страны; остальные требуют явного кода после плюса.
    /// </summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        var digits = ExtractDigits(trimmed);

        if (digits.Length == 0)
        {
            return false;
        }

        // Ведущий плюс означает, что код страны уже указан. Достраивать такой номер
        // до российского нельзя: «+7900123456» — это опечатка, а не «9001234567».
        if (trimmed.StartsWith('+'))
        {
            if (digits.Length is < MinInternationalLength or > MaxInternationalLength)
            {
                return false;
            }

            normalized = "+" + digits;
            return true;
        }

        // 9001234567 → +79001234567
        if (digits.Length == RussianNationalLength)
        {
            normalized = "+7" + digits;
            return true;
        }

        // 89001234567 → +79001234567
        if (digits.Length == RussianNationalLength + 1 && digits[0] == '8')
        {
            normalized = "+7" + digits[1..];
            return true;
        }

        if (digits.Length is >= MinInternationalLength and <= MaxInternationalLength)
        {
            normalized = "+" + digits;
            return true;
        }

        return false;
    }

    /// <summary>Нормализован ли номер уже сейчас — для проверок в тестах и валидаторах.</summary>
    public static bool IsValid(string? input) => TryNormalize(input, out _);

    private static string ExtractDigits(string input)
    {
        var digits = new StringBuilder(input.Length);

        foreach (var character in input)
        {
            if (char.IsAsciiDigit(character))
            {
                digits.Append(character);
            }
        }

        return digits.ToString();
    }
}
