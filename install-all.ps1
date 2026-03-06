param(
    [switch]$SkipPython,
    [switch]$SkipFrontend,
    [switch]$SkipDotnet
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$localRoot = Join-Path $root ".local"
$venvPath = Join-Path $root ".venv"
$pythonCache = Join-Path $localRoot "pip-cache"
$npmCache = Join-Path $localRoot "npm-cache"
$nugetCache = Join-Path $localRoot "nuget-packages"

New-Item -ItemType Directory -Force -Path $localRoot | Out-Null
New-Item -ItemType Directory -Force -Path $pythonCache | Out-Null
New-Item -ItemType Directory -Force -Path $npmCache | Out-Null
New-Item -ItemType Directory -Force -Path $nugetCache | Out-Null

Write-Host "Using local package paths under: $localRoot"

if (-not $SkipPython) {
    Write-Host "`n[Python] Creating local virtual environment..."
    if (-not (Test-Path $venvPath)) {
        python -m venv $venvPath
    }

    $pythonExe = Join-Path $venvPath "Scripts\python.exe"
    & $pythonExe -m pip install --upgrade pip --cache-dir $pythonCache
    & $pythonExe -m pip install -r (Join-Path $root "requirement.tzt") --cache-dir $pythonCache
    Write-Host "[Python] Done."
}

if (-not $SkipFrontend) {
    Write-Host "`n[Frontend] Installing npm packages with local cache..."
    $webPath = Join-Path $root "src\web"
    Push-Location $webPath
    npm install --cache $npmCache
    Pop-Location
    Write-Host "[Frontend] Done."
}

if (-not $SkipDotnet) {
    Write-Host "`n[C#] Restoring dotnet packages with local NuGet cache..."
    $gatewayProj = Join-Path $root "src\gateway\Zta.Gateway\Zta.Gateway.csproj"
    $env:NUGET_PACKAGES = $nugetCache
    dotnet restore $gatewayProj
    Write-Host "[C#] Done."
}

Write-Host "`nAll requested installs completed."
Write-Host "Python venv: $venvPath"
Write-Host "pip cache:   $pythonCache"
Write-Host "npm cache:   $npmCache"
Write-Host "NuGet cache: $nugetCache"
