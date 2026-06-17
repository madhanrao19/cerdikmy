using System.Text.Json;
using Cerdik.Application.Abstractions;
using Cerdik.Application.Ai;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Infrastructure.Ai;

/// <summary>Retrieval over the approved lesson-chunk corpus, filtered by curriculum context
/// (curriculum_version + subject + school_type + language + dlp_mode) and ranked by cosine similarity.
///
/// Ranking is computed in-process over the embedding vectors. For large corpora, SQL Server 2025's
/// native VECTOR column + VECTOR_DISTANCE ANN index can be used instead; see
/// <c>Migrations/Sql/0002_vector_index.sql</c> and the commented native query below. The in-process
/// path keeps the platform DB-portable (incl. tests) and is more than adequate for the seeded corpus.</summary>
public sealed class VectorRetriever : IVectorRetriever
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingProvider _embeddings;

    public VectorRetriever(AppDbContext db, IEmbeddingProvider embeddings)
    {
        _db = db;
        _embeddings = embeddings;
    }

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default)
    {
        var queryVec = await _embeddings.EmbedAsync(query.QueryText, ct);

        // Candidate filter pushed to SQL via the IX_EmbeddingChunk_RetrievalFilter index.
        var candidates = await _db.EmbeddingChunks
            .AsNoTracking()
            .Where(c => c.Approved
                        && c.CurriculumVersionCode == query.CurriculumVersionCode
                        && c.SubjectId == query.SubjectId
                        && c.SchoolType == query.SchoolType
                        && c.Language == query.Language
                        && c.DlpMode == query.DlpMode)
            .Select(c => new ChunkRow(c.Id, c.LessonId, c.Lesson.Title, c.Content, c.EmbeddingJson))
            .ToListAsync(ct);

        // If the strict filter is empty (e.g. a DLP variant not yet authored), relax DLP/language
        // so the tutor still has *some* grounded context rather than hallucinating.
        if (candidates.Count == 0)
        {
            candidates = await _db.EmbeddingChunks
                .AsNoTracking()
                .Where(c => c.Approved
                            && c.CurriculumVersionCode == query.CurriculumVersionCode
                            && c.SubjectId == query.SubjectId)
                .Select(c => new ChunkRow(c.Id, c.LessonId, c.Lesson.Title, c.Content, c.EmbeddingJson))
                .ToListAsync(ct);
        }

        var ranked = candidates
            .Select(c => new RetrievedChunk(c.Id, c.LessonId, c.LessonTitle, c.Content, Cosine(queryVec, Deserialize(c.EmbeddingJson))))
            .Where(r => r.Score >= query.MinScore)
            .OrderByDescending(r => r.Score)
            .Take(query.TopK)
            .ToList();

        return ranked;
    }

    private static float[] Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<float[]>(json) ?? Array.Empty<float>();
        }
        catch
        {
            return Array.Empty<float>();
        }
    }

    internal static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
        }
        if (na <= 1e-12 || nb <= 1e-12) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private readonly record struct ChunkRow(Guid Id, Guid LessonId, string LessonTitle, string Content, string EmbeddingJson);
}
