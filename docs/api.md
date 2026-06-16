# API Reference

Base URL (dev): `http://localhost:5081`. All routes are served by
`src/Cerdik.Api` (ASP.NET Core 10 Web API). Request/response bodies are JSON;
DTO shapes match `src/Cerdik.Application/Dtos/`.

## Authentication model

- **Access token** — short-lived JWT (default 15 min, `JWT_ACCESS_TTL_MINUTES`).
  Returned in the login/register/refresh response body as `accessToken` and sent
  on subsequent requests as `Authorization: Bearer <token>`.
- **Refresh token** — opaque, rotating, stored **hashed** server-side
  (`RefreshToken.TokenHash`) and delivered to the browser as an **httpOnly,
  Secure, SameSite cookie**. `POST /auth/refresh` validates the cookie, rotates
  the token (old one marked `ReplacedByTokenHash`) and issues a new access token.
  `POST /auth/logout` revokes the active refresh token and clears the cookie.
- **Roles** — `Parent`, `Student`, `Admin`, `ContentAdmin`, `SafetyReviewer`
  (see `UserRole`). Resolved per request via `ICurrentUser`.

Tokens are also tenant-scoped: the JWT carries `OrganizationId` and (for student
logins) `StudentId`, enforced on every `ITenantScoped` query.

## Error envelope

All non-2xx responses use a consistent envelope (mirrors `Result<T>.Fail`):

```json
{ "error": "Human-readable message.", "code": "machine_code" }
```

Common codes: `unauthorized`, `forbidden`, `not_found`, `validation_error`,
`conflict`, `moderation_blocked`, `error`. Validation failures (FluentValidation)
return `400` with `code: "validation_error"`.

## Pagination

List endpoints accept `?page=` and `?pageSize=` query params (`PageRequest`:
default page 1, pageSize 20, clamped 1–200; optional `search`). Responses are a
`PagedResult<T>`:

```json
{ "items": [ ... ], "total": 137, "page": 1, "pageSize": 20 }
```

---

## Auth & identity

| Method | Path | Auth / Role | Request body | Response |
| --- | --- | --- | --- | --- |
| POST | `/auth/register-parent` | Public | `RegisterParentRequest` | `AuthResponse` (+ refresh cookie) |
| POST | `/auth/register-student` | Parent | `RegisterStudentRequest` | `AuthResponse` or `StudentSummaryDto` |
| POST | `/auth/login` | Public | `LoginRequest` | `AuthResponse` (+ refresh cookie) |
| POST | `/auth/refresh` | Refresh cookie | _none_ | `AuthResponse` (rotated cookie) |
| POST | `/auth/logout` | Bearer | _none_ | `204 No Content` |
| GET | `/me` | Bearer | — | `MeResponse` |

- `RegisterParentRequest`: `{ email, password, fullName, householdName, preferredLanguage }`
- `RegisterStudentRequest`: `{ householdId, displayName, email?, password?, level, schoolType, primaryLanguage, dlpMode, dateOfBirth? }`
- `LoginRequest`: `{ email, password }`
- `AuthResponse`: `{ user: UserDto, accessToken, accessExpiresAt }`
- `MeResponse`: `{ user, students: StudentSummaryDto[], features: { "lang.zh": false, ... } }`

## Curriculum & content

| Method | Path | Auth / Role | Request | Response |
| --- | --- | --- | --- | --- |
| GET | `/curriculum/versions` | Bearer | — | `CurriculumVersionDto[]` |
| GET | `/school-profiles` | Bearer | — | `SchoolProfileDto[]` |
| GET | `/subjects` | Bearer | `?curriculumVersionCode=&level=&schoolType=&language=&dlpMode=` (`CurriculumFilter`) | `SubjectDto[]` (with `Variants`) |
| GET | `/subjects/{id}/standards` | Bearer | — | `LearningStandardDto[]` |
| GET | `/lessons/{id}` | Bearer | — | `LessonDto` (blocks + activities) |

## Learning & progress

| Method | Path | Auth / Role | Request | Response |
| --- | --- | --- | --- | --- |
| POST | `/activities/{id}/start` | Student / Parent | `StartActivityRequest` `{ studentId }` | `AttemptDto` |
| POST | `/attempts/{id}/submit` | Student / Parent | `SubmitAttemptRequest` `{ answers: { qId: value } }` | `AttemptResultDto` |
| GET | `/students/{id}/progress` | Student (self) / Parent | — | `ProgressDto` |
| GET | `/parents/dashboard` | Parent | — | `ParentDashboardDto` |
| POST | `/parents/study-plans` | Parent | `StudyPlanRequest` | `StudyPlanDto` |

- `AttemptResultDto` includes per-question grading (`QuestionResultDto[]`) and the
  derived `TahapPenguasaan` (`MasteryBand`).
- `ProgressDto` returns overall mastery, per-subject breakdown, a daily heatmap
  and badges.

## AI tutor

