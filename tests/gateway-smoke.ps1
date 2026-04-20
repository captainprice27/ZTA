param(
    [string]$GatewayBaseUrl = "http://localhost:5000"
)

$ErrorActionPreference = "Stop"

function Invoke-ZtaJson {
    param(
        [string]$Method,
        [string]$Url,
        [object]$Body = $null
    )

    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $Url -ContentType "application/json"
    }

    return Invoke-RestMethod -Method $Method -Uri $Url -ContentType "application/json" -Body ($Body | ConvertTo-Json -Depth 8)
}

Write-Host "Checking gateway health..."
$health = Invoke-ZtaJson -Method Get -Url "$GatewayBaseUrl/health"
if ($health.status -ne "ok") {
    throw "Gateway health check failed"
}

Write-Host "Testing allow path..."
$allow = Invoke-ZtaJson -Method Post -Url "$GatewayBaseUrl/api/evaluate" -Body @{
    userId = "prayas"
    sourceIp = "203.0.113.10"
    path = "/api/data"
    method = "GET"
    requestsPerMinute = 8
    payloadBytes = 2048
    hourOfDay = 14
    requestLatencyMs = 120
}

if (-not $allow.allowed) {
    throw "Expected allow decision"
}

Write-Host "Testing anomaly/block path..."
try {
    Invoke-ZtaJson -Method Post -Url "$GatewayBaseUrl/api/evaluate" -Body @{
        userId = "prayas"
        sourceIp = "203.0.113.11"
        path = "/api/data/attack"
        method = "GET"
        requestsPerMinute = 250
        payloadBytes = 500000
        hourOfDay = 2
        requestLatencyMs = 2400
    } | Out-Null

    throw "Expected block decision"
}
catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 403) {
        throw
    }
}

Write-Host "Testing kill-switch..."
$kill = Invoke-ZtaJson -Method Post -Url "$GatewayBaseUrl/api/kill-switch" -Body @{
    userId = "operator"
    sourceIp = "203.0.113.90"
    reason = "smoke-test"
}

if ($kill.status -ne "blocked") {
    throw "Kill-switch failed"
}

$events = Invoke-ZtaJson -Method Get -Url "$GatewayBaseUrl/api/events"
if (-not ($events | Where-Object { $_.sourceIp -eq "203.0.113.90" })) {
    throw "Expected kill-switch event not found"
}

Write-Host "Gateway smoke tests passed."
