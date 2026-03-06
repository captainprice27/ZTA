# ZTA Starter (C# + Python + React + Azure + SQL Server + SQLite)

Initial monorepo scaffold for a Zero-Trust Access (ZTA) proof-of-concept.

## Stack

- Gateway/API: ASP.NET Core (`.NET 10`) + EF Core
- AI service: Python + FastAPI
- Dashboard: React + Vite
- Primary policy DB: SQL Server
- Local decision cache: SQLite
- Cloud action hook: Azure NSG block (starter stub wired)

## Repository Layout

- `src/gateway/Zta.Gateway` - C# gateway and policy engine
- `src/ai-service` - Python scoring service
- `src/web` - React dashboard
- `infra/azure` - Azure Bicep starter templates

## Quick Start

### Local install (keep caches/packages in ZTA folder)

```powershell
.\install-all.ps1
```

Options:

- `.\install-all.ps1 -SkipPython`
- `.\install-all.ps1 -SkipFrontend`
- `.\install-all.ps1 -SkipDotnet`

### 1. Start infra + backend services

```bash
docker compose up --build mssql ai-service gateway
```

Gateway API:

- `http://localhost:5000/health`

AI service:

- `http://localhost:8000/health`

### 2. Start frontend

```bash
cd src/web
npm install
npm run dev
```

Dashboard URL:

- `http://localhost:5173`

## Core API Endpoints

- `POST /api/evaluate` - evaluate one request with policy + AI signal
- `GET /api/events` - recent security events for dashboard
- `POST /api/kill-switch` - manually block an IP
- `GET /api/policies` - inspect seeded policy entries

## Notes

- SQL Server stores access policies.
- SQLite stores short-lived decisions and event stream.
- Azure NSG blocker is intentionally a safe starter stub; replace with full SDK-based implementation once credentials and resource details are available.
