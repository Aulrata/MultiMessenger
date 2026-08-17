using FluentAssertions;
using MultiMessenger.Infrastructure.Configuration;
using MultiMessenger.Infrastructure.Messengers.Outbox;
using MultiMessenger.Tests.Support;

namespace MultiMessenger.Tests.Messengers;

/// <summary>
/// Паузы между отправками защищают аккаунты от блокировки, а не экономят ресурсы.
/// Ошибка здесь — либо бесполезно медленная переписка, либо попавший под подозрение
/// антифрода аккаунт.
/// </summary>
public class OutboxRateLimiterTests
{
    private static readonly OutboxOptions Options = new()
    {
        DelayBetweenRecipientsSeconds = 25,
        DelayWithinDialogSeconds = 3,
    };

    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _accountId = Guid.CreateVersion7();

    [Fact]
    public void FirstSendGoesImmediately()
    {
        var limiter = new OutboxRateLimiter(_time);

        limiter.IsAllowed(_accountId, "client-1", Options).Should().BeTrue();
    }

    /// <summary>
    /// Смена собеседника — самый заметный признак рассылки, здесь пауза полная.
    /// </summary>
    [Fact]
    public void SwitchingRecipientWaitsTheLongPause()
    {
        var limiter = new OutboxRateLimiter(_time);
        limiter.RecordSend(_accountId, "client-1");

        _time.Advance(TimeSpan.FromSeconds(24));
        limiter.IsAllowed(_accountId, "client-2", Options).Should().BeFalse();

        _time.Advance(TimeSpan.FromSeconds(1));
        limiter.IsAllowed(_accountId, "client-2", Options).Should().BeTrue();
    }

    /// <summary>
    /// А несколько сообщений подряд одному человеку — обычное поведение,
    /// и ждать по двадцать пять секунд между ними незачем.
    /// </summary>
    [Fact]
    public void ContinuingTheSameConversationWaitsLess()
    {
        var limiter = new OutboxRateLimiter(_time);
        limiter.RecordSend(_accountId, "client-1");

        _time.Advance(TimeSpan.FromSeconds(2));
        limiter.IsAllowed(_accountId, "client-1", Options).Should().BeFalse();

        _time.Advance(TimeSpan.FromSeconds(1));
        limiter.IsAllowed(_accountId, "client-1", Options).Should().BeTrue();
    }

    /// <summary>Пауза одного менеджера не должна задерживать переписку остальных.</summary>
    [Fact]
    public void AccountsAreThrottledIndependently()
    {
        var limiter = new OutboxRateLimiter(_time);
        var otherAccount = Guid.CreateVersion7();

        limiter.RecordSend(_accountId, "client-1");

        limiter.IsAllowed(_accountId, "client-2", Options).Should().BeFalse();
        limiter.IsAllowed(otherAccount, "client-2", Options).Should().BeTrue();
    }

    [Fact]
    public void RemainingTimeIsReported()
    {
        var limiter = new OutboxRateLimiter(_time);
        limiter.RecordSend(_accountId, "client-1");

        _time.Advance(TimeSpan.FromSeconds(10));

        limiter.TimeUntilAllowed(_accountId, "client-2", Options).Should().Be(TimeSpan.FromSeconds(15));
        limiter.TimeUntilAllowed(_accountId, "client-1", Options).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ForgettingAccountResetsThePause()
    {
        var limiter = new OutboxRateLimiter(_time);
        limiter.RecordSend(_accountId, "client-1");

        limiter.Forget(_accountId);

        limiter.IsAllowed(_accountId, "client-2", Options).Should().BeTrue();
    }

    /// <summary>Нулевые паузы допустимы — для тестов и для платформ без ограничений.</summary>
    [Fact]
    public void ZeroDelaysAllowContinuousSending()
    {
        var limiter = new OutboxRateLimiter(_time);
        var noDelays = new OutboxOptions { DelayBetweenRecipientsSeconds = 0, DelayWithinDialogSeconds = 0 };

        limiter.RecordSend(_accountId, "client-1");

        limiter.IsAllowed(_accountId, "client-2", noDelays).Should().BeTrue();
    }
}
