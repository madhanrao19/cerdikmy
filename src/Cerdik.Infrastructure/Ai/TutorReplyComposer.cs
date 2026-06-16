using Cerdik.Application.Ai;
using Cerdik.Domain;

namespace Cerdik.Infrastructure.Ai;

/// <summary>Shared helpers for composing grounded tutor replies across providers:
/// builds the citation set from the retrieved context and estimates a mastery signal.
/// Citations always map to the chunks we actually grounded on, so the [n] markers in the
/// answer resolve to real, approved lesson content.</summary>
internal static class TutorReplyComposer
{
    public static IReadOnlyList<AiCitation> CitationsFromContext(TutorPrompt prompt) =>
        prompt.Context
            .Select(c => new AiCitation(c.ChunkId, c.LessonId, c.LessonTitle, Snippet(c.Content), c.Score))
            .ToList();

    public static string Snippet(string content, int max = 240) =>
        content.Length <= max ? content : content[..max].TrimEnd() + "…";

    /// <summary>Heuristic Tahap Penguasaan estimate from the conversation so far.
    /// Real providers may override via their structured output; this is the safe default.</summary>
    public static MasteryBand EstimateMastery(TutorPrompt prompt)
    {
        var studentTurns = prompt.History.Count(h => h.Role == TutorMessageRole.User) + 1;
        var q = prompt.StudentQuestion.ToLowerInvariant();
        var confused = q.Contains("don't") || q.Contains("tak faham") || q.Contains("confus") || q.Contains("help") || q.Contains("tolong");
        var advanced = q.Contains("why") || q.Contains("prove") || q.Contains("kenapa") || q.Contains("buktikan");

        var band = 3 + (advanced ? 1 : 0) - (confused ? 1 : 0) + (studentTurns >= 4 ? 1 : 0);
        band = Math.Clamp(band, 1, 6);
        return (MasteryBand)band;
    }

    public static IEnumerable<string> ChunkForStream(string text)
    {
        // Emit word-by-word deltas (keeps the SSE channel lively and is provider-agnostic).
        var words = text.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            yield return i == 0 ? words[i] : " " + words[i];
        }
    }
}
