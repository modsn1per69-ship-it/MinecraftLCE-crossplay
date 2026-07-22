[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot
)

$ErrorActionPreference = "Stop"
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Manifest = Join-Path $RepoRoot "patches\baseline.sha256"
$failed = $false

foreach ($line in Get-Content -LiteralPath $Manifest) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $parts = $line -split "  ", 2
    $expected = $parts[0].Trim().ToLowerInvariant()
    $relativePath = $parts[1].Trim().Replace("/", "\")
    $path = Join-Path $SourceRoot $relativePath

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Write-Host "MISSING  $relativePath"
        $failed = $true
        continue
    }

    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        Write-Host "MISMATCH $relativePath"
        $failed = $true
    }
    else {
        Write-Host "OK       $relativePath"
    }
}

if ($failed) {
    throw "The source tree does not exactly match the tested baseline."
}

Write-Host "All patched baseline files match."
