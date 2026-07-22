# Architecture

## Design goal

Keep the original game packet serializers and simulation authoritative while
replacing platform matchmaking and transport with a small common relay layer.

## Components

`CPlatformNetworkManagerRelay` implements the platform network-manager boundary
used by the existing game network manager. `NetworkPlayerRelay` implements the
existing network-player interface. `RelayTransport` owns the TCP connection,
handshake parsing, frame buffering, and per-peer routing.

The local relay server owns session discovery for one host and multiple peers.
It does not parse Minecraft gameplay packets.

## Handshake

V2 host:

```text
HOST <session-id> <build-id> V2\n
HOST <session-id> <build-id> V2 <access-token>\n
```

V2 client:

```text
JOIN <session-id> <build-id> V2\n
JOIN <session-id> <build-id> V2 <access-token>\n
```

The token suffix is optional when server authentication is disabled and
required when `CONSOLE_LEGACY_RELAY_TOKEN` is configured. Tokens are never
written to relay logs.

The server returns `WAITING` to a registered host and `READY` when at least one
matching client joins. Build mismatches receive an error and are disconnected.

Older single-peer join handshakes without `V2` are accepted into an existing
V2 hub for compatibility with the tested Xbox build.

## Host framing

The host connection is framed so replies can target a specific peer:

```text
byte 0      frame type
bytes 1-4   peer ID, unsigned big-endian
bytes 5-8   payload length, unsigned big-endian
bytes 9..   payload
```

Frame types:

| Type | Direction | Meaning |
| --- | --- | --- |
| `D` | both | Gameplay data for one peer |
| `J` | relay to host | Peer joined |
| `L` | relay to host | Peer left |

Client connections remain a raw gameplay byte stream after `READY`. The relay
wraps client-to-host data with the assigned peer ID and unwraps host responses.

## Identity normalization

Platform account identifiers have different representations and cannot be
compared directly. Relay players receive a canonical relay UID. The PS3 wire
path serializes that identity through a deterministic 64-bit representation.
Server-side login replaces platform-specific packet IDs with the relay UID.

Relay tracking containers compare player object identity rather than display
name, allowing multiple peers even when platform profile names collide.

## Visibility and movement

The normal tracker can send its first AddPlayer snapshot before a slower
console finishes world setup. After login initialization, the server replays
only player entities already considered visible to the new peer.

Compact relative movement packet layouts are avoided for relay players because
their ABI-sensitive packing differed across the tested platform builds. Regular
movement packets are used, with periodic absolute corrections.

## Chunk compatibility

The native clients use different compression paths. Relay builds send raw
block-region payloads so all platforms decode the same bytes. Full chunk biome
data is read from the payload tail rather than from a platform-dependent sparse
storage byte count.

## Security model

The server has two deployment profiles:

- Local/LAN mode keeps tokenless compatibility with existing tested builds.
- VPS mode requires a shared token and applies handshake, session, and peer
  limits.

Neither profile encrypts gameplay traffic or supplies persistent accounts,
server discovery, application-level traffic rate limiting, or denial-of-service
protection. Treat session IDs as routing labels, not passwords. Use a VPN or
source-IP firewall allowlist for external deployment.
