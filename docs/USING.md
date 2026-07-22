# Using the local crossplay relay

## Network layout

The relay PC accepts one host and multiple clients. The game host remains the
authority for worlds, entities, inventory, and simulation. The relay only
frames and forwards the game's existing packet stream.

For an authenticated external relay, follow [VPS.md](VPS.md). The local steps
below remain unchanged and do not require a token.

```text
PC game host ----\
Xbox 360/Xenia ----> LocalRelayServer on TCP 61000
PS3/RPCS3 -------/
```

## 1. Choose the relay PC address

Open PowerShell on the relay PC:

```powershell
Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike "127.*" }
```

Use the IPv4 address on the same LAN as the emulator or console. Reserve that
address in the router or give the PC a stable local address so console builds
do not need to be rebuilt after DHCP changes.

## 2. Start the relay

Same-PC testing:

```powershell
.\scripts\start-relay.ps1 -BindAddress 127.0.0.1 -Port 61000
```

LAN testing:

```powershell
.\scripts\start-relay.ps1 -BindAddress 0.0.0.0 -Port 61000
```

Allow inbound TCP `61000` in Windows Firewall only for the Private network
profile. Do not expose the port on the router.

## 3. Start the PC host

Set the PC environment and start the game from the directory containing its
runtime files:

```powershell
$env:CONSOLE_LEGACY_RELAY_ADDR = "127.0.0.1:61000"
$env:CONSOLE_LEGACY_RELAY_MODE = "local"
$env:CONSOLE_LEGACY_RELAY_SESSION = "my-world"
$env:CONSOLE_LEGACY_RELAY_BUILD_ID = "584111F7-1.0.10.0-lce1.2.3-net495-proto39"
& ".\MINECRAFT.CLIENT.EXE"
```

Create or load a world through the normal menus and enable online multiplayer.
The relay log should show a `HOST` handshake followed by `WAITING`.

## 4. Join Xbox 360/Xenia

Boot the XEX built with the same relay address, session, and build ID. Open the
normal multiplayer/join flow. When it connects, the relay log shows a peer ID
and sends `READY` to the host.

Wait until the Xbox player is fully in the world before starting the next
client. This is not required by the protocol, but it makes failures easier to
identify during initial setup.

## 5. Join PS3/RPCS3

Boot the PS3 build produced from the same patched revision. Enter the normal
join flow. The PS3 relay path polls the synthetic compatible session and joins
it directly, avoiding the retired PSN friend-session lookup.

## 6. Verify the session

Check all of the following from every screen:

- Each player is visible and has a name.
- PC movement is visible on both console clients.
- Xbox movement is visible on PC and PS3.
- PS3 movement is visible on PC and Xbox.
- Hits and health updates agree on all clients.
- Chat travels in both directions.
- Walking several chunks away loads terrain on both consoles.
- Grass and foliage colors remain stable instead of cycling colors.
- Leaving one client does not close the host or freeze the remaining client.

Save the relay log and all three build hashes when reporting a failure.

## Session rules

- Session IDs are case-sensitive strings.
- Build IDs must match exactly.
- Only one host can own a session ID at a time.
- Peer IDs start at `2`; the host routes each framed response to the correct
  peer instead of broadcasting client-specific packets.
- Restart the relay between unrelated test runs to avoid confusing old logs.
