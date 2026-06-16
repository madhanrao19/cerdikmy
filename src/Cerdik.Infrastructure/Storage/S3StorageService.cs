using Amazon.S3;
using Amazon.S3.Model;
using Cerdik.Application.Abstractions;
using Cerdik.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Cerdik.Infrastructure.Storage;

/// <summary>S3-compatible storage (MinIO in dev, AWS S3 in prod).</summary>
public sealed class S3StorageService : IStorageService, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly StorageOptions _opt;

    public string Provider => "s3";

    public S3StorageService(IOptions<StorageOptions> opt)
    {
        _opt = opt.Value;
        var config = new AmazonS3Config
        {
            ServiceURL = _opt.S3Endpoint,
            ForcePathStyle = _opt.ForcePathStyle, // MinIO requires path-style addressing
            AuthenticationRegion = _opt.S3Region,
        };
        _client = new AmazonS3Client(_opt.S3AccessKey, _opt.S3SecretKey, config);
    }

    public async Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _opt.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };
        await _client.PutObjectAsync(request, ct);
        return key;
    }

    public async Task<Stream> GetAsync(string key, CancellationToken ct = default)
    {
        var response = await _client.GetObjectAsync(_opt.Bucket, key, ct);
        return response.ResponseStream;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _client.DeleteObjectAsync(_opt.Bucket, key, ct);

    public Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry, bool forUpload = false, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _opt.Bucket,
            Key = key,
            Verb = forUpload ? HttpVerb.PUT : HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry),
        };
        return Task.FromResult(_client.GetPreSignedURL(request));
    }

    public void Dispose() => _client.Dispose();
}
