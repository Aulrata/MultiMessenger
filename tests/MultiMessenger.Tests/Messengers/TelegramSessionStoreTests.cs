using FluentAssertions;
using MultiMessenger.Infrastructure.Messengers.Telegram;

namespace MultiMessenger.Tests.Messengers;

public class TelegramSessionStoreTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"mm-sessions-{Guid.CreateVersion7():N}");

    [Fact]
    public void SessionPathIsBuiltFromAccountId()
    {
        var accountId = Guid.Parse("0198f2a1-0000-7000-8000-0000000000aa");

        var path = TelegramSessionStore.GetSessionPath(_basePath, accountId);

        Path.GetFileName(path).Should().Be($"{accountId:N}.session");
        Directory.Exists(_basePath).Should().BeTrue("каталог создаётся, если его ещё нет");
    }

    /// <summary>
    /// В имени не должно быть номера телефона: по содержимому каталога нельзя
    /// понять, чьи это аккаунты.
    /// </summary>
    [Fact]
    public void SessionFileNameCarriesNoPhoneNumber()
    {
        var path = TelegramSessionStore.GetSessionPath(_basePath, Guid.CreateVersion7());

        Path.GetFileName(path).Should().NotContain("+").And.NotContain("7900");
    }

    /// <summary>
    /// Файл обязан лежать ровно в заданном каталоге. Проверка на случай, если
    /// когда-нибудь имя начнут собирать не только из идентификатора.
    /// </summary>
    [Fact]
    public void SessionFileStaysInsideTheConfiguredDirectory()
    {
        var path = TelegramSessionStore.GetSessionPath(_basePath, Guid.CreateVersion7());

        Path.GetDirectoryName(path).Should().Be(Path.GetFullPath(_basePath));
        Path.IsPathFullyQualified(path).Should().BeTrue();
    }

    /// <summary>Относительный базовый путь разворачивается в абсолютный.</summary>
    [Fact]
    public void RelativeBasePathIsResolved()
    {
        var relative = Path.Combine(".", Path.GetFileName(_basePath));

        var path = TelegramSessionStore.GetSessionPath(relative, Guid.CreateVersion7());

        path.Should().NotContain("..").And.NotContain($".{Path.DirectorySeparatorChar}{Path.GetFileName(_basePath)}{Path.DirectorySeparatorChar}");
        Path.IsPathFullyQualified(path).Should().BeTrue();

        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void DifferentAccountsGetDifferentFiles()
    {
        var first = TelegramSessionStore.GetSessionPath(_basePath, Guid.CreateVersion7());
        var second = TelegramSessionStore.GetSessionPath(_basePath, Guid.CreateVersion7());

        first.Should().NotBe(second);
    }

    [Fact]
    public void DeletingMissingSessionIsNotAnError()
    {
        var accountId = Guid.CreateVersion7();

        var delete = () => TelegramSessionStore.DeleteSession(_basePath, accountId);

        delete.Should().NotThrow();
    }

    [Fact]
    public void DeletedSessionDisappears()
    {
        var accountId = Guid.CreateVersion7();
        var path = TelegramSessionStore.GetSessionPath(_basePath, accountId);
        File.WriteAllText(path, "сессия");

        TelegramSessionStore.DeleteSession(_basePath, accountId);

        File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyBasePathIsRejected(string basePath)
    {
        var build = () => TelegramSessionStore.GetSessionPath(basePath, Guid.CreateVersion7());

        build.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
