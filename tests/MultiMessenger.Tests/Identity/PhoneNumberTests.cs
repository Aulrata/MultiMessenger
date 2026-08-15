using FluentAssertions;
using MultiMessenger.Core.Identity;

namespace MultiMessenger.Tests.Identity;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+79001234567")]
    [InlineData("79001234567")]
    [InlineData("89001234567")]
    [InlineData("8 (900) 123-45-67")]
    [InlineData("+7 900 123 45 67")]
    [InlineData("9001234567")]
    [InlineData(" 8-900-123-45-67 ")]
    public void RussianNumbersNormalizeToSingleForm(string input)
    {
        PhoneNumber.TryNormalize(input, out var normalized).Should().BeTrue();

        normalized.Should().Be("+79001234567");
    }

    [Fact]
    public void InternationalNumberKeepsItsCountryCode()
    {
        PhoneNumber.TryNormalize("+49 151 12345678", out var normalized).Should().BeTrue();

        normalized.Should().Be("+4915112345678");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("не телефон")]
    [InlineData("+7900123456")]        // на цифру короче
    [InlineData("1234567890123456")]   // длиннее E.164
    public void MalformedInputIsRejected(string? input)
    {
        PhoneNumber.TryNormalize(input, out var normalized).Should().BeFalse();

        normalized.Should().BeEmpty();
        PhoneNumber.IsValid(input).Should().BeFalse();
    }

    /// <summary>
    /// Ради этого нормализация и существует: один сотрудник, записанный разными
    /// способами, обязан быть одним логином.
    /// </summary>
    [Fact]
    public void DifferentNotationsOfSameNumberCollide()
    {
        PhoneNumber.TryNormalize("8 900 111-22-33", out var first);
        PhoneNumber.TryNormalize("+7 (900) 1112233", out var second);

        first.Should().Be(second);
    }
}
