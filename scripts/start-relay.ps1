[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 61000,
    [string]$BindAddress = "127.0.0.1",
    [string]$LogPath = "",
    [switch]$VerboseTraffic
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Server = Join-Path $RepoRoot "relay\bin\LocalRelayServer.exe"

if (-not (Test-Path -LiteralPath $Server -PathType Leaf)) {
    & (Join-Path $PSScriptRoot "build-relay.ps1")
}

if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $LogPath = Join-Path $RepoRoot "relay\local-relay.log"
}

$arguments = @($Port, $LogPath, $BindAddress)
if ($VerboseTraffic) {
    $arguments += "--verbose-traffic"
}

Write-Host "Relay listening on ${BindAddress}:$Port"
Write-Host "Log: $LogPath"
Write-Host "Press Ctrl+C to stop."
& $Server @arguments
