using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MultiMessenger.Core.Auditing;
using MultiMessenger.Core.Entities;
using MultiMessenger.Core.Enums;
using MultiMessenger.Core.Messaging;
using MultiMessenger.Infrastructure.Auditing;
using MultiMessenger.Infrastructure.Configuration;
using MultiMessenger.Infrastructure.Messengers;
using MultiMessenger.Tests.Messaging;
using MultiMessenger.Tests.Persistence;
using NSubstitute;

namespace MultiMessenger.Tests.Messengers;

/// <summary>
/// Сквозной путь подключения канала: номер → код → пароль → рабочее состояние.
/// Telegram подменён заготовкой: реальные обращения к платформе медленны,
/// нестабильны и упираются в лимиты.
/// </summary>
[Collection(PostgresCollection.Name)]
public class MessengerAccountServiceTests(PostgresFixture postgres) : IDisposable
{
    private const string Ip = "203.0.113.11";

    private readonly string _sessionsPath = Path.Combine(Path.GetTempPath(), $"mm-2-4-{Guid.CreateVersion7():N}");
    private readonly PendingLoginStore _pendingLogins = new(NullLogger<PendingLoginStore>.Instance);
    private readonly ScriptedConnectorFactory _factory = new();

    private AccountConnectionManager _connections = null!;

    [Fact]
    public async Task FullLoginFlowActivatesTheChannel()
    {
        var manager = await CreateManagerAsync();

        var (loginId, step) = await BeginAsync(manager.Id, manager.PhoneNumber);
        step.Should().BeOfType<LoginStep.NeedsVerificationCode>();

        (loginId, step) = await ContinueAsync(manager.Id, loginId, new LoginAnswer.VerificationCode("12345"));
        step.Should().BeOfType<LoginStep.NeedsTwoFactorPassword>();

        (_, step) = await ContinueAsync(manager.Id, loginId, new LoginAnswer.TwoFactorPassword("секрет"));
        step.Should().BeOfType<LoginStep.Completed>();

        await using var dbContext = postgres.CreateDbContext();
        var account = await dbContext.MessengerAccounts.SingleAsync(item => item.ManagerId == manager.Id);

        account.Status.Should().Be(MessengerAccountStatus.Active);
        account.ConnectedAt.Should().NotBeNull();
        account.SessionPath.Should().NotBeNullOrEmpty();
        _connections.TryGet(account.Id, out _).Should().BeTrue("соединение переходит менеджеру подключений");
        _pendingLogins.Count.Should().Be(0, "завершённая попытка убирается из хранилища");
    }

    [Fact]
    public async Task SuccessfulConnectionIsAudited()
    {
        var manager = await CreateManagerAsync();

        var (loginId, _) = await BeginAsync(manager.Id, manager.PhoneNumber);
        (loginId, _) = await ContinueAsync(manager.Id, loginId, new LoginAnswer.VerificationCode("12345"));
        await ContinueAsync(manager.Id, loginId, new LoginAnswer.TwoFactorPassword("секрет"));

        await using var dbContext = postgres.CreateDbContext();
        var audit = await dbContext.AuditEntries
            .SingleAsync(entry => entry.ManagerId == manager.Id && entry.Action == AuditAction.MessengerAccountConnected);

        audit.Subject.Should().Be(manager.PhoneNumber);
        audit.IpAddress.Should().Be(Ip);
        audit.DetailsJson.Should().Contain("Telegram");
    }

    /// <summary>Номер приводится к единому виду до создания записи о канале.</summary>
    [Fact]
    public async Task PhoneNumberIsNormalizedBeforeSaving()
    {
        var manager = await CreateManagerAsync();
        var national = "8" + manager.PhoneNumber[2..];

        await BeginAsync(manager.Id, national);

        await using var dbContext = postgres.CreateDbContext();
        var account = await dbContext.MessengerAccounts.SingleAsync(item => item.ManagerId == manager.Id);

        account.PhoneNumber.Should().Be(manager.PhoneNumber);
    }

    [Fact]
    public async Task MalformedPhoneNumberIsRejectedWithoutCreatingAccount()
    {
        var manager = await CreateManagerAsync();

        var (_, step) = await BeginAsync(manager.Id, "не телефон");

        step.Should().BeOfType<LoginStep.Failed>()
            .Which.Reason.Should().Be(LoginFailureReason.InvalidPhoneNumber);

        await using var dbContext = postgres.CreateDbContext();
        (await dbContext.MessengerAccounts.AnyAsync(item => item.ManagerId == manager.Id)).Should().BeFalse();
    }

