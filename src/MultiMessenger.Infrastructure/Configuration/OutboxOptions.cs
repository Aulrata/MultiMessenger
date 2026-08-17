using System.ComponentModel.DataAnnotations;

namespace MultiMessenger.Infrastructure.Configuration;

/// <summary>
/// Настройки очереди исходящих. Секция <c>Outbox</c>.
/// <para>
/// Паузы здесь — не про производительность, а про сохранность аккаунтов.
/// Неофициальные клиенты Telegram и WhatsApp вызывают подозрения антифрода,
/// и быстрая рассылка разным получателям — самый заметный его признак.
/// Отключать эти паузы «для скорости» нельзя.
/// </para>
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// Пауза перед отправкой новому получателю. Значение из плана — 20–30 секунд;
    /// именно смена собеседника выглядит как рассылка.
    /// </summary>
    [Range(0, 600)]
    public int DelayBetweenRecipientsSeconds { get; init; } = 25;

    /// <summary>
    /// Пауза между сообщениями одному и тому же собеседнику. Человек вполне может
    /// написать несколько сообщений подряд, поэтому здесь достаточно секунд.
    /// </summary>
    [Range(0, 600)]
    public int DelayWithinDialogSeconds { get; init; } = 3;

    /// <summary>Как часто очередь заглядывает в базу за новыми сообщениями.</summary>
    [Range(1, 60)]
    public int PollIntervalSeconds { get; init; } = 2;

    /// <summary>
    /// После этого числа неудач сообщение помечается ошибкой и показывается
    /// менеджеру. Иначе временная неполадка превратилась бы в вечный цикл.
    /// </summary>
    [Range(1, 20)]
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Базовая пауза перед повтором после временной ошибки; растёт с числом попыток.</summary>
    [Range(1, 600)]
    public int RetryBackoffSeconds { get; init; } = 30;

    public TimeSpan DelayBetweenRecipients => TimeSpan.FromSeconds(DelayBetweenRecipientsSeconds);

    public TimeSpan DelayWithinDialog => TimeSpan.FromSeconds(DelayWithinDialogSeconds);

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);

    /// <summary>Пауза перед повтором: 30 секунд, минута, две, четыре — и так далее.</summary>
    public TimeSpan BackoffFor(int attempts) =>
        TimeSpan.FromSeconds(RetryBackoffSeconds * Math.Pow(2, Math.Max(0, attempts - 1)));
}
