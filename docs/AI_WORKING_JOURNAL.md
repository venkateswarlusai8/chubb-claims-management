# AI Working Journal

A running log of how AI tooling was directed: what was asked, accepted, challenged and overridden.
Sections marked **[Candidate]** are for my own review notes. They are deliberately left for me to fill in honestly.

---

## Session 1: initial prototype (before this repo had git)

1. Requirements decomposition: extracted claimant and claims-officer journeys, status lifecycle, workload and exposure from the brief. Accepted those as the smallest end-to-end slice. Deferred complex multi-market policy rules.
2. Architecture: chose Angular + ASP.NET Core API + EF Core. Chose a modular monolith over microservices because the assessment is time-boxed and domain boundaries are not yet validated.
3. Backend: generated a claim model, request contracts, EF context, service and REST controllers.
4. Frontend: generated one Angular component with claimant and officer views. The role selector is a demo affordance only.
5. Review: listed production gaps (authN/Z, audit history, uploads, notifications, Kafka outbox, tests, observability, concurrency, multi-currency liability).

---

## Session 2: Claude Code (Opus), 2026-11-02

### Prompts
- `/init` to analyse the prototype and write CLAUDE.md.
- "Check this document [the assessment brief], I need to implement on this."

### Decisions the AI asked me to make (my choices)
| Question | Options offered | Chosen |
|---|---|---|
| Kafka, given no Docker locally | outbox + switchable transport / Kafka required / docs only | **Outbox + switchable** (in-process default, Kafka via config, docker-compose provided) |
| Database | SQLite file / SQL Server LocalDB / keep in-memory | **SQLite with EF migrations** |
| Scope of this pass | deep backend + solid UI / backend only / minimal fixes | **Deep backend + solid UI** |
| Commits | AI commits as it goes / without co-author line / I commit | **AI commits as it goes** (I set my own git identity) |

### Problems the AI found in the session-1 prototype (accepted)
- The frontend called `http://localhost:5080` but the API launched on 59814/59815, so the app could not work out of the box.
- The frontend sent status as strings but the API had no `JsonStringEnumConverter`, so status updates would be rejected.
- Default in-memory DB lost all data on restart; there were no migrations.
- `PATCH /status` allowed any transition (e.g. Settled → Submitted), and claimant identity came from the request body.

### What was generated
- **Domain:**
  - `Claim` aggregate with state machine, assignment/ownership rules, audit history and domain events.
  - `InformationRequest`, `ClaimHistoryEntry`, `Notification`.
- **Persistence:** SQLite + EF Core migration. `SaveChanges` writes domain events to an outbox table in the same transaction.
- **Messaging:**
  - Outbox processor.
  - In-process publisher or Kafka producer (keyed by claim id) plus a Kafka consumer.
  - Idempotent notification handler (`ProcessedEvents`).
- **API:**
  - Explicit command endpoints, demo header auth producing a real `ClaimsPrincipal` with role attributes.
  - RFC 7807 errors with `code`, workload and exposure dashboards, OpenAPI with an auth button.
- **Tests:** 27 tests (domain lifecycle, HTTP lifecycle, authorisation/visibility, async notifications, idempotency, 409/400 contracts, exposure maths). All passing.
- **Frontend:**
  - Lazy, role-guarded areas; shared claim detail driven by `allowedActions`.
  - Officer queue with tabs, SLA highlighting and paging; dashboard; notifications with polling.

### Issues caught during the session (AI self-corrections, verified by build/test/run)
- The first API run crashed with "no such table: Claims". `dotnet ef migrations add` builds *before* scaffolding, so the `--no-build` run used a binary without the migration. Fixed by rebuilding. The lesson is noted in CLAUDE.md.
- xUnit `Assert.Equal([...], …)` with C# collection expressions doesn't compile (no natural type). Switched to `new[] { … }`.
- Angular route ordering: a `canMatch` guard that returns a redirect `UrlTree` does **not** fall through to the next route. Officers opening `/claims/:id` would have been bounced to their queue. The shared detail route was moved before the claimant-only list route.
- Claim-detail polling originally died after one failed request. Moved `catchError` inside the `switchMap`.
- The tool blocked deleting the old prototype files because nothing was committed yet. They were moved to a backup folder instead, so the git history starts at the redesign.

### Trade-offs to be ready to defend
- Liability uses claimant estimates as the reserve for undecided claims. It's a stated simplification, and the API returns the basis text.
- The dashboard aggregates in memory. That's fine for a prototype; at scale it would be a read model or SQL aggregation (SQLite also can't aggregate `decimal` server-side).
- Polling instead of SSE/SignalR on the frontend.
- TypeScript models are hand-mirrored from the OpenAPI contract, not generated.

### [Candidate] My review
- What I accepted after reading the code:
- What I challenged or changed:
- What I overrode and why:
- Anything I can't yet defend line by line (review before the walkthrough):

---

