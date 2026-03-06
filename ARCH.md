# ARCH: Current Implemented Architecture

## 1. Scope

This document describes the architecture currently implemented in this repository (not the target-final architecture).  
It should be updated when endpoint contracts, model loading, storage, or deployment topology changes.

## 2. High-Level Component View

1. `src/web` (React + Vite)
- Operator dashboard
- Polls events
- Exposes manual kill-switch UI

2. `src/gateway/Zta.Gateway` (ASP.NET Core)
- Main decision API (`/api/evaluate`)
- Policy check + cache + event logging
- Calls AI scoring service
- Maintains block list and manual kill-switch

3. `src/ai-service` (FastAPI)
- Model-backed risk scoring (`/score`)
- Loads network + behavior model artifacts at startup

4. Datastores (local runtime)
- Policy store: SQLite (`zta-policy.db`) for local mode
- Decision/event cache: SQLite (`zta-cache.db`)

5. Infra templates
- `infra/azure` contains Bicep starter resources (SQL + NSG)

## 3. Runtime Ports and URLs

- Frontend: `http://localhost:5173`
- Gateway: `http://localhost:5000`
- AI service: `http://localhost:8000`

Primary API:
- Gateway `/api/evaluate`, `/api/events`, `/api/policies`, `/api/kill-switch`
- AI `/score`, `/health`

## 4. Detailed Request Flow

### 4.1 Evaluate Flow

1. Client posts request context to `POST /api/evaluate` on gateway.
2. Gateway checks:
- blocked IP table,
- short-lived decision cache.
3. Gateway resolves policy:
- user + method + best path-prefix match.
4. Gateway forwards request features to AI `/score`, including optional:
- `behaviorSubject`,
- `behaviorFeatures`.
5. AI returns:
- `anomaly_score`,
- `is_anomaly`,
- reasons.
6. Gateway merges with policy result:
- allow only when policy exists and no anomaly.
7. Gateway stores:
- cached decision,
- security event,
- optional auto-block for anomaly.

### 4.2 Events Flow

- `GET /api/events` returns recent event stream.
- Implementation note: for SQLite compatibility, ordering by `DateTimeOffset` is done in-memory.

### 4.3 Kill-Switch Flow

- `POST /api/kill-switch` inserts blocked IP entry and event log.
- Calls network blocker service.
- Current network blocker behavior: logs intent (Azure action not fully implemented yet).

## 5. AI Scoring Architecture

## 5.1 Loaded Artifacts

Network:
- `src/ai-service/models/network/network_iforest.joblib`
- `src/ai-service/models/network/network_iforest_meta.json`

Behavior:
- `src/ai-service/models/behavior/behavior_*.joblib` (per subject)
- `src/ai-service/models/behavior/user_subject_map.json`

## 5.2 Score Composition

Inputs:
- network request features from gateway,
- optional behavior features map.

Sub-scores:
1. Network score from IsolationForest
2. Contextual path/method/off-hours risk
3. Optional behavior reconstruction risk

Final score:
- no behavior input: `network + path risk` (clamped)
- with behavior input: weighted blend using env weights

Decision:
- anomaly if final score >= threshold (env-configurable)

## 5.3 Robustness

- Model load failures are trapped and logged as warnings.
- Service can continue with degraded scoring if some models fail.

## 6. Data Model (Gateway)

Policy DB (`PolicyDbContext`):
- `AccessPolicy`

Cache DB (`CacheDbContext`):
- `CachedDecision`
- `SecurityEvent`
- `BlockedIpEntry`

Seeded policies include:
- `prayas` `GET /api/data`
- `prayas` `POST /api/auth`

## 7. Config and Environment

Gateway:
- `ConnectionStrings:PolicyDb`
- `ConnectionStrings:DecisionCacheDb`
- `PolicyStore:Provider` (`Sqlite` or `SqlServer`)
- `AiService:BaseUrl`
- CORS origins

AI service:
- `NETWORK_WEIGHT`
- `BEHAVIOR_WEIGHT`
- `ANOMALY_THRESHOLD`

## 8. Deployment Modes

### 8.1 Local no-Docker mode (current practical default)
- Gateway + AI + Frontend on localhost
- SQLite for policy/cache

### 8.2 Docker-assisted mode
- SQL Server container for policy DB
- Gateway and AI in containers (compose file available)

## 9. Current Gaps / Not Yet Final

1. Azure NSG mutation is stubbed.
2. Feature mapping from gateway request to IDS training feature space is approximate.
3. README primary flow still emphasizes SQL Server; local SQLite-first flow is now common.
4. No full automated integration test suite yet.

## 10. Change-Impact Notes

When changing these, update this file:
1. Endpoint contract changes (`/api/evaluate` fields).
2. Model artifact path or naming changes.
3. Port changes.
4. DB provider strategy changes.
5. Scoring formula / threshold semantics.
6. Cache key strategy (especially behavior hash logic).
