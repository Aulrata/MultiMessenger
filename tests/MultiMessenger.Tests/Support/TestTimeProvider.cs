namespace MultiMessenger.Tests.Support;

/// <summary>
/// Управляемые часы. Проверять паузы очереди настоящим ожиданием нельзя:
/// пауза между получателями — двадцать пять секунд, и такой тест никто
/// не станет запускать.
/// </summary>
public sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan interval) => _now += interval;
}
