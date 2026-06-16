# AI tutor

cerdikMY's tutor is a **grounded, age-safe** assistant for Malaysian school
children. Every reply is retrieved from an **original, review-approved lesson
corpus** (never copyrighted KPM textbook content), wrapped in age-appropriate
system prompts, screened by two-stage moderation, and returned as a structured
envelope the Blazor UI can render with inline citations and a mastery signal.

This document covers the provider contract, the RAG pipeline, moderation,
the age-safe prompts, provider switching, and a worked example.

> See also: [architecture.md](architecture.md) for the full request sequence
> diagram and entity model, and [privacy-and-safety.md](privacy-and-safety.md)
> for the safety-review and escalation policy.

---

## 1. The provider contract — `IAiProvider`

All vendor SDKs live behind a single interface
(`src/Cerdik.Application/Abstractions/IAiProvider.cs`). The rest of the app never
references OpenAI / Azure OpenAI / Anthropic directly.

```csharp
public interface IAiProvider
{
    string Name { get; }

    Task<TutorReply>            GenerateTutorReplyAsync(TutorPrompt prompt, CancellationToken ct = default);
    IAsyncEnumerable<TutorStreamChunk> StreamTutorReplyAsync(TutorPrompt prompt, CancellationToken ct = default);
    Task<RiskClassification>    ClassifyRiskAsync(string text, CancellationToken ct = default);
    Task<PracticeSet>           GeneratePracticeSetAsync(PracticeRequest request, CancellationToken ct = default);
    Task<ProgressSummary>       SummarizeProgressAsync(ProgressSummaryRequest request, CancellationToken ct = default);
}
```

| Method | Purpose |
| --- | --- |
| `GenerateTutorReplyAsync` | One-shot grounded reply (non-streaming `POST /tutor/sessions/{id}/messages`). |
| `StreamTutorReplyAsync` | Token-by-token reply for SSE delivery; the final `TutorStreamChunk.Final` carries the structured payload. |
| `ClassifyRiskAsync` | Safety/escalation classifier used by both moderation stages (pre + post). |
| `GeneratePracticeSetAsync` | Original, KPM-aligned practice questions for a learning standard / subject variant. |
| `SummarizeProgressAsync` | Parent-friendly narrative + recommendations from a student's mastery roll-up. |

Embeddings are a **separate** interface (`IEmbeddingProvider`) so indexing can
use a cheaper model than chat. Provider resolution goes through
`IAiProviderFactory.Resolve(name?)`.

### Structured reply envelope

`TutorReply` (`src/Cerdik.Application/Ai/AiModels.cs`) is the canonical shape, and
`SystemPrompts.OutputContract` instructs the model to emit exactly this JSON:

```jsonc
{
  "answer_markdown": "string (markdown with [n] citation markers)",
  "citations": [
    { "ref": 1, "chunk_id": "guid", "lesson_id": "guid", "snippet": "string" }
  ],
  "mastery_signal": 1,        // TP1..TP6 (Tahap Penguasaan) or null
  "needs_review": false       // true => a trusted adult should review the exchange
}
```

In code this maps to:

| JSON field | `TutorReply` member | Type |
| --- | --- | --- |
| `answer_markdown` | `AnswerMarkdown` | `string` |
| `citations[]` | `Citations` | `IReadOnlyList<AiCitation>` |
| `mastery_signal` | `MasterySignal` | `MasteryBand?` (`TP1`=1 … `TP6`=6) |
| `needs_review` | `NeedsReview` | `bool` |

Each `AiCitation(ChunkId, LessonId, LessonTitle, Snippet, Score)` traces back to
the `EmbeddingChunk` it was grounded on, persisted as a `Citation` row linked to
the `TutorMessage`.

---

## 2. RAG pipeline

### 2.1 Indexing (seeded lessons → embedding chunks)

Approved, original lessons are chunked and embedded into `EmbeddingChunk` rows by
a Hangfire job in `src/Cerdik.Worker`. Each chunk carries the curriculum-scoping
columns used at retrieval time plus the embedding itself:

- `Embedding` — stored in SQL Server's native **`VECTOR(N)`** column.
- `EmbeddingJson` — a portable JSON fallback for instances/providers without the
  native type; in-app cosine similarity is computed from this when the native
  vector index is unavailable.
- Filter columns: `CurriculumVersionCode`, `SubjectId`, `SchoolType`,
  `Language`, `DlpMode`, `Approved`, plus `LessonId`, `ChunkIndex`, `Dimensions`.

