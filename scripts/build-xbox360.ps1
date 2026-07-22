param(
    [ValidateSet("Debug", "Release", "ContentPackage", "CONTENTPACKAGE_SYMBOLS", "ReleaseForArt", "ContentPackage_NO_TU")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "MinecraftConsoles.sln"
$project = Join-Path $root "Minecraft.Client\Minecraft.Client.vcxproj"

$xdkRoots = @(
    $env:XEDK,
    ${env:Xbox360SDKDir},
    "C:\Program Files (x86)\Microsoft Xbox 360 SDK",
    "C:\Program Files\Microsoft Xbox 360 SDK"
) | Where-Object { $_ -and (Test-Path $_) }

if (-not $xdkRoots) {
    throw "Xbox 360 XDK/toolchain not found. Visual Studio's PC compiler cannot emit a default.xex. Install or mount your licensed Xbox 360 SDK, then run this script again."
}

[xml]$projectXml = Get-Content -LiteralPath $project
$ns = New-Object System.Xml.XmlNamespaceManager($projectXml.NameTable)
$ns.AddNamespace("m", "http://schemas.microsoft.com/developer/msbuild/2003")
$condition = "'`$(Configuration)|`$(Platform)'=='$Configuration|Xbox 360'"
$group = $projectXml.SelectSingleNode("//m:ItemDefinitionGroup[@Condition=`"$condition`"]", $ns)
if (-not $group -or $group.ClCompile.PreprocessorDefinitions -notmatch "CONSOLE_LEGACY_RELAY") {
    throw "Configuration $Configuration|Xbox 360 is not relay-enabled; refusing to build an unmodified XEX."
}

$msbuildCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe",
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
) | Where-Object { Test-Path $_ }

if (-not $msbuildCandidates) {
    throw "MSBuild 4.x was not found. The Xbox 360 project requires the MSBuild integration supplied with its SDK."
}

$started = Get-Date
& $msbuildCandidates[0] $solution /m /t:Build "/p:Configuration=$Configuration" "/p:Platform=Xbox 360"
if ($LASTEXITCODE -ne 0) {
    throw "Xbox 360 build failed with exit code $LASTEXITCODE."
}

$xex = Get-ChildItem -LiteralPath $root -Recurse -File -Filter default.xex |
    Where-Object { $_.LastWriteTime -ge $started } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $xex) {
    throw "Build completed but no new default.xex was found."
}

$hash = Get-FileHash -LiteralPath $xex.FullName -Algorithm SHA256
Write-Host "Relay-enabled Xbox 360 XEX: $($xex.FullName)"
Write-Host "SHA256: $($hash.Hash)"
