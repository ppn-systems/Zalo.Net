<#
.SYNOPSIS
Pushes NuGet packages (.nupkg) to a NuGet feed for Zalo.Net.

.DESCRIPTION
- Pushes all .nupkg in the artifacts folder (default: .\artifacts\nuget)
- Accepts API key from parameter or environment variable NUGET_API_KEY
- Pushes to NuGet.org by default or a custom source

.USAGE
pwsh .\push-nuget.ps1 -ApiKey "YOUR_API_KEY"
pwsh .\push-nuget.ps1 -Source "https://api.nuget.org/v3/index.json" -SkipDuplicates
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $false)]
    [string]$PackagesDir = ".\artifacts\nuget",

    [Parameter(Mandatory = $false)]
    [string]$Source = "https://api.nuget.org/v3/index.json",

    [Parameter(Mandatory = $false)]
    [string]$ApiKey = $env:NUGET_API_KEY,

    [Parameter(Mandatory = $false)]
    [switch]$SkipDuplicates
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Section {
    param([Parameter(Mandatory = $true)][string]$Title)
    Write-Host ""
    Write-Host ("=" * 72) -ForegroundColor DarkGray
    Write-Host ("  " + $Title) -ForegroundColor Cyan
    Write-Host ("=" * 72) -ForegroundColor DarkGray
}

try {
    Write-Section "Zalo.Net NuGet Push"

    if (-not (Test-Path -LiteralPath $PackagesDir)) {
        throw "Packages directory not found: $PackagesDir. Run .\pack.ps1 first."
    }

    if ([string]::IsNullOrWhiteSpace($ApiKey)) {
        throw "API key is required. Pass -ApiKey <key> or set $env:NUGET_API_KEY."
    }

    $pkgDirFull = (Resolve-Path -LiteralPath $PackagesDir).Path

    Write-Host "PackagesDir    : $pkgDirFull"
    Write-Host "Source         : $Source"
    Write-Host "SkipDuplicates : $($SkipDuplicates.IsPresent)"

    Write-Section "Discover Packages"

    $packages = @(Get-ChildItem -LiteralPath $pkgDirFull -File |
        Where-Object {
            $_.Extension -ieq ".nupkg" -and
            $_.Name -notmatch '\.snupkg$'
        } | Sort-Object Name)

    if ($packages.Count -eq 0) {
        Write-Host "No .nupkg packages found in: $pkgDirFull" -ForegroundColor Yellow
        exit 0
    }

    foreach ($p in $packages) {
        Write-Host ("- " + $p.Name) -ForegroundColor Green
    }

    Write-Section "Pushing Packages"

    foreach ($p in $packages) {
        $pushArgs = @(
            "nuget", "push",
            $p.FullName,
            "--api-key", $ApiKey,
            "--source", $Source
        )

        if ($SkipDuplicates.IsPresent) {
            $pushArgs += "--skip-duplicate"
        }

        if ($PSCmdlet.ShouldProcess($p.FullName, "Push package to $Source")) {
            Write-Host ">> dotnet $($pushArgs -join ' ')" -ForegroundColor DarkGray
            & dotnet @pushArgs
            if ($LASTEXITCODE -ne 0) {
                throw "Push failed (exit $LASTEXITCODE): $($p.Name)"
            }
        }
    }

    Write-Host ""
    Write-Host "Done pushing all packages successfully." -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    exit 1
}
