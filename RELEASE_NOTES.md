# Minecraft Legacy Console Crossplay 0.2.0

Version 0.2.0 introduces Legacy Crossplay Patcher, a self-contained Windows
desktop utility for the verified source-patch workflow.

## Included

- clean modern Patch, Setup Guide, and Activity Log views
- local Minecraft Game format detection and SHA-256 reporting
- exact 30-file source baseline verification
- timestamped source backups
- embedded crossplay patch and relay adapter installation
- shared relay/VPS configuration generation
- PC, Xbox 360, and PS3 platform build launching
- in-app PC, Xbox 360, PS3, relay/VPS, and troubleshooting guides
- PS3 join pacing fix from version 0.1.1
- authenticated external VPS relay support
- automated patcher and three-peer relay regression tests

## Tested baseline

```text
PC: native Windows64 source build 1.3.0495.0
Xbox 360: 1.0.10.0 / title ID 584111F7
PS3: BLES01976 / update 1.84 / APP_VER 01.84
LCE source: 1.2.3
Net version: 495
Protocol: 39
```

## Distribution boundary

The release asset contains only the open-source patcher and embedded open patch
data. It does not contain Minecraft binaries or assets, proprietary source,
console SDK files, firmware, keys, certificates, license bypasses, or modified
console game packages.

Users must provide their own legally obtained Minecraft Game, matching source,
and required platform toolchains.

## Community

- Discord: https://discord.gg/2rvruaWDXk
- Support: https://buymeacoffee.com/sn1per
