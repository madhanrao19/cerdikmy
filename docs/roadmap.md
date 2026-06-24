# cerdikMY — Roadmap & enhancement status

This tracks the post-MVP enhancement program: what shipped, what must happen
before a production launch, and what remains on the backlog (with implementation
notes for the items that need an external asset, credential, or larger effort).

## Shipped

### Production-readiness (first wave)
UI localization (BM/EN/ZH/TA), real model-based moderation + clean tutor output,
transactional email + password reset, media upload (endpoint + admin UI),
expanded tests, observability (metrics, AI meter/spans, correlation IDs),
distributed rate-limiting + account lockout, CSP/security headers + self-hosted
fonts, global soft-delete query filters, mobile bottom-nav.

### Learning & engagement
- **Mock-exam mode** — timed papers, auto-submit, Malaysian letter grades, per-standard analytics, history.
- **Diagnostic placement** — per-subject quiz that seeds an initial mastery baseline.
- **Adaptive recommendations** — "what to do next" ranked by need (continue / review / new).
- **Spaced-repetition review** — band-based interval review queue.
- **Per-standard mastery gap map** — mastery vs KPM target band per learning standard, with remediation links.
- **Streaks & daily goals** on the student home.
- **Certificates of achievement** for passed mock exams (printable).
- **Read-aloud (TTS)** on lessons and tutor replies (Web Speech API).

### Family / trust
- **Parent visibility into AI tutor chats** (flagged-first).
- **Notifications** — weekly parent digest + high-risk safety alerts (Hangfire jobs).
- **Predictive insights** — projected grade, trend, at-risk status on the report.
- **Printable report card** — mastery, exams, streak, outlook.

### Platform / monetization / a11y
- **PWA** — installable + offline fallback + cached shell.
- **Promo / gift codes** — admin-created discount codes, validated at checkout, redeemed atomically at payment success.
- **Accessibility** — dynamic `<html lang>`, keyboard skip link, localized landmarks.
- **Quiz UI fix** — true/false now uses the question's own (localized) options.
- Localized the student pages, tutor chat, billing page, and report.

## Pre-launch hand-offs (must do before production)

These can't be completed from the build sandbox (no .NET SDK / blocked asset hosts):

1. **EF migrations** — generate and commit the initial migration before first
   prod deploy: `./scripts/generate-initial-migration.sh` on a machine with the
   .NET 10 SDK. The app already prefers `Migrate()` once migrations exist; the
   migration will capture every table (incl. `ExamAttempts`, `PromoCodes`, and
   the `ModerationEvent.GuardianNotifiedAt` / `Subscription.PromoCode` columns
   added by the enhancement work). See `infra/hostinger/README.md`.
2. **Secrets** — run `./scripts/generate-secrets.sh`, keep
   `ALLOW_DEV_DEFAULT_SECRETS=false` and `SEED_DEMO_DATA=false`, set the
   `BOOTSTRAP_ADMIN_*` first-admin credentials.
3. **Self-hosted font binaries** — drop the Public Sans `.woff2` files into
   `wwwroot` per the README (CDN removed for CSP).
4. **PWA icons** — SVG icons cover Android/desktop install; add PNG
   `apple-touch-icon` (180/192/512) for iOS home-screen polish.

## Backlog (not yet built)

### Blocked here (need an external asset / credential / capability)
- **Google / DELIMa SSO** — add an `IExternalIdentityValidator` abstraction;
  real impl validates the Google ID token (via `Microsoft.IdentityModel` against
  Google's JWKS, or `Google.Apis.Auth`), gated by `GOOGLE_CLIENT_ID` (+ optional
  hosted-domain for DELIMa). Endpoint `/auth/google` finds-or-creates the user
  and issues the app JWT (reuse the login path). Frontend needs Google Identity
  Services JS + CSP `script-src`/`connect-src` for `accounts.google.com`, and a
  real OAuth client id. Testable via a mock validator.
- **Math rendering** — vendor KaTeX (JS/CSS/fonts) into `wwwroot` (asset hosts
  are blocked here), render `$...$` in lessons/tutor.
- **"Snap a question" tutor** — needs an AI vision-capable provider; extend
  `IAiProvider` with an image input and wire the existing media upload.

### Larger, multi-PR efforts
- **Classroom / co-op + teacher role + messaging** (Tier 3) — builds on the
  existing `Organization` tenant: group enrolment, teacher-assigned content,
  group analytics, parent–teacher messaging.
- **Content marketplace**, **SCORM/LTI/H5P** import, **public API** (API keys),
  **native mobile apps** (or Capacitor over the PWA).

### Smaller follow-ups
- Localize the **admin pages** (analytics/users/content/curriculum/media/
  moderation/payments — still English).
- Finish the **Ordering** question type (currently rendered as single-choice).
- Admin **UI** for managing promo codes (create/list works via the API today).
- Fuller **WCAG** pass — contrast audit, dyslexia-friendly font option,
  remaining page-level aria-labels.
- **Family-timezone** day boundaries for streaks (currently UTC).
