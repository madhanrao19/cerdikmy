# cerdikMY

**cerdikMY** is a family-first, Malaysia KPM-aligned homeschooling platform covering
preschool, primary, lower-secondary and upper-secondary. It pairs original,
standards-mapped lessons with an age-safe, grounded AI tutor, mastery tracking
(Tahap Penguasaan / TP1–TP6), parent dashboards and PDPA-aligned privacy controls.

It is a **.NET 10 on-prem monorepo** (solution `CerdikMY.sln`) — not a Node/pnpm
workspace. Everything runs self-hosted on SQL Server with no hard dependency on
Redis or any managed queue.

> ⚖️ **Legal / content note.** cerdikMY contains **no copyrighted KPM textbook
> content**. We model the *structure* of the curriculum (subjects, learning
> standards, mastery bands) and ship **original placeholder lessons** mapped to
> those standards. Learning standards are described in original phrasing; we never
> reproduce protected textbook passages. The AI tutor is grounded only on this
> original, review-approved corpus.

---

## Features

- **KPM-aligned curriculum model** — `CurriculumVersion` → `Subject` →
  `SubjectVariant` (keyed by school type, language and DLP mode) → `Lesson` →
  `LessonBlock` / `Activity`, with `LearningStandard` codes and `MasteryBand`
  (TP1–TP6) targets.
- **Curriculum dimensions** — level (preschool / primary / lower_secondary /
  upper_secondary), school type (SK, SJKC, SJKT, SMK, SMKA, SABK, homeschool,
  private), language (BM, EN, ZH, TA, other) and DLP mode (none, bilingual,
  dlp_subject_variant).
- **Grounded AI tutor** — provider-agnostic `IAiProvider` (OpenAI / Azure OpenAI /
  Anthropic / mock) with RAG retrieval over an embedded lesson corpus
  (`EmbeddingChunk`), structured replies (`answer_markdown`, citations, mastery
  signal, needs_review) and **streaming via SSE**.
- **Two-stage moderation** — pre- and post-generation safety screening with
  intervention/escalation flagging and a `SafetyReviewer` review queue.
- **Mastery & progress** — `Attempt` grading rolls up into `ProgressRecord`
  (EWMA mastery + Tahap Penguasaan), badges, heatmaps and parent dashboards.
- **Family accounts** — `Organization` → `Household` → guardians + `Student`
  learners, with guardian-managed student logins and weekly `StudyPlan`s.
- **Billing** — Malaysia-first payments (Billplz / Curlec) plus Stripe, via
  `IPaymentProvider`, with subscriptions, invoices and webhook reconciliation.
- **Privacy & safety** — PDPA-aligned consent capture, data export and
  delete/anonymize requests (soft-delete via `DeletedAt`), and append-only
  audit logging.
- **Background jobs** — Hangfire on SQL Server storage (embedding indexing,
  export bundle generation, anonymization, billing reconciliation).
- **Observability** — OpenTelemetry (ASP.NET Core / HTTP / EF Core instrumentation)
  with OTLP export, plus Serilog.

---

## Stack

| Concern | Technology |
| --- | --- |
| Runtime | .NET 10 (`global.json`-pinned SDK) |
| API | ASP.NET Core 10 Web API (`src/Cerdik.Api`), minimal hosting + controllers |
| Auth | JWT access tokens + rotating refresh tokens in httpOnly cookies; RBAC roles Parent, Student, Admin, ContentAdmin, SafetyReviewer |
| Frontend | Blazor Web App (`src/Cerdik.Web`) |
| ORM / DB | EF Core 10 on SQL Server 2025 |
| Vector search | SQL Server native `VECTOR` type for embeddings (VARBINARY/JSON fallback + in-app cosine) |
| Background jobs | Hangfire with SQL Server storage (`src/Cerdik.Worker`) — no Redis/BullMQ |
| Storage | S3-compatible (MinIO in dev) via `AWSSDK.S3`; Azure Blob adapter for prod, behind `IStorageService` |
| AI | Provider-agnostic `IAiProvider` (OpenAI / Azure OpenAI / Anthropic / mock); streaming tutor replies via SSE |
| Observability | OpenTelemetry + OTLP exporter; Serilog |
| Payments | Billplz / Curlec / Stripe behind `IPaymentProvider` |
| Dev infra | Docker Compose (mssql, minio, mailpit, api, web, worker) |
| Prod infra | Hostinger VPS (docker compose + nginx) and Azure (Bicep + Container Apps + Azure SQL) |

