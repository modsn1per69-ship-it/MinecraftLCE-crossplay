# Minecraft Legacy Console Crossplay 0.2.2

## Updated patcher and Xenia join guidance

Version 0.2.2 rebuilds the self-contained Windows patcher from the current
repository. It embeds the complete verified LCE 1.2.3 / net 495 / protocol 39
crossplay patch bundle and adds the confirmed Xenia profile fix to the built-in
Xbox 360 and troubleshooting guides.

When Xenia remains on the join spinner, close Minecraft, select one valid
signed-in Xenia profile, restart Xenia, and retry before changing relay or build
settings. Existing profiles and saves should be backed up rather than deleted.

The gameplay patch payload is unchanged from 0.2.1. This release packages the
current verified payload with corrected setup guidance; it does not introduce
an untested protocol or game-code revision.

Patch application now runs independently of any parent Git repository. This
prevents Git from silently skipping the patch when the selected source folder is
nested inside another repository. The patcher's apply/reapply smoke test covers
that layout.

The patcher remains fully local and patch-only. It does not contain or upload
Minecraft binaries, proprietary source, console SDKs, firmware, keys,
certificates, or license bypasses.

---

# Previous release: 0.2.1

## Text visibility fix

Version 0.2.1 fixes a WPF rendering issue that could make values typed into the patcher's input fields invisible. Input text, the caret, and selected text now use explicit high-contrast colors across supported Windows themes.

The patcher remains fully local and patch-only. It does not contain or upload Minecraft binaries, proprietary source, console SDKs, firmware, keys, certificates, or license bypasses.

---

# Previous release: 0.2.0

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
