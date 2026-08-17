using System.Collections.Concurrent;
using MultiMessenger.Infrastructure.Configuration;

namespace MultiMessenger.Infrastructure.Messengers.Outbox;

/// <summary>
/// Решает, можно ли каналу отправлять прямо сейчас.
/// <para>
/// Очередь у каждого канала своя: пауза одного менеджера не должна задерживать
/// переписку остальных.
/// </para>
/// <para>
/// Различает смену собеседника и продолжение разговора. Несколько сообщений подряд
/// одному человеку — обычное поведение, а быстрый перебор получателей выглядит
/// как рассылка, и именно на него реагирует антифрод платформы.
/// </para>
/// </summary>
public sealed class OutboxRateLimiter(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<Guid, LastSend> _lastSends = new();

    /// <summary>
    /// Сколько ждать до отправки этому получателю. <see cref="TimeSpan.Zero"/> —
    /// можно отправлять немедленно.
    /// </summary>
    public TimeSpan TimeUntilAllowed(Guid messengerAccountId, string platformUserId, OutboxOptions options)
    {
        if (!_lastSends.TryGetValue(messengerAccountId, out var last))
        {
            return TimeSpan.Zero;
        }

        var required = last.PlatformUserId == platformUserId
            ? options.DelayWithinDialog
            : options.DelayBetweenRecipients;

        var elapsed = timeProvider.GetUtcNow() - last.SentAt;
        var remaining = required - elapsed;

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public bool IsAllowed(Guid messengerAccountId, string platformUserId, OutboxOptions options) =>
        TimeUntilAllowed(messengerAccountId, platformUserId, options) == TimeSpan.Zero;

    /// <summary>
    /// Отмечает состоявшуюся отправку. Вызывается и при неудаче тоже: обращение
    /// к платформе всё равно произошло, и для её лимитов это одно и то же.
    /// </summary>
    public void RecordSend(Guid messengerAccountId, string platformUserId) =>
        _lastSends[messengerAccountId] = new LastSend(platformUserId, timeProvider.GetUtcNow());

    public void Forget(Guid messengerAccountId) => _lastSends.TryRemove(messengerAccountId, out _);

    private sealed record LastSend(string PlatformUserId, DateTimeOffset SentAt);
}
