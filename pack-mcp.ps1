# PowerShell script to publish Zalo.Net.Mcp Single-File Self-Contained executables for all platforms

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$rids = @("osx-x64", "osx-arm64", "win-x64", "linux-x64")

foreach ($rid in $rids) {
    Write-Host "=== Publishing Zalo.Net.Mcp for $rid ($Configuration) ===" -ForegroundColor Green
    dotnet publish mcp/Zalo.Net.Mcp/Zalo.Net.Mcp.csproj `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true
}

Write-Host "`n[SUCCESS] All target platform binaries published to artifacts/publish/Zalo.Net.Mcp/" -ForegroundColor Cyan
