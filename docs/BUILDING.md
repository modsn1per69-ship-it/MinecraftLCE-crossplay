# Building patched clients

This repository does not include or download proprietary console SDKs. Use the
toolchain required by your legally obtained source project and platform access.

Build every client from the same patched source revision. Do not combine a new
PC executable with an older XEX or PS3 build.

## Shared build requirements

All three targets need:

```text
CONSOLE_LEGACY_RELAY
CONSOLE_LEGACY_RELAY_BUILD_DEFAULT="584111F7-1.0.10.0-lce1.2.3-net495-proto39"
CONSOLE_LEGACY_RELAY_SESSION_DEFAULT="my-world"
CONSOLE_LEGACY_RELAY_ADDR_DEFAULT="192.168.1.50:61000"
CONSOLE_LEGACY_RELAY_MODE_DEFAULT="local"
```

Replace `192.168.1.50` with the LAN IPv4 address of the PC running the relay.
Keep the same build ID and session ID on every participant.

The patch already adds `CONSOLE_LEGACY_RELAY` to the tested Windows and Xbox
project configurations. For PS3 builds, make sure the platform compile command
also receives `-DCONSOLE_LEGACY_RELAY` and the default macros above.

## PC host

1. Open `MinecraftConsoles.sln` with the Visual Studio/toolset expected by the
   source snapshot.
2. Select the patched Windows x64 Release configuration.
3. Confirm `Minecraft.Client` and `Minecraft.World` both define
   `CONSOLE_LEGACY_RELAY`.
4. Build the solution.
5. Run the result from its complete output directory so the original runtime
   DLLs and `Common` resources are found.

PC can override relay settings at runtime:

```powershell
$env:CONSOLE_LEGACY_RELAY_ADDR = "127.0.0.1:61000"
$env:CONSOLE_LEGACY_RELAY_MODE = "local"
$env:CONSOLE_LEGACY_RELAY_SESSION = "my-world"
$env:CONSOLE_LEGACY_RELAY_BUILD_ID = "584111F7-1.0.10.0-lce1.2.3-net495-proto39"
& ".\MINECRAFT.CLIENT.EXE"
```

If the relay is on another machine, replace `127.0.0.1` with its LAN address.

## Xbox 360 / Xenia

An Xbox 360 executable must be produced by the Xbox 360 toolchain integrated
with the source project. A normal desktop Visual C++ compiler cannot emit an
XEX.

1. Set the compile-time relay address to the PC relay's LAN address.
2. Build the patched `Release|Xbox 360` configuration.
3. Confirm the output timestamp changed and record its SHA-256 hash.
4. Keep the XEX beside the matching resources from your own legal dump/build.
5. Start it with a current Xenia build configured for the title.

The helper validates the build configuration and invokes MSBuild when a
licensed Xbox SDK is already installed:

```powershell
.\scripts\build-xbox360.ps1 -Configuration Release
```

The helper does not obtain an SDK, source tree, title update, or game data.

## PS3 / RPCS3

The full game project uses platform APIs and libraries not supplied by
PSL1GHT. `ps3dev/ps3toolchain` is useful for standalone relay probes, but it is
not a drop-in replacement for every dependency in the original game project.

For a supported full build:

1. Use the PS3 toolchain and project integration appropriate to your legal
   source environment.
2. Add `-DCONSOLE_LEGACY_RELAY` to both client and world-library compilation.
3. Add the same compile-time address, session, mode, and build defaults used by
   PC and Xbox.
4. Ensure the new relay `.cpp` files are compiled and linked into the client.
5. Build the executable/package against your own legally obtained game data.
6. Install or boot it in RPCS3 using your own firmware and dump.

The patch keeps the original full-version entitlement handling. It does not
turn a trial build into a full game and does not remove license checks.

## Build identity discipline

Record a manifest for every test set:

```text
PC executable SHA-256:
Xbox XEX SHA-256:
PS3 executable/package SHA-256:
Relay source commit:
Game build ID:
Session ID:
```

When one platform is rebuilt, rebuild all three unless you can prove the packet
and ABI-facing code did not change. A stale console client can connect far
enough to show a loading screen and still fail or corrupt state later.
