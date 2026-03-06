# Local API And Runbook

## Local Services And URLs

### Frontend (React + Vite)
- App: `http://localhost:5173`

### Gateway (C# / ASP.NET)
- Base: `http://localhost:5000`
- Health: `http://localhost:5000/health`
- Policies: `http://localhost:5000/api/policies`
- Evaluate: `http://localhost:5000/api/evaluate` (`POST`)
- Events: `http://localhost:5000/api/events`
- Kill switch: `http://localhost:5000/api/kill-switch` (`POST`)

### AI Service (Python / FastAPI)
- Base: `http://localhost:8000`
- Health: `http://localhost:8000/health`
- Score: `http://localhost:8000/score` (`POST`)

### Optional Docker SQL Server (only if Docker is installed)
- SQL Server port: `localhost:1433`

## Request Shapes

### Gateway `POST /api/evaluate`
```json
{
  "userId": "prayas",
  "sourceIp": "203.0.113.10",
  "path": "/api/data",
  "method": "GET",
  "requestsPerMinute": 5,
  "payloadBytes": 2048,
  "hourOfDay": 14,
  "requestLatencyMs": 120,
  "behaviorSubject": "s002",
  "behaviorFeatures": {
    "H.period": 0.12,
    "DD.period.t": 0.33
  }
}
```

### Gateway `POST /api/kill-switch`
```json
{
  "userId": "operator",
  "sourceIp": "203.0.113.10",
  "reason": "manual-test"
}
```

### AI `POST /score`
Accepts both camelCase and snake_case keys (for example `userId` or `user_id`).

## Commands: Setup / Install

Run from repo root:
```powershell
cd e:\SAVED_CODES_AND_PROJECT\code-playing\ZTA
.\install-all.ps1
```

Optional skip flags:
```powershell
.\install-all.ps1 -SkipPython
.\install-all.ps1 -SkipFrontend
.\install-all.ps1 -SkipDotnet
```

## Commands: Run Services

### 1) AI service
```powershell
cd e:\SAVED_CODES_AND_PROJECT\code-playing\ZTA\src\ai-service
& "e:\SAVED_CODES_AND_PROJECT\code-playing\ZTA\.venv\Scripts\python.exe" -m uvicorn app.main:app --host 0.0.0.0 --port 8000 --reload
```

### 2) Gateway
```powershell
cd e:\SAVED_CODES_AND_PROJECT\code-playing\ZTA\src\gateway\Zta.Gateway
dotnet run
```

### 3) Frontend
```powershell
cd e:\SAVED_CODES_AND_PROJECT\code-playing\ZTA\src\web
npm run dev
```

## Commands: Build

### Frontend
```powershell
cd e:\SAVED_CODES_AND_PROJECT\code-playing\ZTA\src\web
npm run build
npm run preview
```

### Gateway
```powershell
cd e:\SAVED_CODES_AND_PROJECT\code-playing\ZTA\src\gateway\Zta.Gateway
dotnet build
```

### AI service quick syntax check
```powershell
cd e:\SAVED_CODES_AND_PROJECT\code-playing\ZTA
& ".\.venv\Scripts\python.exe" -m py_compile ".\src\ai-service\app\main.py"
```

## Commands: Useful Quick Tests

### Health checks
```powershell
curl http://localhost:8000/health
curl http://localhost:5000/health
curl http://localhost:5000/api/policies
```

### Events
```powershell
curl http://localhost:5000/api/events
```

## Notes
- Current local gateway is configured to use SQLite for policy + cache by default.
- AI loads:
  - `src/ai-service/models/network/network_iforest.joblib`
  - `src/ai-service/models/behavior/behavior_*.joblib`
