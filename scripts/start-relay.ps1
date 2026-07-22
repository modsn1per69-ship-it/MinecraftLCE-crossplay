[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 61000,
    [string]$BindAddress = "127.0.0.1",
    [string]$LogPath = "",
    [string]$AccessToken = $env:CONSOLE_LEGACY_RELAY_TOKEN,
    [ValidateRange(1, 4096)]
    [int]$MaxSessions = 64,
    [ValidateRange(1, 64)]
    [int]$MaxPeers = 8,
    [switch]$VpsMode,
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

if ($VpsMode -and -not $PSBoundParameters.ContainsKey("BindAddress")) {
    $BindAddress = "0.0.0.0"
}
if ($VpsMode -and [string]::IsNullOrWhiteSpace($AccessToken)) {
    throw "VPS mode requires -AccessToken or CONSOLE_LEGACY_RELAY_TOKEN."
}
if ($VpsMode -and $BindAddress -eq "127.0.0.1") {
    throw "VPS mode must bind to a non-loopback address such as 0.0.0.0."
}
if ($BindAddress -ne "127.0.0.1" -and [string]::IsNullOrWhiteSpace($AccessToken)) {
    Write-Warning "This non-loopback relay has no access token. Restrict it to a trusted LAN."
}

$arguments = @($Port, $LogPath, $BindAddress)
if ($VerboseTraffic) {
    $arguments += "--verbose-traffic"
}

Write-Host "Relay listening on ${BindAddress}:$Port"
Write-Host "Log: $LogPath"
Write-Host ("Authentication: " + $(if ([string]::IsNullOrWhiteSpace($AccessToken)) { "disabled" } else { "required" }))
Write-Host "Limits: $MaxSessions sessions, $MaxPeers peers per session"
Write-Host "Press Ctrl+C to stop."

$environment = @{
    CONSOLE_LEGACY_RELAY_TOKEN = $AccessToken
    CONSOLE_LEGACY_RELAY_MAX_SESSIONS = [string]$MaxSessions
    CONSOLE_LEGACY_RELAY_MAX_PEERS = [string]$MaxPeers
}
$previousEnvironment = @{}
foreach ($name in $environment.Keys) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
    [Environment]::SetEnvironmentVariable($name, $environment[$name], "Process")
}
try {
    & $Server @arguments
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], "Process")
    }
}