---

## Repository layout

```
cerdikmy/
├── CerdikMY.sln
├── global.json                      # pins the .NET 10 SDK
├── Directory.Build.props            # shared MSBuild settings
├── Directory.Packages.props         # central NuGet package versions
├── .env.example                     # copy to .env for Docker Compose
├── README.md
├── src/
│   ├── Cerdik.Domain/               # entities, enums, base types (no dependencies)
│   │   ├── Common/BaseEntity.cs
│   │   ├── Entities/                # Identity, Curriculum, Content, Progress, Tutor, Billing, Operations
│   │   └── Enums.cs
│   ├── Cerdik.Application/          # DTOs, abstractions, AI contracts, feature flags
│   │   ├── Abstractions/            # IAiProvider, IStorageService, IVectorRetriever, IModerationService, ...
│   │   ├── Ai/                      # AiModels, SystemPrompts
│   │   ├── Common/                  # Result, PagedResult, PageRequest
│   │   ├── Dtos/                    # Auth, Curriculum, Learning, Tutor, AdminBilling DTOs
│   │   └── Features/                # FeatureFlags
│   ├── Cerdik.Infrastructure/       # EF Core DbContext, providers, storage, payment + AI adapters
│   ├── Cerdik.Api/                  # ASP.NET Core 10 Web API (controllers, auth, SSE)
│   ├── Cerdik.Web/                  # Blazor Web App
│   └── Cerdik.Worker/               # Hangfire jobs host
├── tests/                           # xUnit unit + integration tests (Testcontainers.MsSql, Playwright)
├── infra/
│   ├── docker/                      # docker-compose.yml + Dockerfiles (dev/all-in-one)
│   ├── hostinger/                   # deploy.sh, nginx.conf (VPS deployment)
│   └── azure/                       # main.bicep, deploy.azcli.sh (Container Apps + Azure SQL)
└── docs/                            # this documentation set
```

> Some `src/` projects (Infrastructure, Api, Web, Worker) and `infra/` assets are
> referenced throughout the docs as the canonical structure; the Domain and
> Application projects above are the currently committed source of truth for the
> entity, DTO and abstraction names used in this documentation.

---

## Prerequisites

- **.NET 10 SDK** (version pinned in `global.json`).
- **Docker** + Docker Compose (for the one-command dev stack).
- Optionally **SQL Server 2025** locally if you run outside Docker.

---

## Quickstart (one command, Docker)

```bash
# 1. Configure environment
cp .env.example .env          # edit secrets as needed; dev defaults work out of the box

# 2. Bring up the whole stack (mssql, minio, mailpit, api, web, worker)
docker compose -f infra/docker/docker-compose.yml up -d --build
```

On first run the **API auto-applies EF Core migrations and seeds** the database
(curriculum versions, school profiles, original placeholder lessons + activities,
embedded chunks for RAG, and the demo accounts below). No manual migration step
is required.

### Default URLs

| Service | URL |
| --- | --- |
| Blazor Web app | http://localhost:5080 |
| API | http://localhost:5081 |
| Hangfire dashboard | http://localhost:5081/hangfire |
| Mailpit (dev mail UI) | http://localhost:8025 |
| MinIO console | http://localhost:9001 (user `minioadmin` / pass `minioadmin`) |

