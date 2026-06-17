using System.Text.Json;
using Cerdik.Application.Abstractions;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cerdik.Infrastructure.Jobs;

/// <summary>Builds the RAG corpus: splits an approved lesson's text blocks into chunks, embeds them,
/// and upserts <see cref="EmbeddingChunk"/> rows carrying the curriculum retrieval filters.
/// Only lessons in a published variant become Approved (retrievable).</summary>
public sealed class ContentIndexer
{
    private const int TargetChunkChars = 700;
    private readonly AppDbContext _db;
    private readonly IEmbeddingProvider _embeddings;
    private readonly ILogger<ContentIndexer> _log;

    public ContentIndexer(AppDbContext db, IEmbeddingProvider embeddings, ILogger<ContentIndexer> log)
    {
        _db = db;
        _embeddings = embeddings;
        _log = log;
    }

    public async Task<int> IndexLessonAsync(Guid lessonId, CancellationToken ct = default)
    {
        var lesson = await _db.Lessons
            .Include(l => l.Blocks)
            .Include(l => l.SubjectVariant).ThenInclude(v => v.Subject).ThenInclude(s => s.CurriculumVersion)
            .FirstOrDefaultAsync(l => l.Id == lessonId, ct);
        if (lesson is null) return 0;

        var variant = lesson.SubjectVariant;
        var subject = variant.Subject;
        var approved = lesson.State == PublishState.Published && variant.State == PublishState.Published;

        // Remove any prior chunks for an idempotent re-index.
        var existing = await _db.EmbeddingChunks.Where(c => c.LessonId == lessonId).ToListAsync(ct);
        if (existing.Count > 0) _db.EmbeddingChunks.RemoveRange(existing);

        var chunks = BuildChunks(lesson).ToList();
        var embeddings = await _embeddings.EmbedBatchAsync(chunks, ct);

        for (var i = 0; i < chunks.Count; i++)
        {
            var vec = embeddings[i];
            _db.EmbeddingChunks.Add(new EmbeddingChunk
            {
                LessonId = lesson.Id,
                CurriculumVersionCode = subject.CurriculumVersion.Code,
                SubjectId = subject.Id,
                SchoolType = variant.SchoolType,
                Language = variant.Language,
                DlpMode = variant.DlpMode,
                ChunkIndex = i,
                Content = chunks[i],
                TokenCount = chunks[i].Length / 4,
                EmbeddingJson = JsonSerializer.Serialize(vec),
                Dimensions = vec.Length,
                Approved = approved,
            });
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Indexed {Count} chunks for lesson {LessonId} (approved={Approved})", chunks.Count, lessonId, approved);
        return chunks.Count;
    }

    /// <summary>Re-index every published lesson (scheduled / triggered after content publish).</summary>
    public async Task<int> ReindexAllAsync(CancellationToken ct = default)
    {
        var ids = await _db.Lessons.Where(l => l.State == PublishState.Published).Select(l => l.Id).ToListAsync(ct);
        var total = 0;
        foreach (var id in ids) total += await IndexLessonAsync(id, ct);
        return total;
    }

    private static IEnumerable<string> BuildChunks(Lesson lesson)
    {
        var sources = new List<string> { $"{lesson.Title}. {lesson.Summary}" };
        sources.AddRange(lesson.Blocks
            .OrderBy(b => b.SortOrder)
            .Where(b => !string.IsNullOrWhiteSpace(b.Markdown))
            .Select(b => b.Markdown!.Trim()));

        var buffer = string.Empty;
        foreach (var src in sources)
        {
            foreach (var sentence in SplitSentences(src))
            {
                if (buffer.Length + sentence.Length > TargetChunkChars && buffer.Length > 0)
                {
                    yield return buffer.Trim();
                    buffer = string.Empty;
                }
                buffer += sentence + " ";
            }
            if (buffer.Length > 0)
            {
                yield return buffer.Trim();
                buffer = string.Empty;
            }
        }
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        var current = string.Empty;
        foreach (var ch in text)
        {
            current += ch;
            if (ch is '.' or '!' or '?' or '\n')
            {
                if (current.Trim().Length > 0) yield return current.Trim();
                current = string.Empty;
            }
        }
        if (current.Trim().Length > 0) yield return current.Trim();
    }
}
