[CmdletBinding()]
param(
    [ValidateRange(1, 65534)]
    [int]$Port = 61001
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutputDir = Join-Path $RepoRoot "relay\bin"
$Server = Join-Path $OutputDir "LocalRelayServer.exe"
$Test = Join-Path $OutputDir "RelayHubIntegrationTest.exe"

& (Join-Path $PSScriptRoot "build-relay.ps1")

function Invoke-RelayIntegrationTest {
    param(
        [int]$TestPort,
        [string]$Token,
        [string]$ExpectedMarker
    )

    $LogPath = Join-Path $OutputDir "integration-test-$TestPort.log"
    Remove-Item -LiteralPath $LogPath -Force -ErrorAction SilentlyContinue

    $previousToken = $env:CONSOLE_LEGACY_RELAY_TOKEN
    try {
        if ([string]::IsNullOrEmpty($Token)) {
            Remove-Item Env:CONSOLE_LEGACY_RELAY_TOKEN -ErrorAction SilentlyContinue
        }
        else {
            $env:CONSOLE_LEGACY_RELAY_TOKEN = $Token
        }
        $serverProcess = Start-Process -FilePath $Server `
            -ArgumentList @($TestPort, $LogPath, "127.0.0.1") `
            -WindowStyle Hidden -PassThru
    }
    finally {
        if ($null -eq $previousToken) {
            Remove-Item Env:CONSOLE_LEGACY_RELAY_TOKEN -ErrorAction SilentlyContinue
        }
        else {
            $env:CONSOLE_LEGACY_RELAY_TOKEN = $previousToken
        }
    }

    try {
        $ready = $false
        for ($attempt = 0; $attempt -lt 50; $attempt++) {
            Start-Sleep -Milliseconds 100
            if (Test-Path -LiteralPath $LogPath) {
                if (Select-String -LiteralPath $LogPath `
                    -SimpleMatch "listening 127.0.0.1:$TestPort" -Quiet) {
                    $ready = $true
                    break
                }
            }
        }
        if (-not $ready) {
            throw "Relay did not begin listening on port $TestPort."
        }

        $arguments = @("127.0.0.1", $TestPort)
        if (-not [string]::IsNullOrEmpty($Token)) {
            $arguments += $Token
        }
        $output = @(& $Test @arguments)
        if ($LASTEXITCODE -ne 0) {
            throw "Relay integration test failed with exit code $LASTEXITCODE."
        }
        $output | ForEach-Object { Write-Host $_ }
        if ($output -notcontains $ExpectedMarker) {
            throw "Relay integration test did not print $ExpectedMarker."
        }
    }
    finally {
        if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
            Stop-Process -Id $serverProcess.Id -Force
            $serverProcess.WaitForExit(5000) | Out-Null
        }
    }
}

Invoke-RelayIntegrationTest -TestPort $Port -Token "" `
    -ExpectedMarker "RELAY_HUB_3_PEER_OK"
Invoke-RelayIntegrationTest -TestPort ($Port + 1) `
    -Token "integration-test-access-token" `
    -ExpectedMarker "RELAY_HUB_3_PEER_AUTH_OK"

Write-Host "Relay integration tests passed."
