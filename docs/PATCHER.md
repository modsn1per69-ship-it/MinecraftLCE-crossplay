# Legacy Crossplay Patcher

Legacy Crossplay Patcher is an optional Windows desktop utility for applying
this repository's source patch. It keeps the existing script workflow
available and does not replace the platform toolchains.

## What it does

- accepts a Windows EXE, Xbox 360 XEX, PS3 PKG, or PS3 SELF/EBOOT
- validates the file signature locally and records its SHA-256 hash
- verifies the selected source against the tested 31-file baseline manifest
- creates a timestamped source backup before applying changes
- runs the same `crossplay-core.patch` compatibility check and application
- installs the eight relay adapter files
- writes a shared relay address, port, session, build ID, mode, and token to
  `LegacyRelayUserConfig.h`
- invokes `Release|x64`, `Release|Xbox 360`, or `Release|PS3` when the matching
  platform toolchain is installed
- includes PC, Xbox 360, PS3, relay/VPS, and troubleshooting guides

All processing is local. The selected game file is not uploaded or included in
the project.

## Important build boundary

The utility does not rewrite a signed XEX or PKG in place. The game file
identifies the target platform and provides a reproducible input hash. The
source patch must still be compiled by the toolchain required by that platform:

- PC uses the source project's Windows64 compiler/toolset.
- Xbox 360 requires the licensed Xbox 360 SDK and its MSBuild integration.
- PS3 requires the PS3 SDK/project integration used by the legal source
  environment. Packaging a new SELF into an update layout remains a separate
  platform step.

The patcher does not download an SDK, source tree, game file, firmware, key,
certificate, title update, or license bypass.

## Build the standalone EXE

Install the .NET 8 SDK, then run:

```powershell
.\scripts\build-patcher.ps1
```

The self-contained output is:

```text
patcher/publish/win-x64/LegacyCrossplayPatcher.exe
```

The EXE embeds only this repository's open patch data and relay source. It does
not need a separate .NET installation on the destination PC.

## Use

1. Open `LegacyCrossplayPatcher.exe`.
2. Add the existing game EXE, XEX, PKG, SELF, or EBOOT.
3. Select the clean matching source root containing
   `MinecraftConsoles.sln`.
4. Enter the relay settings used by every participant.
5. Select **Validate**.
6. Select **Apply patch**.
7. Select **Build client** when the matching platform toolchain is installed.
8. Use **Open output** to locate a newly written build result.

The source backup is stored under:

```text
<source-root>/LegacyCrossplayBackups/<timestamp>/
```

## PS3 toolchain discovery

The patcher uses MSBuild's registered PS3 platform when available. For a
portable PS3 MSBuild integration, set:

```powershell
$env:LCE_PS3_VCTARGETS_PATH = "D:\toolchains\MSBuild\Microsoft.Cpp\v4.0"
```

That directory must contain:

```text
Platforms/PS3/Microsoft.Cpp.PS3.targets
```

The patcher disables the old PS3 file-access tracker during builds because it
can fail to hook modern Windows processes after the linker has produced a valid
SELF.

## Safety and recovery

The app refuses to force the patch onto a mismatched source revision. If source
validation fails, obtain a clean copy of the exact tested baseline or port the
patch deliberately.

If a later manual edit or build fails, preserve the activity log and restore
the affected source files from the timestamped backup. Do not distribute game
binaries, proprietary source, SDK files, firmware, or signing material.
