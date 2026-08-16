using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using MultiMessenger.Core.Storage;
using MultiMessenger.Infrastructure.Configuration;

namespace MultiMessenger.Infrastructure.Storage;

public class MinioFileStorage(IAmazonS3 s3Client, IOptions<MinioOptions> options) : IFileStorage
{
    private readonly MinioOptions _options = options.Value;

    public async Task SaveAsync(
        Stream content,
        string key,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        await s3Client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                // Поток принадлежит вызывающему коду: он мог получить его из HTTP-запроса
                // и собираться использовать дальше.
                AutoCloseStream = false,
            },
            cancellationToken);
    }

    public async Task<StoredFile?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await s3Client.GetObjectAsync(_options.BucketName, key, cancellationToken);

            return new StoredFile(
                response.ResponseStream,
                response.Headers.ContentType ?? "application/octet-stream",
                response.ContentLength);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        s3Client.DeleteObjectAsync(_options.BucketName, key, cancellationToken);

    public Task<Uri> GetUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var url = s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(lifetime),
            Protocol = _options.UseSsl ? Protocol.HTTPS : Protocol.HTTP,
        });

        return Task.FromResult(new Uri(url));
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await s3Client.GetObjectMetadataAsync(_options.BucketName, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
