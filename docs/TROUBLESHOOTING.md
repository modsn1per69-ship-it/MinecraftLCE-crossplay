# Troubleshooting

## Patch does not apply

Run:

```powershell
.\scripts\verify-baseline.ps1 -SourceRoot "D:\LCE\source"
.\scripts\apply-patches.ps1 -SourceRoot "D:\LCE\source" -CheckOnly
```

If hashes differ, use a clean copy of the tested baseline. Do not use
`git apply --reject` and blindly combine rejected hunks. The affected files
contain packet layouts and state transitions where a partial patch can appear
to work and later corrupt the session.

## Relay does not start

- Run `scripts/build-relay.ps1` and read the compiler error.
- Check whether another process already owns TCP port `61000`:

```powershell
Get-NetTCPConnection -LocalPort 61000 -ErrorAction SilentlyContinue
```

- Try a different port and rebuild console defaults to match it.

## Console cannot reach the relay

- Bind the relay to `0.0.0.0`, not `127.0.0.1`.
- Use the PC's LAN IPv4 address in console builds.
- Keep PC and console/emulator on the same subnet for the first test.
- Allow inbound TCP on the selected port for Private networks.
- Do not use a public hostname until LAN testing is reliable.

Test the listener from another PC:

```powershell
Test-NetConnection 192.168.1.50 -Port 61000
```

## Host stays on WAITING

No client completed a matching join handshake. Check:

- session ID spelling and capitalization
- relay address and port
- build ID on every platform
- firewall profile
- whether the console binary was actually rebuilt after changing defaults

## ERROR build-mismatch

Every client must use:

```text
584111F7-1.0.10.0-lce1.2.3-net495-proto39
```

If you intentionally port the patch to another baseline, assign a new build
ID. Never weaken the build check to make mismatched packet layouts connect.

## Client loops on Connecting to host

- Confirm the relay log shows `READY` for that peer.
- Confirm the host entered an online world after starting its `HOST` handshake.
- Confirm the client uses the join flow, not a second host build configuration.
- Delete only emulator network caches known to be safe for that emulator; do
  not delete game saves as a first response.

## A joining client closes the game

Most often this is a stale or mismatched executable. Record SHA-256 hashes and
rebuild all three targets from one patched tree. Also check that the Xbox XEX
was produced by the Xbox configuration rather than copied from an old package.

## Third player cannot see the host

Confirm the patch contains all of these pieces:

- `EntityTracker::resendPlayersTo`
- `TrackedEntity::resendPlayerTo`
- relay canonical player UIDs in `PendingConnection`
- per-peer relay IDs in `NetworkPlayerRelay`
- host frame routing by peer ID in `RelayTransport`
- pointer identity hashing for relay players in `Player.cpp`

Applying only the transport files is not enough.

## Player freezes at an old position

Confirm the relay build uses regular movement packet layouts for players and
the periodic absolute correction in `TrackedEntity.cpp`. A client compiled
without that hunk may still join but fail to interpret compact movement from a
different platform ABI.

## Only a small area of the world loads

Confirm both sender and receiver were rebuilt with the raw
`BlockRegionUpdatePacket` relay path. Mixing a raw-packet client and a native
compression client causes incomplete chunks or disconnects.

## Grass or foliage changes color on PC

Confirm the `LevelChunk.cpp` biome-tail fix is present and every platform uses
the same chunk patch. This symptom occurs when the biome map is read at a
platform-dependent sparse-data offset.

## PS3 trial/full-version prompt

This patch does not alter entitlement checks. Use a valid full-version setup
from your own legally obtained game. The relay patch cannot convert a trial
into a full game.

## Useful report template

```text
Host platform:
Client platforms:
Source baseline:
PC SHA-256:
Xbox SHA-256:
PS3 SHA-256:
Relay commit:
Build ID:
Session ID:
First failing action:
Expected result:
Actual result:
Relay log attached:
Emulator logs attached:
```