Only chunks with `Approved = true` are ever retrieved, so unreviewed content can
never reach a child.

### 2.2 Retrieval (filter → top-k cosine → grounding)

`IVectorRetriever.RetrieveAsync` builds a filtered, top-k cosine query. The
filter mirrors the curriculum context of the `TutorSession`:

```
curriculum_version  +  school_type  +  language  +  dlp_mode  +  subject  +  Approved=true
        │
        ▼  (longest-match index IX_EmbeddingChunk_Filter)
   candidate chunks
        │
        ▼  cosine(query_embedding, Embedding)   — native VECTOR index (diskann, cosine)
        │                                          or in-app cosine over EmbeddingJson
        ▼
   top-K RetrievedChunk[]  ->  numbered grounding passages [1], [2], ...
```

The retrieved passages become the `Context` on the `TutorPrompt`, numbered so the
model can cite them as `[1]`, `[2]`, etc.

### 2.3 Grounding + generation

The provider receives a `TutorPrompt`:

```csharp
public sealed record TutorPrompt
{
    public required string StudentQuestion { get; init; }
    public required Level Level { get; init; }
    public required Language Language { get; init; }
    public string SubjectName { get; init; } = "General";
    public IReadOnlyList<TutorTurn> History { get; init; }
    public IReadOnlyList<RetrievedChunk> Context { get; init; }  // numbered grounding
    public string SystemPrompt { get; init; }                    // SystemPrompts.TutorSystem(...)
}
```

