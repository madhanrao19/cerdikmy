using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cerdik.Application.Abstractions;
using Cerdik.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Cerdik.Infrastructure.Ai;

/// <summary>Deterministic, dependency-free embedding using feature hashing of word n-grams.
/// Lets the platform index and retrieve lesson content offline (no API key, reproducible tests).
/// Quality is intentionally modest; swap in a real provider for production-grade semantics.</summary>
public sealed class LocalEmbeddingProvider : IEmbeddingProvider
{
    private readonly int _dims;

    public LocalEmbeddingProvider(IOptions<AiOptions> opt) => _dims = Math.Max(64, opt.Value.EmbeddingDimensions);

    public string Model => "local-hash-v1";
    public int Dimensions => _dims;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Embed(text));

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());

    private float[] Embed(string text)
    {
        var vec = new float[_dims];
        var tokens = Tokenize(text);
        for (var i = 0; i < tokens.Count; i++)
        {
            AddToken(vec, tokens[i], 1f);
            if (i + 1 < tokens.Count)
            {
                AddToken(vec, tokens[i] + " " + tokens[i + 1], 0.5f); // bigram
            }
        }
        Normalize(vec);
        return vec;
    }

    private void AddToken(float[] vec, string token, float weight)
    {
        var hash = BitConverter.ToInt32(SHA1.HashData(Encoding.UTF8.GetBytes(token)), 0);
        var bucket = (int)((uint)hash % (uint)_dims);
        var sign = (hash & 1) == 0 ? 1f : -1f;
        vec[bucket] += sign * weight;
    }

    private static List<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split([' ', '\n', '\t', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '"', '\'', '-', '/'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1)
            .ToList();

    private static void Normalize(float[] vec)
    {
        double sum = 0;
        foreach (var v in vec) sum += v * (double)v;
        var norm = (float)Math.Sqrt(sum);
        if (norm <= 1e-8f) return;
        for (var i = 0; i < vec.Length; i++) vec[i] /= norm;
    }
}

/// <summary>OpenAI / Azure OpenAI embeddings over HTTP.</summary>
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly AiOptions _opt;
    private readonly bool _azure;

    public OpenAiEmbeddingProvider(HttpClient http, IOptions<AiOptions> opt, bool azure = false)
    {
        _opt = opt.Value;
        _azure = azure;
        _http = http;
    }

    public string Model => _azure ? _opt.AzureDeployment : _opt.OpenAiEmbedModel;
    public int Dimensions => _opt.EmbeddingDimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var batch = await EmbedBatchAsync([text], ct);
        return batch[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        using var request = BuildRequest(texts);
        using var resp = await _http.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var data = doc.RootElement.GetProperty("data");
        var result = new List<float[]>(data.GetArrayLength());
        foreach (var item in data.EnumerateArray())
        {
            var emb = item.GetProperty("embedding").EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
            result.Add(emb);
        }
        return result;
    }

    private HttpRequestMessage BuildRequest(IReadOnlyList<string> texts)
    {
        if (_azure)
        {
            var url = $"{_opt.AzureEndpoint.TrimEnd('/')}/openai/deployments/{_opt.AzureDeployment}/embeddings?api-version=2024-06-01";
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { input = texts }) };
            req.Headers.Add("api-key", _opt.AzureApiKey);
            return req;
        }
        var oreq = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
        {
            Content = JsonContent.Create(new { model = _opt.OpenAiEmbedModel, input = texts }),
        };
        oreq.Headers.Authorization = new("Bearer", _opt.OpenAiApiKey);
        return oreq;
    }
}
