using FluentAssertions;
using MultiMessenger.Core.Messaging;

namespace MultiMessenger.Tests.Messaging;

/// <summary>
/// По этим правилам интерфейс решает, показывать ли кнопки правки и удаления.
/// Ошибка здесь — либо кнопка, которая всегда отваливается ошибкой платформы,
/// либо отсутствие кнопки там, где действие возможно.
/// </summary>
public class PlatformCapabilitiesTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EditingIsAllowedInsideTheWindow()
    {
        var capabilities = WithEditWindow(TimeSpan.FromHours(48));

        capabilities.CanEdit(SentAt, SentAt.AddHours(47)).Should().BeTrue();
        capabilities.CanEdit(SentAt, SentAt.AddHours(48)).Should().BeTrue("граница входит в окно");
        capabilities.CanEdit(SentAt, SentAt.AddHours(49)).Should().BeFalse();
    }

    /// <summary>Отсутствие окна означает «всегда можно», а не «нельзя никогда».</summary>
    [Fact]
    public void MissingWindowMeansNoTimeLimit()
    {
        var capabilities = WithEditWindow(null);

        capabilities.CanEdit(SentAt, SentAt.AddYears(5)).Should().BeTrue();
    }

    [Fact]
    public void UnsupportedEditingIsNeverAllowed()
    {
        var capabilities = WithEditWindow(TimeSpan.FromHours(48)) with { SupportsEditing = false };

        capabilities.CanEdit(SentAt, SentAt).Should().BeFalse();
    }

    [Fact]
    public void DeleteForEveryoneFollowsItsOwnWindow()
    {
        var capabilities = WithEditWindow(TimeSpan.FromHours(48)) with
        {
            SupportsDeleteForEveryone = true,
            DeleteForEveryoneWindow = TimeSpan.FromHours(2),
        };

        capabilities.CanEdit(SentAt, SentAt.AddHours(3)).Should().BeTrue();
        capabilities.CanDeleteForEveryone(SentAt, SentAt.AddHours(3)).Should().BeFalse();
    }

    private static PlatformCapabilities WithEditWindow(TimeSpan? window) => new()
    {
        LoginMethod = LoginMethod.PhoneAndCode,
        RequiresPersistentConnection = true,
        SupportsHistoryBackfill = true,
        SupportsEditing = true,
        EditWindow = window,
        SupportsDeleteForEveryone = false,
    };
}
