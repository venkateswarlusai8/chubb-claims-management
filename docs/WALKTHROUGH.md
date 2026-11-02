# Walkthrough notes (30–60 minutes)

## Presentation (15–20 min)
1. **Problem:** fragmented intake; claimants have no visibility; staff work from inboxes; managers can't see exposure.
2. **Prioritisation:** backend first (domain rules, outbox, tests), then a UI covering both journeys. What I left out, and why.
3. **Architecture** (`docs/ARCHITECTURE.md`):
   - Modular monolith with Claims, Reporting and Notifications modules.
   - REST for commands and queries; events via a transactional outbox to in-process handlers or Kafka.
4. **Domain:**
   - Claim aggregate and state machine.
   - Assignment and ownership rules.
   - Audit history.
   - `allowedActions` as the contract that keeps the UI thin.
5. **Demo:** follow the demo script in README.md. Show the async notification arriving, the 403/409 error responses, and the dashboard.
6. **Tests:** `dotnet test`. Domain rules plus HTTP-level lifecycle, authorisation and idempotency tests.

## Likely panel questions

**Why not microservices?** The time box is tight, the boundaries are unproven, and there's one team. The modules already communicate via events (Notifications never calls Claims), so extraction later is a deployment change. Split when scaling, ownership or release cadence diverge.

**Why is Kafka optional?** I had no Docker locally, and the deliverable must start locally. The outbox makes the transport a config switch: `Messaging:Transport=Kafka`, with `docker-compose.kafka.yml` for the broker. The events are keyed by claim id for ordering, and the producer uses idempotence with acks=all.

**Why an outbox instead of publishing to Kafka directly?** It avoids the dual-write problem. The DB commit and the event are atomic. Delivery is at-least-once, so consumers deduplicate on `ProcessedEvents`.

**What's synchronous and what's async?** Anything where the user needs an authoritative answer now (validation, conflicts, new state) is REST. Side effects and fan-out (notifications, future audit/analytics/payments) are events.

**How does one Angular app serve two user types?** Lazy feature areas behind `canMatch` role guards, plus a shared claim-detail screen whose actions come from the server. The guards are UX; the API enforces everything.

**Who owns the contract?** The backend, via C# records published as OpenAPI. TypeScript types mirror it today; next step is generating them in CI.

**How is liability calculated?** Approved-but-unpaid = approved amount; undecided open = claimant estimate; closed = 0. Shown per market in local currency and USD at indicative rates, with the basis shown next to the number. Production needs reserves, excess/limits, recoveries and dated FX.

**How do you stop two officers acting on the same claim?** Only the assigned officer can act, and pick-up only works on Submitted claims. A `Version` concurrency token turns races into 409s.

**What next?** OIDC and market-scoped permissions, document upload, email/SMS consumers plus a DLQ, reporting read models, a generated client and SSE, observability, approval limits.
