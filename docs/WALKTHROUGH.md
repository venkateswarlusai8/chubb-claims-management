# Walkthrough notes (30–60 minutes)

Organised by the brief's four segments. Before the session: start the API (`cd backend/Claims.Api`, `dotnet run`) and the UI (`cd frontend/claims-ui`, `npm start`). Delete `backend/Claims.Api/claims.db*` first for fresh seed data. Have Swagger (`http://localhost:5080/swagger`) and `docs/ARCHITECTURE.md` open in tabs.

---

## 1. Presentation: architecture, decisions, demo (15–20 min)

| Time | Topic | Show |
|---|---|---|
| 2 min | **Problem:** fragmented intake; claimants have no visibility; staff work from inboxes; managers can't see exposure. | — |
| 3 min | **Prioritisation:** backend first (domain rules, outbox, tests), then a UI covering both journeys. What I left out, and why. | ARCHITECTURE §1 |
| 4 min | **Architecture:** modular monolith (Claims, Reporting, Notifications); REST for commands and queries; events via a transactional outbox to in-process handlers or Kafka. | ARCHITECTURE §3 diagram, §4 table |
| 3 min | **Domain:** Claim aggregate and state machine; assignment and ownership rules; audit history; `allowedActions` as the contract that keeps the UI thin. | `Domain/Claim.cs`, ARCHITECTURE §2 |
| 6 min | **Demo:** follow the demo script in README.md. Show the async notification arriving, a 403/409 error response, and the dashboard. | Browser, Swagger |
| 1 min | **Tests:** `dotnet test`. Domain rules plus HTTP-level lifecycle, authorisation and idempotency tests (27). | Terminal |

Demo fallbacks: if the UI misbehaves, run the same flow in Swagger (**Authorize** with `claimant-daniel` / `officer-priya`). If `/api` calls return 500 in the browser, the API isn't running.

---

## 2. Panel Q&A: "why not X?" and trade-offs (10–15 min)

**Why not microservices?** The time box is tight, the boundaries are unproven, and there's one team. The modules already communicate via events (Notifications never calls Claims), so extraction later is a deployment change. Split when scaling, ownership or release cadence diverge.

**Why is Kafka optional?** I had no Docker locally, and the deliverable must start locally. The outbox makes the transport a config switch: `Messaging:Transport=Kafka`, with `docker-compose.kafka.yml` for the broker. The events are keyed by claim id for ordering, and the producer uses idempotence with acks=all.

**Did you run it against real Kafka?** No. The Kafka producer and consumer compile and are wired by config, but they have never run against a broker: Docker wasn't available and no test covers that path. The in-process transport, the outbox and the idempotent handler are tested. First step with more time: run `docker-compose.kafka.yml` and add a Testcontainers integration test.

**Why an outbox instead of publishing to Kafka directly?** It avoids the dual-write problem. The DB commit and the event are atomic. Delivery is at-least-once, so consumers deduplicate on `ProcessedEvents`.

**What's synchronous and what's async?** Anything where the user needs an authoritative answer now (validation, conflicts, new state) is REST. Side effects and fan-out (notifications, future audit/analytics/payments) are events.

**How does one Angular app serve two user types?** Lazy feature areas behind `canMatch` role guards, plus a shared claim-detail screen whose actions come from the server. The guards are UX; the API enforces everything.

**Who owns the contract?** The backend, via C# records published as OpenAPI. TypeScript types mirror it today; next step is generating them in CI.

**How is liability calculated?** Approved-but-unpaid = approved amount; undecided open = claimant estimate; closed = 0. Shown per market in local currency and USD at indicative rates, with the basis shown next to the number. Production needs reserves, excess/limits, recoveries and dated FX.

**How do you stop two officers acting on the same claim?** Only the assigned officer can act, and pick-up only works on Submitted claims. A `Version` concurrency token turns races into 409s.

**Why SQLite?** Zero-install local start, real migrations and a real SQL engine. Its limits show up honestly: it can't aggregate `decimal` server-side, so dashboards aggregate in memory. Production would be SQL Server or Postgres; EF Core makes that a provider change plus a new migration.

**Why is the commit history created in one go?** Be upfront: the AI was meant to commit as it went but didn't, and the history was committed afterwards in build order with real timestamps. The journal (Session 3) records this.

### Shortcuts taken under time pressure
Each is deliberate and has a known fix. All verified in the code.

| Shortcut | Where | Production fix |
|---|---|---|
| Demo header auth (`X-Demo-User`), no real identity | `Infrastructure/Auth/` | OIDC/JWT bearer; the handler already produces a normal `ClaimsPrincipal` |
| Officers see every market | `Claim.CanView` | Market-scoped permissions |
| Kafka path never run against a broker | `Infrastructure/Messaging/Messaging.cs` | Testcontainers test; run the compose file in CI |
| Poison Kafka messages are logged and skipped | `Messaging.cs` (consumer retry loop) | Dead-letter topic |
| Claimant estimate used as the reserve | `Claim.OutstandingLiability` | Adjuster-set case reserves |
| Static, indicative FX rates | `appsettings.json` `Fx:Rates` | Dated rates from a market-data source |
| Dashboards aggregate in memory | `Services/DashboardService.cs` | SQL aggregation or event-fed read models |
| UI polls every 10s | `app.component.ts`, `claim-detail.component.ts` | SSE/SignalR driven by the same events |
| TypeScript models hand-mirrored | `core/models.ts` | Generate from OpenAPI in CI |
| No frontend tests; `npm run build` (strict templates) is the only check | `frontend/claims-ui` | Component tests for the guards and claim detail |
| No document upload | — | Object storage, malware scan, signed URLs |

---

## 3. What would you do with more time? (10 min)

In priority order (detail in ARCHITECTURE §9):
1. **Prove the Kafka path:** run the broker, add a Testcontainers integration test, add a dead-letter topic.
2. **Real identity:** OIDC for claimants and staff, market-scoped officer permissions.
3. **Document upload** on information requests.
4. **Email/SMS** as more event consumers.
5. **Reporting read models** fed from events; Postgres or SQL Server.
6. **Generated TypeScript client** and SSE push instead of polling.
7. **Observability:** structured logs, OpenTelemetry tracing, outbox and consumer lag metrics.
8. **Reassignment and approval limits** (a second officer above a threshold).

### [Candidate] What I'd do differently
Write this in your own words. Some facts you can draw on:
- Commit as I go, rather than committing everything at the end.
- Get a Kafka broker running early, so the event path is proven rather than only configured.
- Generate the TypeScript client from OpenAPI from day one.

---

## 4. Your questions (5 min)

Suggestions. Pick two or three that you genuinely want answered:
- Which parts of the claims flow already run on Kafka today, and which stay on REST? How close is my split to yours?
- How do you handle reserving and liability reporting across the six markets? Is FX and reserve logic owned by claims or by finance?
- How are Angular apps shared between customer-facing and staff users in your platforms: one app, or separate?
- What does the team's day-to-day use of AI tooling look like, and how do you review AI-generated code?
- What would the first three months in this role look like?