    /// <summary>
    /// На пару «сотрудник + платформа» стоит уникальный индекс. Повторная попытка
    /// обязана переиспользовать запись, а не падать на нарушении ограничения.
    /// </summary>
    [Fact]
    public async Task RetryReusesTheSameAccountRow()
    {
        var manager = await CreateManagerAsync();

        var (firstLogin, _) = await BeginAsync(manager.Id, manager.PhoneNumber);
        await CancelAsync(manager.Id, firstLogin);
        await BeginAsync(manager.Id, manager.PhoneNumber);

        await using var dbContext = postgres.CreateDbContext();
        (await dbContext.MessengerAccounts.CountAsync(item => item.ManagerId == manager.Id)).Should().Be(1);
    }

    [Fact]
    public async Task WrongCodeEndsTheAttemptAndClosesConnection()
    {
        var manager = await CreateManagerAsync();

        var (loginId, _) = await BeginAsync(manager.Id, manager.PhoneNumber);
        var (nextLoginId, step) = await ContinueAsync(manager.Id, loginId, new LoginAnswer.VerificationCode("00000"));

        step.Should().BeOfType<LoginStep.Failed>()
            .Which.Reason.Should().Be(LoginFailureReason.InvalidCode);
        nextLoginId.Should().Be(Guid.Empty);
        _pendingLogins.Count.Should().Be(0, "неудачная попытка не должна держать сокет");
    }

    /// <summary>Чужую попытку продолжить нельзя, даже зная её идентификатор.</summary>
    [Fact]
    public async Task AnotherManagerCannotContinueTheLogin()
    {
        var owner = await CreateManagerAsync();
        var stranger = await CreateManagerAsync();

        var (loginId, _) = await BeginAsync(owner.Id, owner.PhoneNumber);

        var (_, step) = await ContinueAsync(stranger.Id, loginId, new LoginAnswer.VerificationCode("12345"));

        step.Should().BeOfType<LoginStep.Failed>()
            .Which.Reason.Should().Be(LoginFailureReason.Expired);
    }

    [Fact]
    public async Task ExpiredAttemptAsksToStartOver()
    {
        var manager = await CreateManagerAsync();
        var (loginId, _) = await BeginAsync(manager.Id, manager.PhoneNumber);

        await _pendingLogins.RemoveExpiredAsync(DateTimeOffset.UtcNow + PendingLoginStore.Lifetime + TimeSpan.FromMinutes(1));

        var (_, step) = await ContinueAsync(manager.Id, loginId, new LoginAnswer.VerificationCode("12345"));

        step.Should().BeOfType<LoginStep.Failed>()
            .Which.Reason.Should().Be(LoginFailureReason.Expired);
    }

    /// <summary>Второй вход в уже поднятый канал — лишний перелогин, платформе он не нравится.</summary>
    [Fact]
    public async Task ConnectingAnAlreadyConnectedChannelIsRefused()
    {
        var manager = await CreateManagerAsync();
        var (loginId, _) = await BeginAsync(manager.Id, manager.PhoneNumber);
        (loginId, _) = await ContinueAsync(manager.Id, loginId, new LoginAnswer.VerificationCode("12345"));
        await ContinueAsync(manager.Id, loginId, new LoginAnswer.TwoFactorPassword("секрет"));

        var (_, step) = await BeginAsync(manager.Id, manager.PhoneNumber);

        step.Should().BeOfType<LoginStep.Failed>()
            .Which.Details.Should().Contain("уже подключён");
    }

    [Fact]
    public async Task DisconnectClosesChannelAndRemovesSessionFile()
    {
        var manager = await CreateManagerAsync();
        var (loginId, _) = await BeginAsync(manager.Id, manager.PhoneNumber);
        (loginId, _) = await ContinueAsync(manager.Id, loginId, new LoginAnswer.VerificationCode("12345"));
        await ContinueAsync(manager.Id, loginId, new LoginAnswer.TwoFactorPassword("секрет"));

        Guid accountId;
        await using (var dbContext = postgres.CreateDbContext())
        {
            accountId = (await dbContext.MessengerAccounts.SingleAsync(item => item.ManagerId == manager.Id)).Id;
        }

        // Файл сессии обычно создаёт библиотека Telegram; в тесте кладём его руками.
        var sessionPath = Infrastructure.Messengers.Telegram.TelegramSessionStore
            .GetSessionPath(_sessionsPath, accountId);
        await File.WriteAllTextAsync(sessionPath, "сессия");

        (await DisconnectAsync(manager.Id, accountId)).Should().BeTrue();

        File.Exists(sessionPath).Should().BeFalse("сессия — действующий ключ доступа, её нельзя оставлять");
        _connections.TryGet(accountId, out _).Should().BeFalse();

        await using var verifyContext = postgres.CreateDbContext();
        var account = await verifyContext.MessengerAccounts.SingleAsync(item => item.Id == accountId);
        account.Status.Should().Be(MessengerAccountStatus.Disconnected);
        account.SessionPath.Should().BeNull();
    }

