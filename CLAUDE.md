# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

LoreAI is a personal .NET 10 tool that auto-triages the backlog of the **"Non trié"** ("Unsorted", collection id `-1`) collection in Raindrop.io. Each cycle it learns the user's real collections/tags via the Raindrop API, classifies each new unsorted article with Claude Haiku against that learned taxonomy, and writes the result straight back — no human-in-the-loop validation step. Everything outside "Non trié" is considered already sorted by the user and is never touched.

**Start here: read [`docs/etat-des-lieux.md`](docs/etat-des-lieux.md) first.** It is short and deliberately kept current: which lot is in progress, the next concrete step, what is blocked. Reading it is cheaper and more reliable than re-deriving the state from the repo. Where the project is heading is in [`docs/roadmap.md`](docs/roadmap.md); per-lot tracking lives in GitHub issues #41–#51, grouped under two milestones.

Read `docs/adr/` before making architectural changes — decisions and their rationale are recorded there, not duplicated here. Most relevant: [0007](docs/adr/0007-apprentissage-taxonomie-non-trie.md) (learned taxonomy, why manual validation was rejected), [0001](docs/adr/0001-architecture-generale.md) (why this stays a simple 3-project split, not Clean Architecture/CQRS/MediatR), [0008](docs/adr/0008-versioning-semver-conventional-commits.md) (SemVer versioning via Conventional Commits), [0009](docs/adr/0009-postgresql-mutualise-sur-le-pi.md) (self-hosted shared PostgreSQL instance on the Pi, not SQLite), [0011](docs/adr/0011-ef-core-remplace-dapper.md) (EF Core replaces Dapper for schema and queries) and [0012](docs/adr/0012-modele-item-generique-multi-sources.md) (generic `Item`/`ISourceIngester` model replaces `RaindropItem` as the pipeline's central type, ahead of multi-source ingestion).

## Commands

```bash
dotnet build LoreAI.slnx
dotnet test LoreAI.slnx
dotnet run --project src/LoreAI.Worker
```

Run a single test (xUnit.v3 on Microsoft.Testing.Platform, not `vstest`):

```bash
dotnet test LoreAI.slnx --filter-method "*GetNewRaindropsAsync_StopsAtAlreadyKnownItem*"
```

No real API keys are needed for tests: Raindrop/Anthropic/Discord calls are simulated with WireMock.Net (a real local HTTP server), and persistence uses a disposable PostgreSQL container per test collection (`Testcontainers.PostgreSql`) — **Docker is required to run `dotnet test`** (ADR 0009, ADR 0011).

Local dev config (`appsettings.Development.json`) points at the shared PostgreSQL instance on the Pi (reached via its Tailscale MagicDNS name, ADR 0010) and logs under `logs/`. Never commit real secrets there — use `dotnet user-secrets` or `.env` (see `.env.example`) instead.

Docker deployment target is Raspberry Pi 64-bit (arm64); `docker compose build && docker compose up -d`, built directly on the Pi since the `mcr.microsoft.com/dotnet/*` base images are multi-arch.

## Architecture

Three-project split, dependency direction strictly `Worker → Infrastructure → Core`:

- **`src/LoreAI.Core`** — models, enums, interfaces only. Zero external dependencies. This is the seam: every cross-cutting concern (Raindrop API, LLM, PostgreSQL, notifications) is an interface here (`IRaindropClient`, `IClassifier`, `IArticleRepository`, `IPollingStateRepository`, `IImmediateNotifier`, `IDigestNotifier`, `INotificationPolicy`) with a single concrete implementation in `Infrastructure`. Swapping a library (e.g. Anthropic → another LLM provider) means writing a new `IClassifier` implementation, nothing else. The pipeline's central model is the source-agnostic `Item` (ADR 0012), not a Raindrop-specific type; `IRaindropClient : ISourceIngester` is its first (and currently only) implementation — a future Feed/Newsletter ingester would add another, without touching the classifier or persistence contracts.
- **`src/LoreAI.Infrastructure`** — concrete implementations, grouped by concern: `Raindrop/` (API client + DTOs), `Classification/` (Anthropic tool-use call + prompt building + response parsing), `Persistence/` (EF Core + `Npgsql.EntityFrameworkCore.PostgreSQL`, generated migrations), `Notifications/` (Discord immediate alert, email digest via MailKit).
- **`src/LoreAI.Worker`** — Generic Host that wires everything up in `Program.cs` and runs two Coravel-scheduled jobs (`Services/UnsortedClassificationJob.cs`, `Services/DigestNotificationJob.cs`).

### Per-cycle flow (`UnsortedClassificationJob`, driven by `Worker__PollingCronExpression`, default every 15 min)

1. `IRaindropClient.GetNewItemsAsync` (the `ISourceIngester` member) pages `GET /rest/v1/raindrops/{collectionId}` (default `-1` = Non trié) against the stored per-source `PollingState` high-water mark (last known item id/created date), oldest-first, stopping once a known item or a short page is hit, and maps each result to a generic `Item`. There is no webhook API (see ADR 0003) — this is pure polling.
2. `IRaindropClient.GetTaxonomyAsync` re-learns the real taxonomy every cycle: `GET /collections` + `/collections/childrens` (merged) for collections, `GET /tags` for tags with usage counts. There is no fixed `Category` enum — the taxonomy is fully dynamic (ADR 0007 superseded the fixed taxonomy from ADR 0004).
3. For each new item, `IClassifier.ClassifyAsync` (→ `AnthropicClassifier`) calls Claude Haiku via raw `HttpClient` + tool-use forced (`tool_choice: {"type":"tool","name":"classify"}`), with an `input_schema` built per-call from the current taxonomy (`ClassificationPromptBuilder`). `ClassificationResponseParser` defensively re-validates the "structured" output and falls back to `ClassificationResult.Fallback` on parse failure — an article is never silently dropped.
4. The result is persisted via `IArticleRepository.UpsertAsync` (raw LLM response kept for audit/debug), then applied to Raindrop:
   - Tags are **always** merged into the item's existing tags (case-insensitive, additive, never lost).
   - The item is **moved** only if `SuggestedCollection` matches an existing collection title *exactly* (checked in code, not trusted from the LLM blindly) — otherwise it stays in Non trié with just the tags applied.
   - The existing note is appended to, never overwritten.
   - `Worker__WriteBackToRaindrop=false` disables all of the above (classify + persist + report only) — the one remaining safety switch since there's no per-item human approval.
5. If `INotificationPolicy.ShouldNotifyImmediately` (default: `Action == ATester && Priority == Haute`), `IImmediateNotifier` (Discord webhook) fires immediately; a send failure is logged but never aborts the batch.
6. `PollingState` is advanced to the last processed item regardless of individual write-back outcomes.
7. Exactly one `CycleRun` (`ICycleRunRepository`) is recorded per invocation, cycles with zero new items included — `Ok`/`Empty`/`Interrupted`/`Failed` outcome, item/tag/move/notification counts, failure reason. It's the only signal that the worker is actually alive; a failure to record it is logged but never fails the cycle.

`DigestNotificationJob` (driven by `Worker__DigestCronExpression`, default daily 07:00) sends everything with `EmailDigestSentAtUtc IS NULL` via `IDigestNotifier` (MailKit SMTP), grouped by collection then recommended action — a catch-all so nothing is ever missed even if it didn't trigger a Discord alert.

### Healthcheck

`dotnet LoreAI.Worker.dll --health-check` is a second mode of the same binary, run by Docker's `HEALTHCHECK` (the image is chiseled — no shell, no `curl`). `Program.cs` intercepts `--health-check` right after building the host but before `host.Run()`, so no option outside Postgres/`CycleRuns` needs to be valid for the probe to work. `HealthCheckMode.RunAsync` reads the 3 most recent `CycleRun`s and `Core.Services.HealthEvaluator.IsHealthy` (pure, no I/O) decides: unhealthy if the latest is older than `Worker__HealthMaxCycleAgeMinutes` (default 45) or if the last 3 all have `Outcome == Failed`. Docker Compose won't auto-restart on `unhealthy` (`restart: unless-stopped` only reacts to process death) — this gives visibility (e.g. in Portainer), not automatic recovery.

### First-run caveat

With no prior `PollingState` row, the first cycle backfills the *entire* history of "Non trié", which can mean a large volume of LLM calls and bulk in-place Raindrop mutations. To avoid this, seed `PollingState` manually before first start (see README for the exact `INSERT`).

## Conventions worth knowing

- `Directory.Packages.props` centralizes all NuGet versions (`ManagePackageVersionsCentrally`) — add versions there, not in individual `.csproj` files.
- `Directory.Build.props` sets `TreatWarningsAsErrors` solution-wide.
- Config keys follow .NET's `Section__Property` env-var convention (e.g. `Raindrop__Token`, `Worker__WriteBackToRaindrop`); see `.env.example` for the full list.
- Tests: xUnit.v3 (Microsoft.Testing.Platform runner) + NSubstitute for doubles + WireMock.Net for HTTP fakes (chosen over RichardSzalay.MockHttp for being actively maintained and exercising a real network stack).
- Versioning is fully automatic (ADR 0008): open a branch, commit, push, open a PR titled per [Conventional Commits](https://www.conventionalcommits.org/fr/v1.0.0/) (`feat:`, `fix:`, `chore:`, ... — validated by the Semantic PRs app, required to merge) and squash-merge it (the only merge method allowed on `main`). The `cd.yml` `release` job then runs [Versionize](https://github.com/versionize/versionize) to bump `<Version>` in `Directory.Build.props` (the single source of truth — no per-`.csproj` version), commit, tag (`vX.Y.Z`) and push straight to `main`; `build`/`docker`/`github-release` only run when that produced a real release. No `CHANGELOG.md` is committed — release notes live solely in GitHub Releases, generated from merged PRs.
