namespace MultiMessenger.Core.Enums;

/// <summary>
/// Платформа, через которую идёт переписка. Добавление новой платформы на этапе 3
/// должно сводиться к расширению этого перечисления и появлению нового коннектора.
/// </summary>
public enum MessengerPlatform
{
    Telegram,
    WhatsApp,
    Max,
}
