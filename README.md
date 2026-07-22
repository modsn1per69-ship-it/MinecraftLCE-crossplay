# Legacy Console Crossplay Patches

Patch-only crossplay support for a matching Minecraft Legacy Console Edition
source baseline. The verified topology is one PC host with Xbox 360/Xenia and
PS3/RPCS3 clients in the same relay session.

This is the complete setup guide. The files in `docs/` provide the same topics
as smaller reference pages, but a new user can install, patch, build, run, and
troubleshoot the project by following this README from top to bottom.

## Community and support

- Discord: [discord.gg/2rvruaWDXk](https://discord.gg/2rvruaWDXk)
- Buy Me a Coffee: [buymeacoffee.com/sn1per](https://buymeacoffee.com/sn1per)

## Release scope

This repository contains only:

- original relay server and transport source
- source-level compatibility patches
- patch, build, verification, and test scripts
- optional Windows/Linux VPS deployment files
- documentation and an automated three-peer relay regression test

It does not contain a game executable, XEX, PKG, EBOOT, SELF, ISO, resource
archive, copyrighted game asset, complete game source tree, console SDK,
firmware, encryption key, license bypass, launcher, custom logo, or menu mod.

## Exact tested baseline

The three platform builds use different retail-facing version labels. They are
compatible here because they were built from the matching source revision and
share Minecraft net version `495`, packet protocol `39`, and the same relay
build ID.

### Platform builds

| Platform | Exact tested game/build identity | Build target and notes |
| --- | --- | --- |
| PC host | Source-built Windows64 port, common product version `1.3.0495.0` | `Release|x64`, `_WINDOWS64`, Visual C++ `v110`; this is the native LCE Windows source target, not Java Edition or Bedrock Edition |
| Xbox 360 | Minecraft: Xbox 360 Edition `1.0.10.0`, title ID `584111F7` | Locally rebuilt `Release|Xbox 360` XEX; source/LCE baseline `1.2.3`, net `495`, protocol `39`; tested XEX reported media ID `00000000` and used Xbox 360 XDK `2.0.21256.0` libraries |
| PlayStation 3 | Minecraft: PlayStation 3 Edition update `1.84`, serial `BLES01976`, `APP_VER=01.84`, base `VERSION=01.00` | Official update `A0184` with a locally rebuilt relay-enabled executable from the matching source; net `495`, protocol `39` |

The PC version string is derived from the tested common source values:
`VER_FILEVERSION_STRING=1.3`, product build `495`, network build `495`, and QFE
`0`. The resulting common build string is `1.3.0495.0`.

### Shared compatibility contract

| Field | Required value |
| --- | --- |
| LCE source baseline | `1.2.3` |
| Minecraft net version | `495` |
| Packet protocol | `39` |
| Xbox title ID used by the build identity | `584111F7` |
| Relay build ID | `584111F7-1.0.10.0-lce1.2.3-net495-proto39` |

### Recorded emulator environment

These emulator versions document the successful test environment. They are not
part of the packet protocol and later compatible emulator builds may also work.

| Emulator | Recorded tested build |
| --- | --- |
| Xenia | `master@95a5c3ee2`, built 18 February 2026 |
| RPCS3 | `v0.0.41-19595-9b3a916a Alpha` |

Do not mix a different Xbox title update, PS3 update, PC source revision, net
version, protocol, or stale executable. A build ID match is necessary, but it
does not make unrelated packet layouts compatible.

## What the patch changes

- Adds a platform-independent TCP relay transport and network manager adapter.
- Supports one authoritative host and multiple relay peers in one session.
- Gives relay players stable platform-neutral identities.
- Replays initial player visibility state so a third player can see the host.
- Uses compatible movement packets plus periodic absolute corrections.
- Sends chunk regions in a common raw format across platform compression ABIs.
- Reads the biome map from the full chunk payload tail, preventing PC biome
  colors from cycling or corrupting.
- Routes the normal multiplayer flow through the relay policy when
  `CONSOLE_LEGACY_RELAY` is enabled.
- Supports optional shared-token authentication and bounded relay resources for
  external VPS deployments without changing tokenless local/LAN behavior.
- Keeps the original title screen, worlds, menus, saves, and entitlement logic.

## Repository layout

```text
patches/
  baseline.sha256          Hashes for the exact required source files
  crossplay-core.patch     Changes to existing source and project files
  relay/                   Seven new relay adapter source files
relay/
  LocalRelayServer.cs      Local multi-peer TCP relay
  LegacyCrossplayRelay.csproj
scripts/
  verify-baseline.ps1      Verifies the legal source tree before patching
  apply-patches.ps1        Checks and applies the patch
  remove-patches.ps1       Reverses the patch
  build-relay.ps1          Builds the local relay and test executable
  test-relay.ps1           Runs the three-peer routing regression test
  start-relay.ps1          Starts the relay
  publish-relay.ps1        Publishes the .NET 8 relay for a VPS
  build-xbox360.ps1        Validates and invokes an installed Xbox toolchain
tests/
  RelayHubIntegrationTest.cs
docs/                      Focused reference pages
deploy/systemd/            Linux service and environment templates
Dockerfile                 Linux container build
docker-compose.yml         Authenticated VPS container deployment
```

## Requirements

You need:

- Windows PowerShell 5.1 or newer
- Git for Windows
- a clean, legally obtained copy of the matching buildable source baseline
- your own legally obtained game files for emulator or console testing
- Visual Studio/toolsets expected by the source snapshot
- a Windows .NET Framework C# compiler for the relay
- .NET 8 SDK for portable Windows/Linux relay publishing, or Docker Engine with
  Docker Compose on the VPS
- the licensed platform toolchain required by the source project for an Xbox
  360 or PS3 build
- Xenia or RPCS3 when testing emulator clients

This project cannot provide proprietary SDKs, game files, firmware, keys,
certificates, or signing material.

## Complete setup

### 1. Download this patch repository

```powershell
git clone https://github.com/modsn1per69-ship-it/MinecraftLCE-crossplay.git
cd MinecraftLCE-crossplay
Set-ExecutionPolicy -Scope Process Bypass
```

The execution-policy change applies only to the current PowerShell process.

### 2. Prepare a clean source tree

Do not start with a launcher fork, branding mod, gameplay mod, older relay
experiment, partially patched copy, or another title update. Make a separate
backup before changing anything.

The source root must contain at least:

```text
your-source/
  MinecraftConsoles.sln
  Minecraft.Client/
    Minecraft.Client.vcxproj
    Common/Network/GameNetworkManager.cpp
  Minecraft.World/
    LevelChunk.cpp
```

Examples below use `D:\LCE\source`. Replace that path with your own source
directory.

### 3. Verify the exact source baseline

```powershell
.\scripts\verify-baseline.ps1 -SourceRoot "D:\LCE\source"
```

Every listed file should report `OK`, followed by:

```text
All patched baseline files match.
```

`MISMATCH` means the file differs from the tested revision. `MISSING` means the
source tree is incomplete or the wrong folder was selected. Do not force the
patch onto that tree.

### 4. Check the patch without changing files

```powershell
.\scripts\apply-patches.ps1 -SourceRoot "D:\LCE\source" -CheckOnly
```

Expected result:

```text
Patch check passed. No files were changed.
```

### 5. Apply the patch

```powershell
.\scripts\apply-patches.ps1 -SourceRoot "D:\LCE\source"
```

The script:

1. Applies `patches/crossplay-core.patch` to the existing source and projects.
2. Creates `Minecraft.Client/Common/Network/Relay/`.
3. Copies the seven relay adapter files into that directory.

Confirm the result:

```powershell
Select-String `
  -Path "D:\LCE\source\Minecraft.Client\Minecraft.Client.vcxproj" `
  -Pattern "CONSOLE_LEGACY_RELAY"

Select-String `
  -Path "D:\LCE\source\Minecraft.Client\Common\Network\GameNetworkManager.cpp" `
  -Pattern "CPlatformNetworkManagerRelay"
```

The relay directory must contain:

```text
LegacyRelayPolicy.h
NetworkPlayerRelay.cpp
NetworkPlayerRelay.h
PlatformNetworkManagerRelay.cpp
PlatformNetworkManagerRelay.h
RelayTransport.cpp
RelayTransport.h
```

### 6. Choose one relay configuration

All participants must use exactly the same address, port, session ID, and build
ID.

For all three clients running as emulators on the relay PC:

```text
Address: 127.0.0.1:61000
Session: my-world
Mode: local
Build: 584111F7-1.0.10.0-lce1.2.3-net495-proto39
```

For a physical console or another computer on the same LAN, find the relay
PC's address:

```powershell
Get-NetIPAddress -AddressFamily IPv4 |
  Where-Object { $_.IPAddress -notlike "127.*" }
```

Use that LAN IPv4 address, for example `192.168.1.50:61000`, in every console
build. Give the relay PC a stable DHCP reservation if possible.

PC reads these settings at runtime:

```powershell
$env:CONSOLE_LEGACY_RELAY_ADDR = "127.0.0.1:61000"
$env:CONSOLE_LEGACY_RELAY_MODE = "local"
$env:CONSOLE_LEGACY_RELAY_SESSION = "my-world"
$env:CONSOLE_LEGACY_RELAY_BUILD_ID = "584111F7-1.0.10.0-lce1.2.3-net495-proto39"
```

Xbox 360 and PS3 builds use compile-time defaults. Add these definitions to
both the client and world-library build for each console, replacing the address
when the relay is not on the same PC:

```text
CONSOLE_LEGACY_RELAY
CONSOLE_LEGACY_RELAY_ADDR_DEFAULT="127.0.0.1:61000"
CONSOLE_LEGACY_RELAY_MODE_DEFAULT="local"
CONSOLE_LEGACY_RELAY_SESSION_DEFAULT="my-world"
CONSOLE_LEGACY_RELAY_BUILD_DEFAULT="584111F7-1.0.10.0-lce1.2.3-net495-proto39"
```

For local or trusted-LAN operation, leave
`CONSOLE_LEGACY_RELAY_TOKEN_DEFAULT` undefined or empty. An external VPS uses
the additional authenticated setting documented below.

Do not remove or weaken the build-ID check. It prevents obviously mismatched
clients from exchanging gameplay packets.

### 7. Build and test the relay

From the patch repository root:

```powershell
.\scripts\build-relay.ps1
.\scripts\test-relay.ps1
```

A passing test prints:

```text
RELAY_HUB_3_PEER_OK
RELAY_HUB_3_PEER_AUTH_OK
Relay integration tests passed.
```

The first marker verifies backward-compatible tokenless LAN routing. The second
verifies rejection of a wrong token followed by authenticated three-peer
routing. Both runs also keep a second hosted session active and verify that its
traffic remains routed to its own host.

Generated relay executables are written to `relay/bin/` and are intentionally
ignored by Git.

### 8. Start the relay

Same-PC emulator testing:

```powershell
.\scripts\start-relay.ps1 `
  -BindAddress 127.0.0.1 `
  -Port 61000
```

LAN or physical-console testing:

```powershell
.\scripts\start-relay.ps1 `
  -BindAddress 0.0.0.0 `
  -Port 61000
```

For LAN testing, allow inbound TCP `61000` only on the Windows Private network
profile. Run this once from an elevated PowerShell window if a rule is needed:

```powershell
New-NetFirewallRule `
  -DisplayName "Legacy Console Crossplay Relay" `
  -Direction Inbound `
  -Action Allow `
  -Protocol TCP `
  -LocalPort 61000 `
  -Profile Private
```

Do not forward this port through the router. The included relay is not hardened
for unauthenticated public-Internet exposure.

### Optional: Run the relay on an external VPS

The external relay is still a transport service, not a dedicated Minecraft
server. The PC game host must remain running with its world open. Every game
connects outbound to the VPS, so players do not forward ports at home.

The VPS profile adds an access token, handshake timeout, pending-handshake
limit, session limit, and per-session peer limit. Its authenticated routing is
covered by the same three-peer regression test as local mode. Gameplay traffic
is still not encrypted. Use a VPN when possible; otherwise use the VPS firewall
to allow only participating public IP addresses.

#### Windows VPS deployment

From an elevated PowerShell window on a Windows VPS:

```powershell
git clone https://github.com/modsn1per69-ship-it/MinecraftLCE-crossplay.git
Set-Location .\MinecraftLCE-crossplay
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build-relay.ps1
$env:CONSOLE_LEGACY_RELAY_TOKEN = "REPLACE_WITH_A_RANDOM_TOKEN"
.\scripts\start-relay.ps1 `
  -VpsMode `
  -Port 61000
```

The script binds VPS mode to `0.0.0.0`, requires a nonempty token, and keeps the
normal local defaults unchanged when `-VpsMode` is omitted. Run it through Task
Scheduler or a service manager if it must restart automatically after reboot.
Keeping the token in the environment avoids placing it in relay command-line
arguments, but administrators must still protect the service configuration.
Allow only the participating public IP addresses through Windows Firewall:

```powershell
New-NetFirewallRule `
  -DisplayName "Legacy Console Crossplay VPS" `
  -Direction Inbound `
  -Action Allow `
  -Protocol TCP `
  -LocalPort 61000 `
  -RemoteAddress "PLAYER_PUBLIC_IP"
```

#### Docker Compose deployment

On a Linux VPS with Git, Docker Engine, and Docker Compose:

```bash
git clone https://github.com/modsn1per69-ship-it/MinecraftLCE-crossplay.git
cd MinecraftLCE-crossplay
cp .env.example .env
openssl rand -hex 32
```

Put the generated value after `CONSOLE_LEGACY_RELAY_TOKEN=` in `.env`. Do not
commit or share that file. Then start the service:

```bash
docker compose up -d --build
docker compose ps
docker compose logs -f legacy-crossplay-relay
```

The log must report:

```text
listening 0.0.0.0:61000 auth=required
```

To update later:

```bash
git pull --ff-only
docker compose up -d --build
```

#### Native .NET 8 and systemd deployment

Install the .NET 8 runtime on the VPS, clone the repository, and publish:

```bash
dotnet publish relay/LegacyCrossplayRelay.csproj \
  --configuration Release \
  --output publish
sudo useradd --system --home /opt/legacy-crossplay-relay \
  --shell /usr/sbin/nologin legacy-relay
sudo install -d -o legacy-relay -g legacy-relay \
  /opt/legacy-crossplay-relay /var/log/legacy-crossplay-relay
sudo cp -a publish/. /opt/legacy-crossplay-relay/
sudo cp deploy/systemd/legacy-crossplay-relay.service /etc/systemd/system/
sudo cp deploy/systemd/legacy-crossplay-relay.env.example \
  /etc/legacy-crossplay-relay.env
sudo chmod 600 /etc/legacy-crossplay-relay.env
```

Edit `/etc/legacy-crossplay-relay.env`, replace the example token with a random
token, then enable the service:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now legacy-crossplay-relay
sudo systemctl status legacy-crossplay-relay
sudo journalctl -u legacy-crossplay-relay -f
```

#### VPS firewall

Open TCP `61000` in both the provider firewall and the VPS operating-system
firewall. Prefer one allow rule per player's public IP. For example with UFW:

```bash
sudo ufw allow from PLAYER_PUBLIC_IP to any port 61000 proto tcp
```

Do not expose database, management, RDP, SSH, or unrelated ports for the relay.
If TCP `61000` must be open to everyone, treat the token only as basic access
control: it does not encrypt gameplay or protect against traffic flooding.

#### Configure the players

Use the VPS's static numeric public IPv4 address. Xbox 360 and PS3 builds do not
resolve DNS names in this transport path.

PC host configuration:

```powershell
$env:CONSOLE_LEGACY_RELAY_ADDR = "VPS_PUBLIC_IPV4:61000"
$env:CONSOLE_LEGACY_RELAY_MODE = "local"
$env:CONSOLE_LEGACY_RELAY_SESSION = "my-world"
$env:CONSOLE_LEGACY_RELAY_BUILD_ID = "584111F7-1.0.10.0-lce1.2.3-net495-proto39"
$env:CONSOLE_LEGACY_RELAY_TOKEN = "THE_TOKEN_FROM_THE_VPS"
```

`local` selects the direct relay protocol; it does not require the relay to be
on the same machine.

Add these compile-time defaults to both the client and world-library console
builds:

```text
CONSOLE_LEGACY_RELAY_ADDR_DEFAULT="VPS_PUBLIC_IPV4:61000"
CONSOLE_LEGACY_RELAY_MODE_DEFAULT="local"
CONSOLE_LEGACY_RELAY_SESSION_DEFAULT="my-world"
CONSOLE_LEGACY_RELAY_BUILD_DEFAULT="584111F7-1.0.10.0-lce1.2.3-net495-proto39"
CONSOLE_LEGACY_RELAY_TOKEN_DEFAULT="THE_TOKEN_FROM_THE_VPS"
```

Rebuild Xbox 360 and PS3 after changing the VPS IPv4 address or token. The
token is embedded in those executables, so rotate it and rebuild every client
if a build is shared outside the intended group.

Start and join in the same order as the local test: relay, PC host/world, Xbox,
then PS3. All players must use the same address, port, token, session, and build
ID. See [`docs/VPS.md`](docs/VPS.md) for the compact operator checklist.

### 9. Build and start the PC host

The exact tested PC target is the native source `Release|x64` Windows64 port:

```text
Configuration: Release
Platform: x64
Define: _WINDOWS64
Toolset: v110
Common version: 1.3.0495.0
Net version: 495
Protocol: 39
```

Build steps:

1. Open `D:\LCE\source\MinecraftConsoles.sln` with the Visual Studio/toolset
   expected by the source snapshot.
2. Select `Release|x64`.
3. Confirm both `Minecraft.Client` and `Minecraft.World` define
   `CONSOLE_LEGACY_RELAY`.
4. Build the solution.
5. Keep the resulting executable with all matching runtime DLLs, `Common`
   resources, audio files, and other source-project output.
6. Run it from that complete output directory.

Start the host from PowerShell after setting the runtime values:

```powershell
$env:CONSOLE_LEGACY_RELAY_ADDR = "127.0.0.1:61000"
$env:CONSOLE_LEGACY_RELAY_MODE = "local"
$env:CONSOLE_LEGACY_RELAY_SESSION = "my-world"
$env:CONSOLE_LEGACY_RELAY_BUILD_ID = "584111F7-1.0.10.0-lce1.2.3-net495-proto39"
Set-Location "D:\LCE\source\path\to\complete\Windows64\output"
& ".\MINECRAFT.CLIENT.EXE"
```

Create or load a world through the normal menu and enable online multiplayer.
The relay log should show a `HOST` handshake and `WAITING` until a client joins.

### 10. Build and start Xbox 360 / Xenia

The exact tested Xbox baseline is:

```text
Edition/version: Minecraft: Xbox 360 Edition 1.0.10.0
Title ID: 584111F7
Configuration: Release|Xbox 360
Source/LCE baseline: 1.2.3
Net version: 495
Protocol: 39
Test XDK libraries: 2.0.21256.0
Test XEX media ID: 00000000
```

An Xbox 360 XEX must be built by the Xbox 360 toolchain integrated with the
source project. A normal desktop compiler cannot emit `default.xex`.

1. Add the compile-time relay defaults from step 6 to both Xbox client and
   world-library projects.
2. Use the same session and build ID as PC and PS3.
3. Build `Release|Xbox 360` with your licensed toolchain.
4. Confirm the XEX timestamp changed and record its SHA-256 hash.
5. Keep the XEX beside the matching resources from your own legal build/dump.
6. Boot it in Xenia and confirm the title ID is `584111F7` and the title version
   is `1.0.10.0`.
7. Use the normal multiplayer/join flow after the PC host is waiting.

The helper can validate and invoke the build after copying it into the source
tree:

```powershell
New-Item -ItemType Directory -Force "D:\LCE\source\scripts" | Out-Null
Copy-Item ".\scripts\build-xbox360.ps1" `
  "D:\LCE\source\scripts\build-xbox360.ps1" -Force
& "D:\LCE\source\scripts\build-xbox360.ps1" -Configuration Release
```

The helper does not download or install an SDK, title update, source tree, or
game data.

### 11. Build and start PS3 / RPCS3

The exact tested PS3 baseline is:

```text
Edition: Minecraft: PlayStation 3 Edition
Serial: BLES01976
Official update: 1.84 / A0184
PARAM.SFO APP_VER: 01.84
PARAM.SFO VERSION: 01.00
Net version: 495
Protocol: 39
```

1. Prepare your own legal `BLES01976` game dump.
2. Install or apply official update `1.84` using the supported process for your
   own console or RPCS3 setup.
3. Confirm RPCS3 reports `Serial: BLES01976` and
   `APP_VER=01.84 VERSION=01.00` before testing crossplay.
4. Use the PS3 toolchain and project integration appropriate to your legal
   source environment.
5. Compile both client and world code with `CONSOLE_LEGACY_RELAY` and all
   defaults from step 6.
6. Add all seven files from
   `Minecraft.Client/Common/Network/Relay/` to the PS3 client build.
7. Build the executable/package against your own game data.
8. Install or boot the local result in RPCS3, then use the normal join flow.

The original full game project depends on platform libraries not supplied by
PSL1GHT. `ps3dev/ps3toolchain` is useful for standalone network probes but is
not a drop-in replacement for every original project dependency.

This patch leaves entitlement handling unchanged. It does not turn a trial
into a full game or remove license checks.

### 12. Join all three players

Use this order for the first complete test:

1. Start `LocalRelayServer` on the PC.
2. Start the PC build with the exact environment values.
3. Create or load an online world on PC.
4. Wait for the relay to report the host as `WAITING`.
5. Boot the matching Xbox XEX and join through the normal multiplayer menu.
6. Wait until the Xbox player is fully visible in the world.
7. Boot the matching PS3 `1.84` build and join through the normal join menu.
8. Confirm all three clients remain connected before moving away from spawn.

The order is not a protocol requirement, but it produces clearer logs and
makes a stale build easier to identify.

### 13. Verify gameplay from every screen

Check all of the following:

- every player is visible and has a name
- PC movement is visible on Xbox and PS3
- Xbox movement is visible on PC and PS3
- PS3 movement is visible on PC and Xbox
- hits and health updates agree on all clients
- chat travels in both directions
- inventory and block changes replicate
- terrain continues loading after walking several chunks
- grass and foliage colors remain stable on PC
- disconnecting one client does not close or freeze the host
- a fourth connection is rejected cleanly if it exceeds the game/session limit

Save all three build hashes and the relay log after a known-good test.

## Manual patch application

When PowerShell scripts cannot be used:

```powershell
Set-Location "D:\LCE\source"
git -c core.autocrlf=false apply --check --whitespace=nowarn `
  "D:\MinecraftLCE-crossplay\patches\crossplay-core.patch"
git -c core.autocrlf=false apply --whitespace=nowarn `
  "D:\MinecraftLCE-crossplay\patches\crossplay-core.patch"
```

Then copy the seven files from `patches/relay/` into:

```text
Minecraft.Client/Common/Network/Relay/
```

Do not change their names or apply only the transport files. Visibility,
identity, movement, chunk, menu, and serialization changes in the core patch
are all part of the tested result.

## Removing the patch

If patched source files have not been edited further:

```powershell
.\scripts\remove-patches.ps1 -SourceRoot "D:\LCE\source"
```

The script first checks that the patch can reverse cleanly. If that check
fails, preserve your work and restore your backup or use your own source-control
history. Do not force a partial reverse.

## Troubleshooting

### Patch does not apply

Run both checks again:

```powershell
.\scripts\verify-baseline.ps1 -SourceRoot "D:\LCE\source"
.\scripts\apply-patches.ps1 -SourceRoot "D:\LCE\source" -CheckOnly
```

Use a clean copy of the tested source when hashes differ. Do not use
`git apply --reject` and blindly combine packet or state-machine hunks.

### Relay does not start

```powershell
.\scripts\build-relay.ps1
Get-NetTCPConnection -LocalPort 61000 -ErrorAction SilentlyContinue
```

Another process may own the port. If the port changes, every PC environment
value and console compile-time default must change with it.

### Console cannot reach the relay

- Bind to `0.0.0.0` for LAN clients, not `127.0.0.1`.
- Use the relay PC's LAN IPv4 address in physical-console builds.
- Keep all systems on the same subnet for the first test.
- Allow inbound TCP only on the Private firewall profile.
- Confirm the listener from another computer:

```powershell
Test-NetConnection 192.168.1.50 -Port 61000
```

For a VPS, replace the LAN address with its numeric public IPv4 address. Confirm
the provider firewall and operating-system firewall both permit TCP `61000`
from the player's current public IP.

### ERROR unauthorized

The VPS requires authentication and the client supplied no token or the wrong
token. Confirm PC uses `CONSOLE_LEGACY_RELAY_TOKEN` and both console builds use
the matching `CONSOLE_LEGACY_RELAY_TOKEN_DEFAULT`. Tokens are case-sensitive.
Do not post the token in an issue or attach an executable containing it.

### VPS container does not start

```bash
docker compose config
docker compose ps
docker compose logs legacy-crossplay-relay
```

`docker compose config` must resolve `CONSOLE_LEGACY_RELAY_TOKEN` from the local
`.env` file. The repository ignores `.env`; `.env.example` is only a template.

### Host remains on WAITING

No client completed a matching handshake. Check the exact session spelling,
address, port, build ID, firewall profile, and whether the console executable
was rebuilt after its defaults changed.

### ERROR build-mismatch

Every participant in this release must use:

```text
584111F7-1.0.10.0-lce1.2.3-net495-proto39
```

Never weaken the check just to connect another title update.

### Client loops on Connecting to host

- Confirm the relay log shows `READY` for that peer.
- Confirm PC is already inside an online world.
- Confirm the client chose Join, not a second Host flow.
- Confirm the game build matches the platform table exactly.

### Joining closes one game

This usually indicates a stale or mismatched executable. Rebuild all three
targets from one patched tree and compare their recorded SHA-256 hashes. On
Xbox, confirm the new file was produced by `Release|Xbox 360`, not copied from
an old package.

### Third player cannot see the host or usernames

Confirm the full patch includes player resend logic, canonical relay UIDs,
per-peer relay IDs, host routing by peer ID, and relay player pointer identity.
Applying only `RelayTransport` is not sufficient.

### Player is frozen at an old position

Rebuild every client with the regular relay movement-packet path and periodic
absolute corrections from `TrackedEntity.cpp`. A stale client can join and
still misread a compact platform-specific movement layout.

### Only a small area of the world loads

Both sender and receiver must contain the raw `BlockRegionUpdatePacket` relay
path. Mixing a raw relay client with a native compressed client causes missing
chunks or disconnects.

### Grass or foliage changes color on PC

Confirm the `LevelChunk.cpp` biome-tail fix is present and all clients were
rebuilt from that same patched source revision.

### PS3 reports the wrong version

RPCS3 must identify the tested game as:

```text
Title: Minecraft: PlayStation 3 Edition
Serial: BLES01976
APP_VER: 01.84
VERSION: 01.00
```

Do not test a different region/update while using this build ID.

## Reporting a problem

Include this information in an issue:

```text
Host platform:
Client platforms:
PC version/configuration:
Xbox title version and title ID:
PS3 serial and APP_VER:
Source baseline:
PC SHA-256:
Xbox XEX SHA-256:
PS3 executable SHA-256:
Relay commit:
Relay deployment (local, Docker, or systemd):
Authentication enabled (yes/no; never include the token):
Build ID:
Session ID:
First failing action:
Expected result:
Actual result:
Relay log attached:
Emulator logs attached:
```

Do not attach game binaries, game source, SDK files, firmware, keys, or
copyrighted assets.

## Architecture summary

`CPlatformNetworkManagerRelay` implements the existing platform network-manager
boundary. `NetworkPlayerRelay` implements the network-player interface.
`RelayTransport` owns TCP connection, handshake, frame buffering, and per-peer
routing. The local relay routes sessions and does not parse gameplay packets.

Host handshake:

```text
HOST <session-id> <build-id> V2
HOST <session-id> <build-id> V2 <access-token>
```

Client handshake:

```text
JOIN <session-id> <build-id> V2
JOIN <session-id> <build-id> V2 <access-token>
```

The token form is required only when the relay has
`CONSOLE_LEGACY_RELAY_TOKEN` configured. The server compares the supplied token
without logging it. Existing tokenless local/LAN handshakes remain supported
when server authentication is disabled.

The host receives framed peer traffic, while each client sees a raw gameplay
stream after `READY`. The game host remains authoritative for worlds, entities,
inventory, and simulation.

## Security

Local mode remains intended for development and trusted LAN use. VPS mode adds
a shared access token, handshake timeout, pending-handshake cap, session cap,
and peer cap, but it does not add encryption, account identities, traffic rate
limiting, or denial-of-service protection. Session IDs are routing labels, not
passwords. Prefer a VPN or strict source-IP firewall rules and never publish the
access token.

## Legal and distribution boundaries

The MIT license applies only to original files in this patch repository. It
does not grant rights to third-party source, assets, trademarks, SDKs, game
files, or locally produced build outputs.

Users must obtain source, game files, firmware, and toolchains lawfully and
follow the licenses and laws that apply in their jurisdiction. Do not upload a
locally built XEX, PKG, EBOOT, game executable, game archive, SDK, firmware,
key, certificate, or signing material to this repository or its releases.

Minecraft is a trademark of Mojang Synergies AB. Xbox and Microsoft are
trademarks of Microsoft. PlayStation is a trademark of Sony Interactive
Entertainment. This independent compatibility project is not affiliated with
or endorsed by Mojang, Microsoft, Sony, 4J Studios, RPCS3, or Xenia.
