# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Context

Take-home assessment (brief: underspecified claims platform; backend prioritised over frontend). The panel probes every decision, so keep `docs/ARCHITECTURE.md` and `docs/AI_WORKING_JOURNAL.md` in step with significant changes. Append to the journal and do not invent interactions; sections marked **[Candidate]** are for the user to fill in.

## Commands

```bash
# Backend (run from backend/)
dotnet build
dotnet test                                                   # all tests
dotnet test --filter "FullyQualifiedName~ClaimLifecycleTests" # one class
dotnet test --filter "Name=Invalid_transition_returns_409_with_code"
cd Claims.Api && dotnet run                                   # http://localhost:5080, Swagger at /swagger
dotnet run --launch-profile "Claims.Api (Kafka)"              # needs: docker compose -f ../../docker-compose.kafka.yml up -d

# EF migrations (local tool pinned in backend/.config/dotnet-tools.json; run `dotnet tool restore` once)
dotnet ef migrations add <Name> --project Claims.Api --output-dir Infrastructure/Persistence/Migrations

# Frontend (run from frontend/claims-ui/)
npm start        # ng serve :4200, proxies /api -> :5080 (proxy.conf.json)
npm run build    # strictTemplates is on, so template type errors fail the build
```

There is no lint step and no frontend test suite; `npm run build` is the frontend check.

Demo user ids for `X-Demo-User` (curl, or Swagger's **Authorize**): `claimant-asha`, `claimant-daniel`, `claimant-mei`, `officer-priya`, `officer-james` (`Infrastructure/Auth/DemoUserDirectory.cs`).

Gotchas:
- `dotnet ef migrations add` builds *before* it scaffolds. Rebuild before `dotnet run --no-build`, or the app starts with no migrations ("no such table").
- Stop a running API before `dotnet test`; the test build overwrites `Claims.Api/bin`.
- Reset demo data by deleting `backend/Claims.Api/claims.db*`. Seeding only runs when the Claims table is empty.

## Architecture

A modular monolith with Claims, Reporting and Notifications modules plus `Infrastructure/`. The modules are not separate folders: they are `ClaimService`, `DashboardService` and `NotificationService` (with `NotificationEventHandler`) in `Services/`. Files hold several related types, so search by type name rather than file name. For example, all of messaging (options, publishers, `OutboxProcessor`, both background services, DI registration) is in `Infrastructure/Messaging/Messaging.cs`, and the non-claims controllers are in `Controllers/SupportingControllers.cs`. The parts that need several files to understand:

- **Rules live in the aggregate.** `Domain/Claim.cs` owns the state machine, the role/ownership/assigned-officer checks, history entries and domain events. `Services/ClaimService` just loads, calls one domain method and saves. Add new lifecycle behaviour as a `Claim` method, not in a controller or service. `Claim.AllowedActions(actor)` is sent to the UI and must stay consistent with the guard clauses in the same file.
- **Exceptions → HTTP:** the `DomainValidationException`/`ClaimAccessException`/`NotFoundException`/`ClaimStateException` classes map to 400/403/404/409 problem details with a `code` in `Infrastructure/Web/ApiExceptionHandler.cs`. Throw these rather than returning status codes.
- **Events/outbox:** `ClaimsDbContext.SaveChanges*` turns `claim.DomainEvents` into `OutboxMessage` rows in the same transaction.
  - `OutboxProcessor` (polled by `OutboxBackgroundService`) publishes through `IEventPublisher`. That is `InProcessEventPublisher` by default, or `KafkaEventPublisher` when `Messaging:Transport=Kafka`, in which case `KafkaConsumerBackgroundService` feeds the handlers.
  - Handlers implement `IClaimEventHandler` and must be idempotent via `ProcessedEvents`.
  - A new event type needs a record in `Domain/ClaimEvents.cs` **and** an entry in `ClaimEventTypes`.
- **Auth:** the `X-Demo-User` header goes to `DemoHeaderAuthenticationHandler`, which builds a real `ClaimsPrincipal` from `DemoUserDirectory`. Controllers use `[Authorize(Roles = Roles.Officer)]` and `User.ToActor()`. Claimant identity must come from the principal, never the request body. A claim the caller can't see returns 404 (not 403).
- **Name clash:** the domain `Claim` collides with `System.Security.Claims.Claim`. Auth code aliases it (`SecurityClaim`); avoid importing both namespaces in one file.
- **SQLite constraints:** decimals can't be aggregated or ordered server-side, so dashboards aggregate in memory. DateTimes are UTC-tagged by a converter in `OnModelCreating`. Entity Guid keys are `ValueGeneratedNever()` so children added through navigations are inserted, not updated.
- **Config read lazily:** the connection string and messaging options are resolved from DI at runtime, not in `Program.cs` top-level code, so `WebApplicationFactory` overrides (`UseSetting`) work. Keep it that way.
- **Tests:** `ApiFactory` uses a temp SQLite file, no seed data and `EnableBackgroundDispatch=false`. Call `DispatchEventsAsync()` to deliver outbox events deterministically. `ClaimsApiTests` share one DB (assert on your own claim ids); `DashboardApiTests` gets a fresh factory per test for exact totals.

### Frontend (`frontend/claims-ui/src/app`)
- Standalone components, signals, built-in control flow; no NgModules and no state library.
- `core/models.ts` hand-mirrors `backend/Claims.Api/Contracts/Contracts.cs`. Change both together. Enums are strings on the wire.
- `core/session.ts` holds the demo user, the interceptor (adds `X-Demo-User`) and the `hasRole` `canMatch` guard. **Route order in `app.routes.ts` matters:** a failing guard redirects instead of falling through, so the shared `claims/:id` route must come before the claimant-only `claims` route.
- `features/claim-detail` is shared by both roles. Render actions only from `claim.allowedActions`; don't re-derive the rules client-side.
