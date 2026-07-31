# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

RaindropAI is a personal .NET 9 tool that auto-triages the backlog of the **"Non trié"** ("Unsorted", collection id `-1`) collection in Raindrop.io. Each cycle it learns the user's real collections/tags via the Raindrop API, classifies each new unsorted article with Claude Haiku against that learned taxonomy, and writes the result straight back — no human-in-the-loop validation step. Everything outside "Non trié" is considered already sorted by the user and is never touched.

Read `docs/adr/` before making architectural changes — decisions and their rationale are recorded there, not duplicated here. Most relevant: [0007](docs/adr/0007-apprentissage-taxonomie-non-trie.md) (learned taxonomy, why manual validation was rejected) and [0001](docs/adr/0001-architecture-generale.md) (why this stays a simple 3-project split, not Clean Architecture/CQRS/MediatR).

## Commands

```bash
dotnet build RaindropAI.slnx
dotnet test RaindropAI.slnx
dotnet run --project src/RaindropAI.Worker
```

Run a single test (xUnit.v3 on Microsoft.Testing.Platform, not `vstest`):

```bash
dotnet test RaindropAI.slnx --filter-method "*GetNewRaindropsAsync_StopsAtAlreadyKnownItem*"
```

No real API keys are needed for tests: Raindrop/Anthropic/Discord calls are simulated with WireMock.Net (a real local HTTP server), and persistence uses a temp SQLite file per test.

Local dev config (`appsettings.Development.json`) points at a SQLite file `raindropai.dev.db` in the working directory and logs under `logs/`. Never commit real secrets there — use `dotnet user-secrets` or `.env` (see `.env.example`) instead.

Docker deployment target is Raspberry Pi 64-bit (arm64); `docker compose build && docker compose up -d`, built directly on the Pi since the `mcr.microsoft.com/dotnet/*` base images are multi-arch.

## Architecture

Three-project split, dependency direction strictly `Worker → Infrastructure → Core`:

- **`src/RaindropAI.Core`** — models, enums, interfaces only. Zero external dependencies. This is the seam: every cross-cutting concern (Raindrop API, LLM, SQLite, notifications) is an interface here (`IRaindropClient`, `IClassifier`, `IArticleRepository`, `IPollingStateRepository`, `IImmediateNotifier`, `IDigestNotifier`, `INotificationPolicy`) with a single concrete implementation in `Infrastructure`. Swapping a library (e.g. Anthropic → another LLM provider) means writing a new `IClassifier` implementation, nothing else.
- **`src/RaindropAI.Infrastructure`** — concrete implementations, grouped by concern: `Raindrop/` (API client + DTOs), `Classification/` (Anthropic tool-use call + prompt building + response parsing), `Persistence/` (Dapper + `Microsoft.Data.Sqlite`), `Notifications/` (Discord immediate alert, email digest via MailKit).
- **`src/RaindropAI.Worker`** — Generic Host that wires everything up in `Program.cs` and runs two Coravel-scheduled jobs (`Services/UnsortedClassificationJob.cs`, `Services/DigestNotificationJob.cs`).

### Per-cycle flow (`UnsortedClassificationJob`, driven by `Worker__PollingCronExpression`, default every 15 min)

1. `IRaindropClient.GetNewRaindropsAsync` pages `GET /rest/v1/raindrops/{collectionId}` (default `-1` = Non trié) against the stored `PollingState` high-water mark (last known raindrop id/created date), oldest-first, stopping once a known item or a short page is hit. There is no webhook API (see ADR 0003) — this is pure polling.
2. `IRaindropClient.GetTaxonomyAsync` re-learns the real taxonomy every cycle: `GET /collections` + `/collections/childrens` (merged) for collections, `GET /tags` for tags with usage counts. There is no fixed `Category` enum — the taxonomy is fully dynamic (ADR 0007 superseded the fixed taxonomy from ADR 0004).
3. For each new item, `IClassifier.ClassifyAsync` (→ `AnthropicClassifier`) calls Claude Haiku via raw `HttpClient` + tool-use forced (`tool_choice: {"type":"tool","name":"classify"}`), with an `input_schema` built per-call from the current taxonomy (`ClassificationPromptBuilder`). `ClassificationResponseParser` defensively re-validates the "structured" output and falls back to `ClassificationResult.Fallback` on parse failure — an article is never silently dropped.
4. The result is persisted via `IArticleRepository.UpsertAsync` (raw LLM response kept for audit/debug), then applied to Raindrop:
   - Tags are **always** merged into the item's existing tags (case-insensitive, additive, never lost).
   - The item is **moved** only if `SuggestedCollection` matches an existing collection title *exactly* (checked in code, not trusted from the LLM blindly) — otherwise it stays in Non trié with just the tags applied.
   - The existing note is appended to, never overwritten.
   - `Worker__WriteBackToRaindrop=false` disables all of the above (classify + persist + report only) — the one remaining safety switch since there's no per-item human approval.
5. If `INotificationPolicy.ShouldNotifyImmediately` (default: `Action == ATester && Priority == Haute`), `IImmediateNotifier` (Discord webhook) fires immediately; a send failure is logged but never aborts the batch.
6. `PollingState` is advanced to the last processed item regardless of individual write-back outcomes.

`DigestNotificationJob` (driven by `Worker__DigestCronExpression`, default daily 07:00) sends everything with `EmailDigestSentAtUtc IS NULL` via `IDigestNotifier` (MailKit SMTP), grouped by collection then recommended action — a catch-all so nothing is ever missed even if it didn't trigger a Discord alert.

### First-run caveat

With no prior `PollingState` row, the first cycle backfills the *entire* history of "Non trié", which can mean a large volume of LLM calls and bulk in-place Raindrop mutations. To avoid this, seed `PollingState` manually before first start (see README for the exact `INSERT`).

## Conventions worth knowing

- `Directory.Packages.props` centralizes all NuGet versions (`ManagePackageVersionsCentrally`) — add versions there, not in individual `.csproj` files.
- `Directory.Build.props` sets `TreatWarningsAsErrors` solution-wide.
- Config keys follow .NET's `Section__Property` env-var convention (e.g. `Raindrop__Token`, `Worker__WriteBackToRaindrop`); see `.env.example` for the full list.
- Tests: xUnit.v3 (Microsoft.Testing.Platform runner) + NSubstitute for doubles + WireMock.Net for HTTP fakes (chosen over RichardSzalay.MockHttp for being actively maintained and exercising a real network stack).
