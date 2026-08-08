[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot
)

$ErrorActionPreference = "Stop"
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PatchPath = Join-Path $RepoRoot "patches\crossplay-core.patch"
$RelayTarget = Join-Path $SourceRoot "Minecraft.Client\Common\Network\Relay"

$git = Get-Command git.exe -ErrorAction SilentlyContinue
if ($null -eq $git) {
    $git = Get-Command git -ErrorAction SilentlyContinue
}
if ($null -eq $git) {
    throw "Git was not found."
}

Push-Location $SourceRoot
$previousGitDir = $env:GIT_DIR
try {
    $env:GIT_DIR = "NUL"
    & $git.Source -c core.autocrlf=false apply --no-index --reverse --check --whitespace=nowarn $PatchPath
    if ($LASTEXITCODE -ne 0) {
        throw "The patch cannot be reversed cleanly. Preserve your work and inspect the source changes manually."
    }
    & $git.Source -c core.autocrlf=false apply --no-index --reverse --whitespace=nowarn $PatchPath
    if ($LASTEXITCODE -ne 0) {
        throw "Git failed while reversing crossplay-core.patch."
    }
}
finally {
    if ($null -eq $previousGitDir) {
        Remove-Item Env:GIT_DIR -ErrorAction SilentlyContinue
    }
    else {
        $env:GIT_DIR = $previousGitDir
    }
    Pop-Location
}

$relayFiles = @(
    "LegacyRelayPolicy.h",
    "LegacyRelayUserConfig.h",
    "NetworkPlayerRelay.cpp",
    "NetworkPlayerRelay.h",
    "PlatformNetworkManagerRelay.cpp",
    "PlatformNetworkManagerRelay.h",
    "RelayTransport.cpp",
    "RelayTransport.h"
)

foreach ($name in $relayFiles) {
    $path = Join-Path $RelayTarget $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path -Force
    }
}

if (Test-Path -LiteralPath $RelayTarget -PathType Container) {
    $remaining = Get-ChildItem -LiteralPath $RelayTarget -Force
    if ($remaining.Count -eq 0) {
        Remove-Item -LiteralPath $RelayTarget -Force
    }
}

Write-Host "Crossplay source patch removed."
