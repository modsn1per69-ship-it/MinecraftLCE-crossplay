[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,
    [switch]$CheckOnly
)

$ErrorActionPreference = "Stop"
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PatchPath = Join-Path $RepoRoot "patches\crossplay-core.patch"
$RelaySource = Join-Path $RepoRoot "patches\relay"
$RelayTarget = Join-Path $SourceRoot "Minecraft.Client\Common\Network\Relay"

$required = @(
    "MinecraftConsoles.sln",
    "Minecraft.Client\Minecraft.Client.vcxproj",
    "Minecraft.Client\Common\Network\GameNetworkManager.cpp",
    "Minecraft.World\LevelChunk.cpp"
)

foreach ($relativePath in $required) {
    $candidate = Join-Path $SourceRoot $relativePath
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Source baseline check failed: missing $relativePath"
    }
}

$gameNetworkManager = Join-Path $SourceRoot "Minecraft.Client\Common\Network\GameNetworkManager.cpp"
if (Select-String -LiteralPath $gameNetworkManager -SimpleMatch "CPlatformNetworkManagerRelay" -Quiet) {
    throw "This source tree already appears to contain the relay patch."
}

$git = Get-Command git.exe -ErrorAction SilentlyContinue
if ($null -eq $git) {
    $git = Get-Command git -ErrorAction SilentlyContinue
}
if ($null -eq $git) {
    throw "Git was not found. Install Git for Windows, reopen PowerShell, and run this script again."
}

Push-Location $SourceRoot
try {
    & $git.Source -c core.autocrlf=false apply --check --whitespace=nowarn $PatchPath
    if ($LASTEXITCODE -ne 0) {
        throw "The patch does not match this source tree. Confirm the exact baseline and that the source is unmodified."
    }

    if ($CheckOnly) {
        Write-Host "Patch check passed. No files were changed."
        return
    }

    & $git.Source -c core.autocrlf=false apply --whitespace=nowarn $PatchPath
    if ($LASTEXITCODE -ne 0) {
        throw "Git failed while applying crossplay-core.patch."
    }
}
finally {
    Pop-Location
}

New-Item -ItemType Directory -Path $RelayTarget -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $RelaySource "LegacyRelayPolicy.h") -Destination $RelayTarget -Force
Copy-Item -LiteralPath (Join-Path $RelaySource "NetworkPlayerRelay.cpp") -Destination $RelayTarget -Force
Copy-Item -LiteralPath (Join-Path $RelaySource "NetworkPlayerRelay.h") -Destination $RelayTarget -Force
Copy-Item -LiteralPath (Join-Path $RelaySource "PlatformNetworkManagerRelay.cpp") -Destination $RelayTarget -Force
Copy-Item -LiteralPath (Join-Path $RelaySource "PlatformNetworkManagerRelay.h") -Destination $RelayTarget -Force
Copy-Item -LiteralPath (Join-Path $RelaySource "RelayTransport.cpp") -Destination $RelayTarget -Force
Copy-Item -LiteralPath (Join-Path $RelaySource "RelayTransport.h") -Destination $RelayTarget -Force

Write-Host "Crossplay source patch applied successfully."
Write-Host "Next: read docs\BUILDING.md and build every platform from this same source revision."
