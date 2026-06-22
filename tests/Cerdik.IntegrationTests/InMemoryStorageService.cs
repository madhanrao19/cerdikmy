using System.Collections.Concurrent;
using Cerdik.Application.Abstractions;

namespace Cerdik.IntegrationTests;

/// <summary>In-memory <see cref="IStorageService"/> for tests, so storage-touching endpoints (media
/// upload, lesson media URLs, privacy export) work without a real S3/MinIO/Azure backend.</summary>
public sealed class InMemoryStorageService : IStorageService
{
    public string Provider => "memory";

    private readonly ConcurrentDictionary<string, byte[]> _store = new();

    public async Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _store[key] = ms.ToArray();
        return key;
    }

    public Task<Stream> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream>(new MemoryStream(_store.TryGetValue(key, out var bytes) ? bytes : Array.Empty<byte>()));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry, bool forUpload = false, CancellationToken ct = default) =>
        Task.FromResult($"http://test.local/{key}");
}