    [Fact]
    public async Task ManagerCannotDisconnectSomeoneElsesChannel()
    {
        var owner = await CreateManagerAsync();
        var stranger = await CreateManagerAsync();
        var (loginId, _) = await BeginAsync(owner.Id, owner.PhoneNumber);
        (loginId, _) = await ContinueAsync(owner.Id, loginId, new LoginAnswer.VerificationCode("12345"));
        await ContinueAsync(owner.Id, loginId, new LoginAnswer.TwoFactorPassword("секрет"));

        await using var dbContext = postgres.CreateDbContext();
        var accountId = (await dbContext.MessengerAccounts.SingleAsync(item => item.ManagerId == owner.Id)).Id;

        (await DisconnectAsync(stranger.Id, accountId)).Should().BeFalse();
    }

    /// <summary>
    /// На свежем сервере ключи Telegram могут быть не заполнены. Сотрудник должен
    /// увидеть объяснение, а не страницу с трассировкой стека.
    /// </summary>
    [Fact]
    public async Task MissingPlatformCredentialsProduceReadableFailure()
    {
        var manager = await CreateManagerAsync();
        _factory.ThrowOnCreate = true;

        var (_, step) = await BeginAsync(manager.Id, manager.PhoneNumber);

        step.Should().BeOfType<LoginStep.Failed>()
            .Which.Details.Should().Contain("не настроен на сервере");
        _pendingLogins.Count.Should().Be(0);
    }

    [Fact]
    public async Task UnsupportedPlatformIsReportedClearly()
    {
        var manager = await CreateManagerAsync();

        var (_, step) = await NewService().BeginLoginAsync(manager.Id, MessengerPlatform.Max, null, Ip);

        step.Should().BeOfType<LoginStep.Failed>()
            .Which.Details.Should().Contain("не поддерживается");
    }

    // --- обвязка ---------------------------------------------------------

    private Task<(Guid, LoginStep)> BeginAsync(Guid managerId, string? phone) =>
        NewService().BeginLoginAsync(managerId, MessengerPlatform.Telegram, phone, Ip);

    private Task<(Guid, LoginStep)> ContinueAsync(Guid managerId, Guid loginId, LoginAnswer answer) =>
        NewService().ContinueLoginAsync(managerId, loginId, answer, Ip);

    private Task CancelAsync(Guid managerId, Guid loginId) => NewService().CancelLoginAsync(managerId, loginId);

    private Task<bool> DisconnectAsync(Guid managerId, Guid accountId) =>
        NewService().DisconnectAsync(managerId, accountId, Ip);

    private MessengerAccountService NewService()
    {
        _connections ??= new AccountConnectionManager(
            [_factory],
            Substitute.For<IMessengerEventSink>(),
            NullLogger<AccountConnectionManager>.Instance);

        var dbContext = postgres.CreateDbContext();

        return new MessengerAccountService(
            dbContext,
            [_factory],
            _pendingLogins,
            _connections,
            Substitute.For<IMessengerEventSink>(),
            new EfAuditTrail(dbContext),
            Options.Create(new StorageOptions { SessionsBasePath = _sessionsPath }),
            NullLogger<MessengerAccountService>.Instance);
    }

    private async Task<Manager> CreateManagerAsync()
    {
        await using var dbContext = postgres.CreateDbContext();

        var manager = TestData.NewManager("hash");
        dbContext.Managers.Add(manager);
        await dbContext.SaveChangesAsync();

        return manager;
    }

    public void Dispose()
    {
        if (Directory.Exists(_sessionsPath))
        {
            Directory.Delete(_sessionsPath, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Заготовка Telegram: код «12345», затем пароль «секрет».</summary>
    private sealed class ScriptedConnectorFactory : IMessengerConnectorFactory
    {
        /// <summary>Имитирует незаполненные api_id и api_hash на сервере.</summary>
        public bool ThrowOnCreate { get; set; }

        public MessengerPlatform Platform => MessengerPlatform.Telegram;

        public IMessengerConnector Create(Guid messengerAccountId, IMessengerEventSink sink) =>
            ThrowOnCreate
                ? throw new InvalidOperationException("api_id и api_hash не заданы")
                : new PhoneAndCodeConnector { MessengerAccountId = messengerAccountId };
    }
}
