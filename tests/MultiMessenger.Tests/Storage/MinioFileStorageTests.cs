using System.Text;
using FluentAssertions;
using MultiMessenger.Core.Storage;

namespace MultiMessenger.Tests.Storage;

[Collection(MinioCollection.Name)]
public class MinioFileStorageTests(MinioFixture minio)
{
    [Fact]
    public async Task SavedFileComesBackUnchanged()
    {
        var key = NewKey();
        var content = Encoding.UTF8.GetBytes("Голосовое сообщение, но текстом");

        await minio.Storage.SaveAsync(new MemoryStream(content), key, "audio/ogg");

        await using var stored = await minio.Storage.GetAsync(key);

        stored.Should().NotBeNull();
        stored!.ContentType.Should().Be("audio/ogg");
        stored.SizeBytes.Should().Be(content.Length);

        using var buffer = new MemoryStream();
        await stored.Content.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(content);
    }

    /// <summary>Поток вызывающего кода не должен закрываться хранилищем: он может быть нужен дальше.</summary>
    [Fact]
    public async Task SaveLeavesCallerStreamUsable()
    {
        await using var source = new MemoryStream("данные"u8.ToArray());

        await minio.Storage.SaveAsync(source, NewKey(), "text/plain");

        source.CanRead.Should().BeTrue();
    }

    [Fact]
    public async Task MissingKeyReturnsNullInsteadOfThrowing()
    {
        (await minio.Storage.GetAsync(NewKey())).Should().BeNull();
        (await minio.Storage.ExistsAsync(NewKey())).Should().BeFalse();
    }

    [Fact]
    public async Task DeletedFileDisappears()
    {
        var key = NewKey();
        await minio.Storage.SaveAsync(new MemoryStream("данные"u8.ToArray()), key, "text/plain");

        (await minio.Storage.ExistsAsync(key)).Should().BeTrue();

        await minio.Storage.DeleteAsync(key);

        (await minio.Storage.ExistsAsync(key)).Should().BeFalse();
    }

    [Fact]
    public async Task PresignedUrlPointsAtTheObject()
    {
        var key = NewKey();
        await minio.Storage.SaveAsync(new MemoryStream("данные"u8.ToArray()), key, "text/plain");

        var url = await minio.Storage.GetUrlAsync(key, TimeSpan.FromMinutes(5));

        url.AbsolutePath.Should().EndWith(key);
        url.Query.Should().Contain("X-Amz-Signature");
    }

    [Fact]
    public async Task SameKeyIsOverwritten()
    {
        var key = NewKey();

        await minio.Storage.SaveAsync(new MemoryStream("первая версия"u8.ToArray()), key, "text/plain");
        await minio.Storage.SaveAsync(new MemoryStream("вторая"u8.ToArray()), key, "text/plain");

        await using var stored = await minio.Storage.GetAsync(key);
        using var reader = new StreamReader(stored!.Content);

        (await reader.ReadToEndAsync()).Should().Be("вторая");
    }

    private static string NewKey() =>
        MediaStorageKey.Create(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "запись.ogg");
}
