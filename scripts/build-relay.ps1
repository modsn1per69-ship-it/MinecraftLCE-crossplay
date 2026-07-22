[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutputDir = Join-Path $RepoRoot "relay\bin"
$ServerSource = Join-Path $RepoRoot "relay\LocalRelayServer.cs"
$TestSource = Join-Path $RepoRoot "tests\RelayHubIntegrationTest.cs"

$candidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

$csc = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ($null -eq $csc) {
    $command = Get-Command csc.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $csc = $command.Source
    }
}
if ($null -eq $csc) {
    throw "C# compiler not found. Install a Windows .NET Framework developer pack or Visual Studio Build Tools."
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

& $csc /nologo /optimize+ "/out:$OutputDir\LocalRelayServer.exe" $ServerSource
if ($LASTEXITCODE -ne 0) {
    throw "LocalRelayServer compilation failed."
}

& $csc /nologo /optimize+ "/out:$OutputDir\RelayHubIntegrationTest.exe" $TestSource
if ($LASTEXITCODE -ne 0) {
    throw "RelayHubIntegrationTest compilation failed."
}

Write-Host "Built relay tools in $OutputDir"
