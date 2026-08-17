using MultiMessenger.Core.Enums;

namespace MultiMessenger.Core.Messaging;

/// <summary>
/// Создаёт коннектор для конкретного канала. По одной реализации на платформу.
/// <para>
/// Нужна, чтобы менеджер подключений на этапе 2.3 поднимал каналы, не зная,
/// какие вообще бывают платформы: он берёт фабрику по значению
/// <see cref="MessengerAccount.Platform"/> и работает с результатом через интерфейс.
/// </para>
/// </summary>
public interface IMessengerConnectorFactory
{
    MessengerPlatform Platform { get; }

    IMessengerConnector Create(Guid messengerAccountId, IMessengerEventSink sink);
}
