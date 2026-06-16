using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Cerdik.Application.Abstractions;
using Cerdik.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Cerdik.Infrastructure.Storage;

/// <summary>Azure Blob Storage adapter for production.</summary>
public sealed class AzureBlobStorageService : IStorageService
{
    private readonly BlobContainerClient _container;

    public string Provider => "azure";

    public AzureBlobStorageService(IOptions<StorageOptions> opt)
    {
        var o = opt.Value;
        var service = new BlobServiceClient(o.AzureConnectionString);
        _container = service.GetBlobContainerClient(o.Bucket);
    }

    public async Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        var blob = _container.GetBlobClient(key);
        await blob.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        return key;
    }

    public async Task<Stream> GetAsync(string key, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(key);
        var response = await blob.DownloadStreamingAsync(cancellationToken: ct);
        return response.Value.Content;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);

    public Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry, bool forUpload = false, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(key);
        if (!blob.CanGenerateSasUri)
        {
            // Account key not available (e.g. managed identity). Return the blob URI; callers should
            // front it with a SAS-issuing endpoint or a CDN in that deployment mode.
            return Task.FromResult(blob.Uri.ToString());
        }

        var permissions = forUpload ? BlobSasPermissions.Write | BlobSasPermissions.Create : BlobSasPermissions.Read;
        var uri = blob.GenerateSasUri(permissions, DateTimeOffset.UtcNow.Add(expiry));
        return Task.FromResult(uri.ToString());
    }
}
