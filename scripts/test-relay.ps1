[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 61001
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutputDir = Join-Path $RepoRoot "relay\bin"
$Server = Join-Path $OutputDir "LocalRelayServer.exe"
$Test = Join-Path $OutputDir "RelayHubIntegrationTest.exe"
$LogPath = Join-Path $OutputDir "integration-test.log"

& (Join-Path $PSScriptRoot "build-relay.ps1")

$serverProcess = Start-Process -FilePath $Server -ArgumentList @($Port, $LogPath, "127.0.0.1") -WindowStyle Hidden -PassThru
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        Start-Sleep -Milliseconds 100
        if (Test-Path -LiteralPath $LogPath) {
            if (Select-String -LiteralPath $LogPath -SimpleMatch "listening 127.0.0.1:$Port" -Quiet) {
                $ready = $true
                break
            }
        }
    }
    if (-not $ready) {
        throw "Relay did not begin listening on port $Port."
    }

    & $Test "127.0.0.1" $Port
    if ($LASTEXITCODE -ne 0) {
        throw "Relay integration test failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id -Force
        $serverProcess.WaitForExit(5000) | Out-Null
    }
}

Write-Host "Relay integration test passed."
