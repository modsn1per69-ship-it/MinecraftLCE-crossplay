[CmdletBinding()]
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "patcher\LegacyCrossplayPatcher.csproj"
$Output = Join-Path $RepoRoot "patcher\publish\$Runtime"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK was not found. Install the .NET 8 SDK and run this script again."
}

& dotnet publish $Project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $Output

if ($LASTEXITCODE -ne 0) {
    throw "Patcher publish failed with exit code $LASTEXITCODE."
}

$Executable = Join-Path $Output "LegacyCrossplayPatcher.exe"
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
    throw "Publish completed but LegacyCrossplayPatcher.exe was not found."
}

$Hash = Get-FileHash -LiteralPath $Executable -Algorithm SHA256
Write-Host "Standalone patcher: $Executable"
Write-Host "SHA256: $($Hash.Hash)"
