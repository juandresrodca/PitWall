<#
.SYNOPSIS
    Builds PitWall.exe: the Astro dashboard, the tests, then one self-contained Windows executable.

.EXAMPLE
    ./scripts/publish.ps1
    ./scripts/publish.ps1 -Runtime win-arm64 -Output dist/arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',
    [string] $Output = 'dist',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host '==> Building the dashboard (web/)' -ForegroundColor Cyan
    Push-Location web
    try {
        npm ci
        if ($LASTEXITCODE) { throw 'npm ci failed' }
        npm run build
        if ($LASTEXITCODE) { throw 'Dashboard build failed' }
    }
    finally { Pop-Location }

    if (-not $SkipTests) {
        Write-Host '==> Running tests' -ForegroundColor Cyan
        dotnet test --project tests/PitWall.Core.Tests -c Release
        if ($LASTEXITCODE) { throw 'Tests failed' }
    }

    Write-Host "==> Publishing PitWall.exe ($Runtime)" -ForegroundColor Cyan
    dotnet publish src/PitWall.Desktop -c Release -r $Runtime -o $Output
    if ($LASTEXITCODE) { throw 'Publish failed' }

    $exe = Join-Path $Output 'PitWall.exe'
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "==> Done: $exe ($size MB)" -ForegroundColor Green
}
finally { Pop-Location }