## Candidate verification log
- Date: 2026-11-02 and 2026-11-03 (sessions 3 and 4).
- .NET SDK / runtime: SDK 9.0.320 targeting net8.0, runtime 8.0.33.
- Node/npm: Node 26.12.1, npm 11.20.0.
- Backend restore/build/run: builds clean. `dotnet run` starts on :5080, applies the migration and seeds demo data; `/api/claims` and `/api/dashboard/exposure` returned data.
- `dotnet test` result: 27 passed, 0 failed (2026-11-02 and again 2026-11-03).
- Frontend npm install/build: `npm run build` succeeded (2026-11-02 and 2026-11-03).
- Fresh clone from GitHub (2026-11-02): `npm ci` and build succeeded, 27/27 tests passed (needs long paths on Windows; see README).
- Manual test cases: officer queue (James, Unassigned tab, 20-day claim flagged red) and team dashboard checked in the browser; every dashboard figure reconciled by hand. **Not yet run by hand:** the full README demo script (submit → pick up → request info → respond → approve → settle, plus a 403/409). Run it once before the walkthrough.
- Kafka transport: not run (no Docker installed). See ARCHITECTURE §4.
- Issues fixed: API not running caused UI 500s (no code change); favicon 404; truncated queue search placeholder; wrong `models.ts` path in ARCHITECTURE; outdated `Technical_Walkthrough.docx` removed; README long-path note.
- Git commit hashes: history starts at `fb09ff4` (scaffold). Run `git log --oneline` for the full list.

---

## Session 3: Claude Code (Opus), 2026-11-02 (review against the brief)

### Prompts
- "Check my assessment against the brief, I need to complete this."
- Shared screenshots of the queue and dashboard: "check my complete is working right."
- "How to login and commit code" (GitHub account venkateswarlusai8).

### What the AI checked (verified by running, not assumed)
- `dotnet test`: 27 passed, 0 failed. `npm run build`: succeeded.
- Started the API and queried `/api/claims` and the exposure dashboard: both returned data.
- Reconciled every dashboard figure (status counts, workload, ageing/SLA, liability by market and USD totals). All consistent. SLA count and the 15-30 day bucket use the same integer age rule.

### Issues found
- **No git commits existed.** Session 2 planned for the AI to commit as it went, but nothing was committed. The history below was committed in logical chunks at the end of session 3, with real timestamps (nothing backdated), so it shows build order, not the time each piece was written.
- UI showed 500s on `/api` calls: the API was not running (the dev-server proxy returns 500 when :5080 is down). Not a code bug.
- `favicon.ico` 404: added an empty inline icon to `index.html`.
- Queue search placeholder was truncated: shortened it and added a tooltip.

### Later in session 3
- Pushed the history to GitHub (`venkateswarlusai8/chubb-claims-management`).
- **Removed `docs/Technical_Walkthrough.docx`.** It described the session-1 prototype (InMemory/SQL Server, `PATCH /status`, Kafka "not implemented", "has not been compiled or run") and contradicted the actual code and the other docs. `WALKTHROUGH.md` replaces it; the old file stays in git history.
- Checked every factual claim in `ARCHITECTURE.md` against the code (events, rules, endpoints, error codes, Kafka settings, poll intervals, liability formula, FX). All matched except one wrong file path, which was fixed.
- Fresh clone from GitHub: 27/27 tests passed and the frontend built. The first clone into a deep Windows folder failed with "Filename too long" on the migration file, so the API had no tables and 10 tests failed. Added a long-path note to the README.
- Visual Studio created a duplicate `backend/Claims.Api/Claims.Api.sln`. Not committed; `backend/Claims.sln` is the solution to open.

---

## Session 4: Claude Code (Opus), 2026-11-03 (documentation gaps)

### Prompt
- "Check this project what else pending... check the document part, if required prepare any missing document."

### Gaps found against the brief's walkthrough format, and what was added
- **Kafka verification wasn't stated plainly.** Docker isn't installed and no test covers the Kafka producer or consumer, so that path has never run against a broker. The docs implied this but never said it. Added an explicit verification status to ARCHITECTURE §4, a Q&A answer in WALKTHROUGH, and made "prove the Kafka path" the first next step in ARCHITECTURE §9.
- **No system diagram for the presentation.** Added a Mermaid diagram to ARCHITECTURE §3 (UI → REST → modules → SQLite/outbox → in-process or Kafka → notifications).
- **"Shortcuts under time pressure" were scattered.** Added one table in WALKTHROUGH, each row checked in the code (e.g. `Claim.CanView` lets any officer see any market; poison Kafka messages are logged and skipped).
- **No prep for the "Your questions" segment.** Added suggested questions, marked as suggestions.
- Restructured WALKTHROUGH around the brief's four timed segments, with a presentation timing plan and demo fallbacks.

### Left for the candidate
- [Candidate] My review (above) and [Candidate] What I'd do differently (WALKTHROUGH §3).
- Total hours spent. The AI had written an unverified time split in ARCHITECTURE §1 ("65% backend, 25% frontend, 10% docs"); it was replaced with measured code size (about 60% backend and tests), because the AI cannot know how the time was spent.
- Filled the verification log from checks the AI actually ran, and deleted the duplicate `Claims.Api.sln`.
