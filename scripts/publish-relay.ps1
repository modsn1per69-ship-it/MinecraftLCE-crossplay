[CmdletBinding()]
param(
    [ValidateSet("portable", "win-x64", "linux-x64", "linux-arm64")]
    [string]$Runtime = "portable",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "relay\LegacyCrossplayRelay.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $RepoRoot "relay\publish\$Runtime"
}

$arguments = @(
    "publish",
    $Project,
    "--configuration", "Release",
    "--output", $OutputDirectory,
    "--self-contained", "false"
)
if ($Runtime -ne "portable") {
    $arguments += @("--runtime", $Runtime)
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Relay publish failed with exit code $LASTEXITCODE."
}

Write-Host "Published relay to $OutputDirectory"
