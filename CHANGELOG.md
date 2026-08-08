# Changelog

## 0.2.3 - 2026-08-09

### Fixed

- Prevented Xbox 360 relay builds from entering platform DLC installation in
  `XUI_MultiGameJoinLoad.cpp` and `XUI_MultiGameCreate.cpp`.
- Routed the old XUI create scene's sign-in, multiplayer privilege, online-game,
  and user-content checks through `LegacyRelayPolicy`.
- Expanded baseline verification and backups from 30 to 31 files to include
  `XUI_MultiGameCreate.cpp`.
- Made the PowerShell apply/remove scripts independent of unrelated parent Git
  repositories.

## 0.2.2 - 2026-08-07

### Fixed

- Documented Xenia profile state as a confirmed cause of the endless join
  spinner.
- Updated the Discord support bot to check the selected and signed-in Xenia
  profile before suggesting relay or build changes.
- Added regression coverage so physical Xbox 360 reports remain on the separate
  LAN and relay troubleshooting path.
- Rebuilt the standalone Windows patcher with the complete verified crossplay
  patch bundle and the new Xenia profile troubleshooting guidance.
- Fixed patch application silently doing nothing when the selected source folder
  is nested inside an unrelated parent Git repository.

## 0.2.1 - 2026-08-02

### Fixed

- Fixed text entered into patcher input fields rendering invisibly on affected Windows themes.
- Made the input caret and selection highlight explicit for consistent contrast.

## 0.2.0 - 2026-07-29

- Added the optional Legacy Crossplay Patcher Windows desktop utility.
- Added local EXE/XEX/PKG/SELF signature inspection and SHA-256 reporting.
- Added baseline validation, timestamped backups, embedded patch application,
  relay configuration generation, platform build launching, activity logs, and
  an in-app setup guide.
- Added `LegacyRelayUserConfig.h` for reproducible shared relay defaults.

## 0.1.1 - 2026-07-22

- Fixed heavy PS3/RPCS3 lag while joining a relay world by limiting the initial
  world-data receive burst to 32 KiB per game-loop pass. This is a PS3-only
  pacing change and does not alter packets or crossplay compatibility.
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
