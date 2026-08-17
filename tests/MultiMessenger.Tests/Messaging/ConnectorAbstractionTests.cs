using FluentAssertions;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;

namespace MultiMessenger.Tests.Messaging;

/// <summary>
/// Проверяет пригодность <see cref="IMessengerConnector"/> для всех трёх платформ.
/// <para>
/// План прямо предупреждает: если на этапе 3 придётся менять что-то за пределами
/// новых коннекторов, значит абстракция спроектирована плохо. Дешевле выяснить это
/// сейчас, на заготовках, чем через месяц на живом коде.
/// </para>
/// </summary>
public class ConnectorAbstractionTests
{
    /// <summary>
    /// Один и тот же код доводит до конца вход по коду, по QR и по токену.
    /// Он не знает, с какой платформой работает, и не содержит ни одной проверки
    /// на <see cref="MessengerPlatform"/> — именно это и требуется от абстракции.
    /// </summary>
    private static async Task<LoginStep> DriveLoginAsync(
        IMessengerConnector connector,
        LoginRequest request,
        Queue<string> userInput)
    {
        var step = await connector.BeginLoginAsync(request);

        for (var guard = 0; guard < 10; guard++)
        {
            LoginAnswer answer = step switch
            {
                LoginStep.NeedsVerificationCode => new LoginAnswer.VerificationCode(userInput.Dequeue()),
                LoginStep.NeedsTwoFactorPassword => new LoginAnswer.TwoFactorPassword(userInput.Dequeue()),
                LoginStep.NeedsQrScan => new LoginAnswer.CheckStatus(),
                _ => null!,
            };

            if (answer is null)
            {
                return step;
            }

            step = await connector.ContinueLoginAsync(answer);
        }

        throw new InvalidOperationException("вход не завершился за разумное число шагов");
    }

    [Fact]
    public async Task PhoneAndCodeLoginCompletes()
    {
        var connector = new PhoneAndCodeConnector();
        var input = new Queue<string>(["12345", "секрет"]);

        var step = await DriveLoginAsync(
            connector,
            new LoginRequest { MessengerAccountId = connector.MessengerAccountId, PhoneNumber = "+79001234567" },
            input);

        step.Should().BeOfType<LoginStep.Completed>()
            .Which.Account.PlatformUserId.Should().Be("tg-1001");
    }

    [Fact]
    public async Task QrLoginCompletesAfterPolling()
    {
        var connector = new QrCodeConnector(pollsBeforeSuccess: 3);

        var step = await DriveLoginAsync(
            connector,
            new LoginRequest { MessengerAccountId = connector.MessengerAccountId },
            new Queue<string>());

        step.Should().BeOfType<LoginStep.Completed>();
    }

    [Fact]
    public async Task BotTokenLoginCompletesImmediately()
    {
        var connector = new BotTokenConnector();

        var step = await DriveLoginAsync(
            connector,
            new LoginRequest { MessengerAccountId = connector.MessengerAccountId, BotToken = "valid-token" },
            new Queue<string>());

        step.Should().BeOfType<LoginStep.Completed>()
            .Which.Account.PlatformUserId.Should().Be("max-bot-42");
    }

    [Theory]
    [MemberData(nameof(AllConnectors))]
    public async Task EveryPlatformReportsFailureInsteadOfThrowing(IMessengerConnector connector)
    {
        // Пустой запрос: ни номера, ни токена.
        var step = await connector.BeginLoginAsync(new LoginRequest { MessengerAccountId = connector.MessengerAccountId });

        // QR-вход ничего не требует заранее, остальные обязаны отказать понятной причиной.
        if (connector.Capabilities.LoginMethod is LoginMethod.QrCode)
        {
            step.Should().BeOfType<LoginStep.NeedsQrScan>();
            return;
        }

        step.Should().BeOfType<LoginStep.Failed>()
            .Which.Reason.Should().NotBe(LoginFailureReason.Unknown, "причина отказа должна быть внятной");
    }

    /// <summary>
    /// Загрузчик истории на этапе 2.8 не должен ничего знать о платформах.
    /// У MAX истории нет, и он обязан вернуть пустую последовательность,
    /// а не бросить исключение.
    /// </summary>
    [Fact]
    public async Task PlatformWithoutHistoryReturnsEmptySequence()
    {
        var connector = new BotTokenConnector();

        connector.Capabilities.SupportsHistoryBackfill.Should().BeFalse();

        var messages = new List<IncomingMessage>();
        await foreach (var message in connector.EnumerateHistoryAsync())
        {
            messages.Add(message);
        }

        messages.Should().BeEmpty();
    }

    public static TheoryData<IMessengerConnector> AllConnectors() =>
    [
        new PhoneAndCodeConnector(),
        new QrCodeConnector(),
        new BotTokenConnector(),
    ];
}
