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
- Date/time:
- .NET SDK / runtime: (session 2 used SDK 9.0.320 targeting net8.0, runtime 8.0.33)
- Node/npm: (session 2: Node 26.12.1)
- Backend restore/build/run:
- `dotnet test` result:
- Frontend npm install/build:
- Manual test cases (follow README demo script):
- Issues fixed:
- Git commit hashes:

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