The system prompt forbids answering outside the provided context ("If the context
does not contain the answer, say you are not sure … never invent facts"), so the
tutor degrades to "I'm not sure, let's review the lesson" rather than
hallucinating. The reply is parsed into `TutorReply` and persisted as a
`TutorMessage` plus `Citation` rows; `ProgressRecord` is updated with the mastery
signal (EWMA → Tahap Penguasaan).

---

## 3. Two-stage moderation

Every turn is screened **before** generation (the child's question) and **after**
generation (the model's answer). Both stages call `ClassifyRiskAsync`, which
returns a `RiskClassification(Risk, Categories, RequiresEscalation, Reason)` using
the `RiskClassifier` prompt. `IModerationService` maps that into a
`ModerationOutcome(Decision, Risk, Categories, RaiseIntervention, Reason)`.

| Stage (`ModerationStage`) | When | Decision (`ModerationDecision`) |
| --- | --- | --- |
| `PreGeneration` | On the student's incoming question | `Allow` / `Flag` / `Block` / `Escalate` |
| `PostGeneration` | On the model's drafted answer | `Allow` / `Flag` / `Block` / `Escalate` |
| `ManualReport` | When a human reports a message | (recorded for the reviewer queue) |

`RiskLevel` ranges `None → Low → Medium → High → Critical`. Categories include
`self_harm`, `violence`, `sexual`, `harassment`, `personal_data`, `off_topic`,
`distress`, `none`.

**Outcomes:**

- `Allow` / `Flag` — proceed (a `Flag` is logged but not blocking).
- `Block` — the answer is suppressed and a safe redirect is returned.
- `Escalate` / `RaiseIntervention` — a `ModerationEvent` is written with
  `InterventionRaised = true`, the `TutorSession.NeedsReview` flag is set, and the
  exchange enters the **SafetyReviewer** queue. Distress or signs the child may be
  in danger always escalate (`High`/`Critical`).

A `ModerationEvent` records `Stage`, `Decision`, `Risk`, `Categories`, `Reason`,
`InterventionRaised`, and (once worked) `ReviewedByUserId`, `ReviewedAt`,
`ReviewNotes`.

### Single tutor turn (sequence)

```mermaid
sequenceDiagram
    participant S as Student
    participant API as Cerdik.Api
    participant MP as Moderation (pre)
    participant VR as VectorRetriever
    participant AI as IAiProvider
    participant MO as Moderation (post)
    participant DB as SQL Server

    S->>API: question (SSE stream request)
    API->>MP: ClassifyRiskAsync(question)  [PreGeneration]
    alt Block / Escalate
        MP-->>API: RaiseIntervention
        API->>DB: ModerationEvent(InterventionRaised=true), NeedsReview=true
        API-->>S: safe redirect (needs_review=true)
        Note over API,DB: SafetyReviewer queue notified
    else Allow / Flag
        VR-->>API: top-K RetrievedChunk[] (curriculum-filtered cosine)
        API->>AI: StreamTutorReplyAsync(prompt + grounding + system prompt)
        loop SSE tokens
            AI-->>API: TutorStreamChunk(DeltaMarkdown)
            API-->>S: data: delta
        end
        AI-->>API: Final TutorReply(answer, citations, mastery, needs_review)
        API->>MO: ClassifyRiskAsync(answer)  [PostGeneration]
        MO-->>API: Allow / Flag / Block / Escalate
        API->>DB: TutorMessage + Citations; ProgressRecord (mastery EWMA)
        API-->>S: SSE final event (TutorReplyDto)
    end
```

---

## 4. Age-safe system prompts

Prompts live in code (`src/Cerdik.Application/Ai/SystemPrompts.cs`) and are
**versioned** (`SystemPrompts.Version`) so SafetyReviewers can diff and approve
changes. They never embed copyrighted textbook content.

`TutorSystem(Level level, Language language, string subjectName)` composes three
parts:

1. **Persona by level** — Preschool (playful, 5–6 yr), Primary (step-by-step,
   7–12, everyday Malaysian examples), LowerSecondary (Form 1–3, proper
   terminology), UpperSecondary (Form 4–5, exam skills).
2. **Language line** — respond in BM / EN / ZH / TA (Tamil) unless the child
   writes in another language; ZH/TA are gated behind the `lang.*` feature flags.
3. **`SafetyCore` + `OutputContract`** — non-negotiable guardrails (guide rather
   than hand answers, use only provided context, cite `[n]`, refuse unsafe/adult/
   self-harm/personal-data requests, set `needs_review` on distress) plus the JSON
   output contract.

Other prompt builders: `RiskClassifier` (moderation), `PracticeGenerator`
(original questions only — "Do NOT copy from any textbook"), and
`ProgressSummarizer` (parent narrative).

---

## 5. Provider switching

The provider is selected by the **`AI_PROVIDER`** environment variable, resolved
through `IAiProviderFactory`:

| `AI_PROVIDER` | Adapter | Key env vars |
| --- | --- | --- |
| `openai` | OpenAI | `OPENAI_API_KEY`, `OPENAI_CHAT_MODEL` (`gpt-4o-mini`), `OPENAI_EMBED_MODEL` (`text-embedding-3-small`) |
| `azureopenai` | Azure OpenAI | `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY`, `AZURE_OPENAI_DEPLOYMENT` |
| `anthropic` | Anthropic | `ANTHROPIC_API_KEY`, `ANTHROPIC_CHAT_MODEL` (`claude-sonnet-4-6`) |
| `mock` | Deterministic in-process stub | none — for local dev / tests |

Set `AI_PROVIDER=mock` for offline development and CI: it returns deterministic,
grounded-looking replies with no API keys and no network calls. Switching
providers requires **no code change** — just the env var and the relevant keys.

---

## 6. Worked example (seeded conversation)

With the demo seed loaded (Primary Year 1 Mathematics, SK variant, language BM),
the student `aisyah@cerdik.my` asks:

> **Aisyah:** Macam mana nak tolak 7 − 3?

Retrieval filters `EmbeddingChunk` by `curriculum_version = KSSR`,
`school_type = SK`, `language = BM`, `dlp_mode = none`, `subject = Mathematics`
and returns the top-k chunks from the seeded subtraction lesson. The provider
returns:

```jsonc
{
  "answer_markdown": "Bagus, Aisyah! Untuk **7 − 3**, mula dengan 7 jari. Lipat 3 jari turun, satu demi satu. Kira jari yang tinggal: 1, 2, 3, 4 — jadi **7 − 3 = 4** [1]. Cuba pula 8 − 2 sendiri ya? [2]",
  "citations": [
    { "ref": 1, "chunk_id": "0190f3a1-...-1", "lesson_id": "0190f3a0-...-a", "snippet": "Tolak bermaksud mengeluarkan. Kira berapa yang tinggal." },
    { "ref": 2, "chunk_id": "0190f3a1-...-2", "lesson_id": "0190f3a0-...-a", "snippet": "Latihan: gunakan jari atau objek untuk menolak nombor kecil." }
  ],
  "mastery_signal": 2,
  "needs_review": false
}
```

In the Blazor UI this renders as the markdown answer with `[1]`/`[2]` as
clickable citation chips linking to the seeded **Mathematics — SK** lesson, a
**TP2** mastery badge feeding `ProgressRecord`, and no review flag. Pre- and
post-moderation both returned `Allow`, so no `ModerationEvent` was raised. Had
Aisyah expressed distress, `needs_review` would be `true` and the turn would be
routed to the SafetyReviewer queue.
