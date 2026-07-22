# Changelog

## Unreleased

- Added opt-in authenticated external VPS relay handshakes.
- Added handshake, session, and peer limits for external deployments.
- Added portable .NET 8, Docker Compose, and Linux `systemd` deployment files.
- Added regression coverage for both tokenless LAN and authenticated VPS
  three-peer sessions.

## 0.1.0 - 2026-07-22

- Initial patch-only release for PC, Xbox 360/Xenia, and PS3/RPCS3.
- Added multi-peer local relay protocol and server.
- Added cross-platform player identity and third-player visibility fixes.
- Added relay movement compatibility and absolute correction.
- Added raw chunk transport and biome-tail compatibility fixes.
- Added baseline verification, patch application, relay build, and relay test
  scripts.
- Excluded all game binaries, proprietary SDK content, launchers, custom UI,
  logos, splash messages, mods, and untested platform ports.
