using FluentAssertions;
using MultiMessenger.Core.Identity;
using MultiMessenger.Infrastructure.Identity;

namespace MultiMessenger.Tests.Identity;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new IdentityPasswordHasher();

    [Fact]
    public void CorrectPasswordIsAccepted()
    {
        var hash = _hasher.Hash("Admin-Local-2026");

        _hasher.Verify(hash, "Admin-Local-2026").Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void WrongPasswordIsRejected()
    {
        var hash = _hasher.Hash("Admin-Local-2026");

        _hasher.Verify(hash, "admin-local-2026").Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void HashIsNotThePasswordItself()
    {
        const string password = "Admin-Local-2026";

        _hasher.Hash(password).Should().NotContain(password);
    }

    /// <summary>
    /// Соль должна быть своя у каждого хеша: одинаковые пароли двух сотрудников
    /// не должны выглядеть одинаково в базе.
    /// </summary>
    [Fact]
    public void SamePasswordProducesDifferentHashes()
    {
        var first = _hasher.Hash("Admin-Local-2026");
        var second = _hasher.Hash("Admin-Local-2026");

        first.Should().NotBe(second);
        _hasher.Verify(second, "Admin-Local-2026").Should().Be(PasswordVerificationResult.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("не-хеш-вовсе")]
    public void GarbageHashDoesNotThrow(string hash)
    {
        _hasher.Verify(hash, "Admin-Local-2026").Should().Be(PasswordVerificationResult.Failed);
    }
}
