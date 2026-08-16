using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiMessenger.Infrastructure.Configuration;

namespace MultiMessenger.Infrastructure.Storage;

public static class MinioSetup
{
    public static IAmazonS3 CreateClient(MinioOptions options)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = $"{(options.UseSsl ? "https" : "http")}://{options.Endpoint}",

            // MinIO не поддерживает виртуальный хостинг бакетов по поддомену:
            // адрес должен выглядеть как endpoint/bucket/key.
            ForcePathStyle = true,

            // Регион S3-совместимому хранилищу не нужен, но клиент AWS без него
            // отказывается подписывать запрос.
            AuthenticationRegion = "us-east-1",
        };

        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }

    /// <summary>
    /// Создаёт бакет для вложений, если его ещё нет. Делается на старте приложения,
    /// а не отдельным сервисом в docker-compose: тогда это работает одинаково
    /// и локально, и на сервере, и при переезде на внешний S3.
    /// </summary>
    public static async Task EnsureMediaBucketAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var s3Client = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<MinioOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MinioSetup));

        var buckets = await s3Client.ListBucketsAsync(cancellationToken);

        if (buckets.Buckets?.Any(bucket => bucket.BucketName == options.BucketName) is true)
        {
            logger.LogInformation("Бакет {Bucket} на месте", options.BucketName);
            return;
        }

        await s3Client.PutBucketAsync(new PutBucketRequest { BucketName = options.BucketName }, cancellationToken);

        logger.LogWarning("Создан бакет {Bucket} для вложений", options.BucketName);
    }
}
