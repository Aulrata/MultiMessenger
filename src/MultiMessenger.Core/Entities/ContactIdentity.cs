using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Entities;

/// <summary>
/// Идентификатор клиента на одной платформе. По нему входящее сообщение
/// сопоставляется с существующим <see cref="Contact"/>.
/// </summary>
public class ContactIdentity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ContactId { get; set; }

    public Contact? Contact { get; set; }

    public MessengerPlatform Platform { get; set; }

    /// <summary>
    /// Идентификатор на стороне платформы: user id в Telegram, JID в WhatsApp, user id в MAX.
    /// Строка, потому что форматы у платформ разные.
    /// </summary>
    public string PlatformUserId { get; set; } = string.Empty;

    /// <summary>Имя или username, как они видны на этой платформе.</summary>
    public string? DisplayNameOnPlatform { get; set; }
}
