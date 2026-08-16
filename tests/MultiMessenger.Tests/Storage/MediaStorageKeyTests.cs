using FluentAssertions;
using MultiMessenger.Core.Storage;

namespace MultiMessenger.Tests.Storage;

public class MediaStorageKeyTests
{
    private static readonly Guid AttachmentId = Guid.Parse("0198f2a1-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset August2026 = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void KeyIsPartitionedByYearAndMonth()
    {
        var key = MediaStorageKey.Create(AttachmentId, August2026, "договор.pdf");

        key.Should().Be($"media/2026/08/{AttachmentId}.pdf");
    }

    [Fact]
    public void KeyWithoutFileNameHasNoExtension()
    {
        MediaStorageKey.Create(AttachmentId, August2026)
            .Should().Be($"media/2026/08/{AttachmentId}");
    }

    /// <summary>
    /// Имя файла приходит от клиента, поэтому в ключ попадает только заведомо
    /// безобидное расширение — всё прочее отбрасывается.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("файл.")]
    [InlineData("файл.tar.gz/../..")]
    [InlineData("отчёт.ооооченьдлинноерасширение")]
    [InlineData("странный.p df")]
    public void SuspiciousFileNameLosesItsExtension(string fileName)
    {
        var key = MediaStorageKey.Create(AttachmentId, August2026, fileName);

        key.Should().Be($"media/2026/08/{AttachmentId}");
    }

    [Fact]
    public void ExtensionIsLowercased()
    {
        MediaStorageKey.Create(AttachmentId, August2026, "PHOTO.JPG")
            .Should().EndWith(".jpg");
    }
}