| Method | Path | Auth / Role | Request | Response |
| --- | --- | --- | --- | --- |
| POST | `/tutor/sessions` | Student / Parent | `CreateTutorSessionRequest` | `TutorSessionDto` |
| POST | `/tutor/sessions/{id}/messages` | Student / Parent | `SendTutorMessageRequest` `{ content }` | `TutorReplyDto` |
| POST | `/tutor/sessions/{id}/messages/stream` | Student / Parent | `SendTutorMessageRequest` | **SSE** stream of deltas, final event = `TutorReplyDto` |

- `CreateTutorSessionRequest`: `{ studentId, subjectVariantId?, lessonId?, title? }`.
  The session captures `curriculumVersionCode`, `schoolType`, `language`, `dlpMode`
  for reproducible RAG.
- The SSE endpoint sets `Content-Type: text/event-stream`; each `data:` frame is a
  `TutorStreamChunk` delta; the terminal frame carries the structured
  `TutorReplyDto` (`answerMarkdown`, `citations[]`, `masterySignal`, `needsReview`,
  `risk`). See [docs/ai-tutor.md](ai-tutor.md).

## Admin

| Method | Path | Auth / Role | Request | Response |
| --- | --- | --- | --- | --- |
| GET | `/admin/users` | Admin | `?page=&pageSize=&search=` | `PagedResult<AdminUserDto>` |
| POST | `/admin/users` | Admin | `CreateAdminUserRequest` | `AdminUserDto` |
| GET | `/admin/content` | Admin / ContentAdmin | `?page=&pageSize=&search=` | `PagedResult<AdminContentItemDto>` |
| POST | `/admin/content/import` | ContentAdmin | `ImportContentRequest` | `AdminContentItemDto` |
| POST | `/admin/content/publish` | ContentAdmin | `PublishContentRequest` `{ lessonId, publish }` | `AdminContentItemDto` |
| GET | `/analytics/cohorts` | Admin | — | `CohortAnalyticsDto` |

- `ImportContentRequest`: `{ subjectVariantId, title, summary, learningStandardCode?, blocks: ImportBlock[] }`.
  Imported content is **original placeholder material only** (see copyright rule).

## Billing & webhooks

| Method | Path | Auth / Role | Request | Response |
| --- | --- | --- | --- | --- |
| POST | `/billing/checkout-session` | Parent | `CheckoutSessionRequest` `{ householdId, planCode, returnUrl }` | `CheckoutSessionDto` |
| POST | `/webhooks/payments/{provider}` | Public (signature-verified) | raw provider payload | `200 OK` |

`{provider}` is one of `billplz`, `curlec`, `stripe`. Webhooks are verified via
`IPaymentProvider.HandleWebhookAsync` (signature + normalization) before a
`Payment` row is recorded.

## Privacy

| Method | Path | Auth / Role | Request | Response |
| --- | --- | --- | --- | --- |
| POST | `/privacy/export` | Parent / Student | `PrivacyExportRequest` `{ studentId? }` | `PrivacyRequestDto` |
| POST | `/privacy/delete-request` | Parent / Student | `PrivacyDeleteRequest` `{ studentId?, reason? }` | `PrivacyRequestDto` |

See [docs/privacy-and-safety.md](privacy-and-safety.md) for the full
export/delete/anonymize lifecycle.

---

## Example: register a parent

```bash
curl -i -X POST http://localhost:5081/auth/register-parent \
  -H "Content-Type: application/json" \
  -c cookies.txt \
  -d '{
        "email": "parent.demo@cerdik.my",
        "password": "Demo!2345",
        "fullName": "Demo Parent",
        "householdName": "Demo Household",
        "preferredLanguage": "BM"
      }'
```

## Example: login

```bash
curl -s -X POST http://localhost:5081/auth/login \
  -H "Content-Type: application/json" \
  -c cookies.txt \
  -d '{ "email": "parent.demo@cerdik.my", "password": "Demo!2345" }'
# -> { "user": {...}, "accessToken": "eyJ...", "accessExpiresAt": "..." }
# the rotating refresh token is stored in the httpOnly cookie in cookies.txt
```

## Example: open a tutor session and ask a question

```bash
ACCESS="eyJ..."   # accessToken from /auth/login

# 1. create a session
SESSION=$(curl -s -X POST http://localhost:5081/tutor/sessions \
  -H "Authorization: Bearer $ACCESS" \
  -H "Content-Type: application/json" \
  -d '{ "studentId": "<student-guid>", "subjectVariantId": "<variant-guid>", "title": "Pecahan" }')

SID=$(echo "$SESSION" | jq -r .id)

# 2a. non-streaming reply
curl -s -X POST "http://localhost:5081/tutor/sessions/$SID/messages" \
  -H "Authorization: Bearer $ACCESS" \
  -H "Content-Type: application/json" \
  -d '{ "content": "Apa itu pecahan setara?" }'

# 2b. streaming reply (SSE)
curl -N -X POST "http://localhost:5081/tutor/sessions/$SID/messages/stream" \
  -H "Authorization: Bearer $ACCESS" \
  -H "Content-Type: application/json" \
  -H "Accept: text/event-stream" \
  -d '{ "content": "Apa itu pecahan setara?" }'
```
