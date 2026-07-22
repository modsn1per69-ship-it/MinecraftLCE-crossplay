# Legacy Console Crossplay Patches

Patch-only crossplay support for a matching Legacy Console Edition source
baseline. The tested topology is one PC host with Xbox 360/Xenia and PS3/RPCS3
clients in the same relay session.

This repository intentionally contains no game executable, XEX, PKG, ISO,
resource archive, copyrighted game asset, console SDK, firmware, encryption
key, license bypass, launcher, logo replacement, or custom menu artwork.

## Tested baseline

All participants must use the same gameplay and network baseline:

| Field | Required value |
| --- | --- |
| Xbox title ID | `584111F7` |
| Game version | `1.0.10.0` |
| LCE source baseline | `1.2.3` |
| Minecraft net version | `495` |
| Packet protocol | `39` |
| Relay build ID | `584111F7-1.0.10.0-lce1.2.3-net495-proto39` |

Do not mix title updates or source revisions. A client with a different build
ID is rejected before gameplay packets are forwarded.

## What the patch changes

- Adds a platform-independent TCP relay transport and network manager adapter.
- Supports one host and multiple relay peers in the same session.
- Gives relay players stable platform-neutral identities.
- Replays initial player visibility data after login to prevent the third
  player from seeing a frozen or missing host.
- Uses regular movement packets and periodic absolute corrections for relay
  players instead of ABI-sensitive compact movement packets.
- Sends chunk regions in one raw cross-platform format instead of mixing the
  Windows and Xbox compression implementations.
- Reads the biome map from the full chunk payload tail to avoid the PC-only
  color corruption seen with differing sparse storage counts.
- Routes the normal multiplayer menus through the relay policy when the
  `CONSOLE_LEGACY_RELAY` build flag is enabled.
- Keeps the original title screen, logos, splash text, game menus, worlds, and
  entitlement handling.

## Repository layout

```text
patches/
  crossplay-core.patch       Changes to existing source files
  relay/                     New relay adapter source files
relay/
  LocalRelayServer.cs        Patch-only local multi-peer relay
scripts/
  apply-patches.ps1          Applies the patch to your legal source tree
  remove-patches.ps1         Reverses the source patch
  build-relay.ps1            Builds the local relay and integration test
  start-relay.ps1            Starts a local or LAN relay
  build-xbox360.ps1          Validates and invokes an installed Xbox toolchain
tests/
  RelayHubIntegrationTest.cs Multi-peer routing regression test
docs/
  APPLYING.md                Detailed patch instructions
  BUILDING.md                PC, Xbox 360, and PS3 build notes
  USING.md                   Host/join instructions
  TROUBLESHOOTING.md         Common failures and fixes
  ARCHITECTURE.md            Protocol and implementation overview
  LEGAL.md                   Distribution boundaries
```

## Quick start

Requirements:

- Windows PowerShell 5.1 or newer.
- Git for applying the unified patch.
- A legally obtained, buildable copy of the matching source baseline.
- Your own legally obtained game files for emulator use.
- The licensed platform toolchain required by the source project for Xbox 360
  or PS3 builds. Those SDKs are not included or linked here.

Apply the patch from PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\apply-patches.ps1 -SourceRoot "D:\path\to\your\source"
```

Build and test the relay:

```powershell
.\scripts\build-relay.ps1
.\scripts\test-relay.ps1
```

Start it for clients on the same PC only:

```powershell
.\scripts\start-relay.ps1
```

Start it for Xenia/RPCS3 or physical consoles on the LAN:

```powershell
.\scripts\start-relay.ps1 -BindAddress 0.0.0.0 -Port 61000
```

Then configure every build with the same endpoint, session ID, and build ID.
PC reads environment variables at runtime. Console builds use the equivalent
compile-time defaults because they do not have a normal desktop environment.

```text
CONSOLE_LEGACY_RELAY_ADDR=192.168.1.50:61000
CONSOLE_LEGACY_RELAY_MODE=local
CONSOLE_LEGACY_RELAY_SESSION=my-world
CONSOLE_LEGACY_RELAY_BUILD_ID=584111F7-1.0.10.0-lce1.2.3-net495-proto39
```

The host uses the normal Create/Load World flow with online multiplayer
enabled. Clients use the normal Join flow. See [Using the relay](docs/USING.md)
for the exact order.

## Build outputs are private

The patch produces different build artifacts on each platform, but those
artifacts must stay out of this repository:

- PC: the source project's normal Windows executable and runtime files.
- Xbox 360: a locally built `default.xex`.
- PS3: a locally built executable/package suitable for the user's own dump.

Do not open an issue asking for an XEX, PKG, game archive, SDK, firmware, key,
or commercial game file. This project cannot provide them.

## Current scope

The tested release is PC host plus Xbox 360/Xenia and PS3/RPCS3 clients. Wii U,
PS Vita, PS4, Xbox One, public server browsing, accounts, friends, launchers,
mods, shaders, custom branding, and UI redesigns are outside this release.

The local relay is plaintext and unauthenticated. Use it on a trusted LAN. Do
not expose port `61000` directly to the public Internet without adding access
control, encryption, rate limiting, and operational monitoring.

## Verification status

- Local multi-peer relay handshake and routing: automated test included.
- Build mismatch rejection: automated test included.
- PC, Xbox 360/Xenia, and PS3/RPCS3 in one session: verified on the matching
  local source builds used to produce this patch set.
- Third-player visibility, movement, identity, chunk transfer, and biome color
  fixes: included in `crossplay-core.patch`.

## Community and support

- Join the project Discord: [discord.gg/2rvruaWDXk](https://discord.gg/2rvruaWDXk)
- Support development: [buymeacoffee.com/sn1per](https://buymeacoffee.com/sn1per)

This is an independent compatibility project and is not affiliated with or
endorsed by Mojang, Microsoft, Sony, 4J Studios, RPCS3, or the Xenia project.
