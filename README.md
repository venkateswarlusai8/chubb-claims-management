# APAC Claims Platform

Take-home assessment: a claims platform where **claimants** report incidents, track claims, answer information requests and receive decisions, and **claims officers** pick up, assess, decide and settle claims and monitor team workload and liability exposure. Both user types use one Angular app.

- **Backend:** ASP.NET Core 8 Web API, EF Core 8 with SQLite, xUnit tests (`backend/`)
- **Frontend:** Angular 18 standalone app with lazy, role-guarded feature areas (`frontend/claims-ui/`)
- **Messaging:** transactional outbox → in-process dispatch by default, or Kafka with one config switch
- **Docs:** [Architecture & decisions](docs/ARCHITECTURE.md) · [Walkthrough notes](docs/WALKTHROUGH.md) · [AI working journal](docs/AI_WORKING_JOURNAL.md)

## Run locally

Prerequisites: .NET SDK 8+ (8.0 runtime) and Node 18+.

```bash
# 1. API on http://localhost:5080 (Swagger at /swagger). Creates claims.db and seeds demo data on first run.
cd backend/Claims.Api
dotnet run

# 2. UI on http://localhost:4200 (dev server proxies /api to :5080)
cd frontend/claims-ui
npm install
npm start
```

Use the **Signed in as** switcher (top right) to act as a claimant (Asha, Daniel, Mei) or an officer (Priya, James). In Swagger, click **Authorize** and enter a demo user id such as `officer-priya`.

To reset demo data, stop the API and delete `backend/Claims.Api/claims.db*`.

### Optional: real Kafka

```bash
docker compose -f docker-compose.kafka.yml up -d
cd backend/Claims.Api
dotnet run --launch-profile "Claims.Api (Kafka)"   # sets Messaging__Transport=Kafka
```

Events are published to `claims.claim-events.v1`, keyed by claim id. The notification consumer then reads from Kafka instead of being called in-process. Kafka UI: http://localhost:8085.

## Test

```bash
cd backend
dotnet test
```

Domain unit tests cover the lifecycle rules. API integration tests (`WebApplicationFactory`, temporary SQLite file) cover authorisation, visibility, the full lifecycle over HTTP, async notifications, idempotent delivery, error contracts and dashboard calculations.

## Demo script

1. **Daniel** (claimant) → *Report an incident* → submit. The claim appears with status *Submitted*, and a "Claim received" notification arrives about a second later from the async consumer.
2. **Priya** (officer) → *Claims queue* (Unassigned tab, oldest first; age over the SLA is red) → open the claim → *Pick up claim* → *Request information*.
3. **Daniel** → *My claims* shows an "Action needed" banner → open the claim → answer the question.
4. **Priya** gets a "Claimant responded" notification → *Approve* (amount defaults to the estimate) → *Record settlement*.
5. **Priya** → *Dashboard* shows status mix, ageing, per-officer workload, and outstanding liability by market converted to USD.
6. Try something not allowed, e.g. **James** actioning Priya's claim: the API returns 403/409 with a readable reason.