### Seeded demo accounts

| Role | Email | Password |
| --- | --- | --- |
| Parent | `parent.demo@cerdik.my` | `Demo!2345` |
| Admin | `admin@cerdik.my` | `Admin!2345` |
| Student | `aisyah@cerdik.my` | `Student!2345` |

The student login (`aisyah@cerdik.my`) is a guardian-managed account linked to
the demo parent's household (`User.StudentId` points at the seeded `Student`).

### Seeded demo curriculum

The seed loads **original, KPM-aligned placeholder lessons** (never copyrighted
textbook content) across levels, school types, languages and DLP modes:

| Level | Subject / variant |
| --- | --- |
| Preschool | Preschool sample module |
| Primary (Year 1) | Mathematics — SK, SJKC and SJKT variants |
| Primary (Year 1) | English |
| Lower Secondary (Form 1) | Science |
| Upper Secondary (Form 4) | Mathematics |
| DLP | Science and Mathematics DLP subject variants (gated behind `dlp.*` flags) |

Each lesson is chunked into `EmbeddingChunk` rows (embedded for RAG), so the AI
tutor can answer grounded in this corpus out of the box.

---

## Local development (without Docker)

Provision a SQL Server 2025 instance and point `DATABASE_URL` at it (see
`.env.example`), then restore and run each project:

```bash
# Restore the solution (pulls central NuGet versions from Directory.Packages.props)
dotnet restore CerdikMY.sln

# API (auto-migrates + seeds on startup)
dotnet run --project src/Cerdik.Api          # -> http://localhost:5081

# Blazor Web app
dotnet run --project src/Cerdik.Web          # -> http://localhost:5080

# Hangfire worker
dotnet run --project src/Cerdik.Worker
```

The API also exposes explicit one-off flags so you can run migration and seeding
deterministically (e.g. in CI or a Hostinger/Azure release step) instead of
relying on startup auto-apply:

```bash
# Apply EF Core migrations and exit
dotnet run --project src/Cerdik.Api -- --migrate

# Seed the demo curriculum + accounts and exit
dotnet run --project src/Cerdik.Api -- --seed
```

To use the mock AI provider (no API keys, deterministic replies for local dev),
set `AI_PROVIDER=mock` in your `.env` / environment.

---

## Testing

The `tests/` projects use xUnit. Integration tests spin up SQL Server via
`Testcontainers.MsSql`; the E2E project drives the Blazor app with Playwright.

```bash
# Run everything
dotnet test CerdikMY.sln

# Run a single suite
dotnet test tests/Cerdik.UnitTests
dotnet test tests/Cerdik.IntegrationTests
dotnet test tests/Cerdik.E2E
```

---

## Documentation

- [docs/architecture.md](docs/architecture.md) — layered architecture, ER &
  sequence diagrams, RBAC, feature flags, initial SQL indexes.
- [docs/api.md](docs/api.md) — full REST endpoint reference, auth model, error
  envelope, pagination, example curls.
- [docs/ai-tutor.md](docs/ai-tutor.md) — `IAiProvider`, RAG pipeline, two-stage
  moderation, age-safe prompts, provider switching, worked example.
- [docs/deployment-hostinger.md](docs/deployment-hostinger.md) — deploy to a
  Hostinger Ubuntu VPS (nginx + certbot + backups + hardening).
- [docs/deployment-azure.md](docs/deployment-azure.md) — deploy to Azure
  Container Apps + Azure SQL + Blob Storage via Bicep.
- [docs/privacy-and-safety.md](docs/privacy-and-safety.md) — PDPA consent, export
  & delete/anonymize flows, child safety, audit logging, copyright rule.
- [docs/production-readiness.md](docs/production-readiness.md) — rate limiting,
  health checks, security headers, fail-fast config, and the pre-production checklist
  (migrations, dependency audit, secrets, backups, monitoring, scaling).
