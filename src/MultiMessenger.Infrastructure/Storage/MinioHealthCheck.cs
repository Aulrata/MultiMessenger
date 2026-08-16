using Amazon.S3;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MultiMessenger.Infrastructure.Configuration;

namespace MultiMessenger.Infrastructure.Storage;

/// <summary>
/// Проверяет, что бакет вложений доступен. Именно бакет, а не просто сетевая
/// доступность хранилища: приложение без бакета формально живо, но принять
/// входящее медиа не сможет.
/// </summary>
public class MinioHealthCheck(IAmazonS3 s3Client, IOptions<MinioOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var bucketName = options.Value.BucketName;

        try
        {
            await s3Client.GetBucketLocationAsync(bucketName, cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (AmazonS3Exception exception)
        {
            return HealthCheckResult.Unhealthy($"Бакет {bucketName} недоступен", exception);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Нет связи с хранилищем", exception);
        }
    }
}
