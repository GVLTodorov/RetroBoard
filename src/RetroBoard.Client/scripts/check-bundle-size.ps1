<#
.SYNOPSIS
  Publishes the Blazor client and checks the Brotli-compressed _framework bundle size against a
  budget. Download size is "the primary perf metric users actually feel" (REQUIREMENTS.MD Section
  9), so it's tracked explicitly here rather than left to grow unnoticed. Non-gating in CI --
  run manually too: pwsh src/RetroBoard.Client/scripts/check-bundle-size.ps1
#>
param(
    [long]$BudgetBytes = 4194304  # 4 MiB; measured baseline is ~2.3 MB, this leaves headroom
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$publishDir = Join-Path $repoRoot "artifacts/bundle-size-check"

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

dotnet publish (Join-Path $repoRoot "src/RetroBoard.Client/RetroBoard.Client.csproj") -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$frameworkDir = Join-Path $publishDir "wwwroot/_framework"
$brotliFiles = Get-ChildItem -Path $frameworkDir -Filter "*.br" -File
$totalBytes = ($brotliFiles | Measure-Object -Property Length -Sum).Sum

$totalMb = [Math]::Round($totalBytes / 1MB, 2)
$budgetMb = [Math]::Round($BudgetBytes / 1MB, 2)

Write-Host "Brotli-compressed _framework size: $totalMb MB (budget: $budgetMb MB, $($brotliFiles.Count) files)"

Remove-Item -Recurse -Force $publishDir

if ($totalBytes -gt $BudgetBytes) {
    Write-Error "Bundle size regression: $totalMb MB exceeds the $budgetMb MB budget."
    exit 1
}

Write-Host "Bundle size OK."
