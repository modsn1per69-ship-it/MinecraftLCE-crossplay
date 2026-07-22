# External VPS relay

The VPS process forwards crossplay traffic. It does not simulate a world or
replace the PC game host. Keep the PC host running with its world open.

## Operator checklist

1. Give the VPS a stable public IPv4 address.
2. Deploy with Windows PowerShell, Docker Compose, or the included `systemd`
   unit.
3. Generate a random access token and store it only in the VPS environment.
4. Open TCP `61000` in both VPS firewalls, preferably only from player IPs.
5. Configure every game with the same address, port, session, build ID, and
   token.
6. Start the relay, PC host/world, Xbox client, and PS3 client in that order.
7. Watch logs for `auth=required`, `WAITING`, and each `peer ready` line.

## Server environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `CONSOLE_LEGACY_RELAY_BIND_ADDRESS` | `127.0.0.1` | Listener IPv4 address; use `0.0.0.0` on a VPS |
| `CONSOLE_LEGACY_RELAY_PORT` | `61000` | TCP listener port |
| `CONSOLE_LEGACY_RELAY_LOG_PATH` | `local-relay.log` | Persistent relay log path |
| `CONSOLE_LEGACY_RELAY_TOKEN` | empty | Shared access token; required for VPS mode |
| `CONSOLE_LEGACY_RELAY_MAX_SESSIONS` | `64` | Maximum simultaneous hosted sessions |
| `CONSOLE_LEGACY_RELAY_MAX_PEERS` | `8` | Maximum clients per hosted session |
| `CONSOLE_LEGACY_RELAY_MAX_PENDING_HANDSHAKES` | `64` | Maximum concurrent incomplete handshakes |
| `CONSOLE_LEGACY_RELAY_HANDSHAKE_TIMEOUT_MS` | `10000` | Time allowed to submit a handshake |

## Client environment

PC uses runtime environment variables:

```powershell
$env:CONSOLE_LEGACY_RELAY_ADDR = "VPS_PUBLIC_IPV4:61000"
$env:CONSOLE_LEGACY_RELAY_MODE = "local"
$env:CONSOLE_LEGACY_RELAY_SESSION = "my-world"
$env:CONSOLE_LEGACY_RELAY_BUILD_ID = "584111F7-1.0.10.0-lce1.2.3-net495-proto39"
$env:CONSOLE_LEGACY_RELAY_TOKEN = "THE_SHARED_TOKEN"
```

Xbox 360 and PS3 use the corresponding compile-time defaults, including:

```text
CONSOLE_LEGACY_RELAY_ADDR_DEFAULT="VPS_PUBLIC_IPV4:61000"
CONSOLE_LEGACY_RELAY_TOKEN_DEFAULT="THE_SHARED_TOKEN"
```

Use a numeric IPv4 address for console builds. Rebuild them after changing the
address or token.

## Token rotation

1. Stop new joins.
2. Generate a new random token.
3. Update the VPS environment or `.env` file.
4. Update PC configuration and rebuild both console clients.
5. Restart the relay and test all three clients.

The token is basic access control, not encryption. Anyone who can read a client
executable or capture unencrypted relay traffic may recover it. Prefer a VPN or
strict source-IP firewall rules.

## Health and logs

Docker:

```bash
docker compose ps
docker compose logs -f legacy-crossplay-relay
```

systemd:

```bash
sudo systemctl status legacy-crossplay-relay
sudo journalctl -u legacy-crossplay-relay -f
```

The relay stores no worlds or accounts. Back up only configuration and logs;
Minecraft world saves remain with the PC host.
