using Microsoft.Extensions.Options;
using MultiMessenger.Core.Storage;
using MultiMessenger.Infrastructure.Configuration;
using MultiMessenger.Infrastructure.Storage;
using Testcontainers.Minio;

namespace MultiMessenger.Tests.Storage;

public class MinioFixture : IAsyncLifetime
{
    private const string AccessKey = "test-access-key";
    private const string SecretKey = "test-secret-key";
    private const string BucketName = "multimessenger-media-tests";

    private readonly MinioContainer _container = new MinioBuilder("minio/minio")
        .WithUsername(AccessKey)
        .WithPassword(SecretKey)
        .Build();

    public IFileStorage Storage { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new MinioOptions
        {
            // Контейнер отдаёт адрес со схемой, а настройки приложения ждут host:port.
            Endpoint = new Uri(_container.GetConnectionString()).Authority,
            AccessKey = AccessKey,
            SecretKey = SecretKey,
            BucketName = BucketName,
            UseSsl = false,
        };

        var client = MinioSetup.CreateClient(options);
        await client.PutBucketAsync(BucketName);

        Storage = new MinioFileStorage(client, Options.Create(options));
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public class MinioCollection : ICollectionFixture<MinioFixture>
{
    public const string Name = "minio";
}
