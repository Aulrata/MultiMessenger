using System.Collections.Concurrent;

namespace MultiMessenger.Infrastructure.Messengers.Telegram;

/// <summary>
/// Запоминает <c>access_hash</c> собеседников.
/// <para>
/// В MTProto обратиться к пользователю по одному идентификатору нельзя — нужен ещё
/// и хеш доступа, который приходит вместе с апдейтами. Без кеша пришлось бы
/// запрашивать его перед каждой отправкой, а лишние обращения к API у Telegram
/// на счету.
/// </para>
/// </summary>
public sealed class TelegramPeerCache
{
    private readonly ConcurrentDictionary<long, long> _accessHashes = new();

    public void Remember(long userId, long accessHash)
    {
        if (accessHash != 0)
        {
            _accessHashes[userId] = accessHash;
        }
    }

    public bool TryGet(long userId, out long accessHash) => _accessHashes.TryGetValue(userId, out accessHash);
}
