# Applying the source patches

## Before you begin

Use a clean copy of the matching source baseline. Do not apply this patch on
top of a launcher fork, logo mod, gameplay mod, another title update, or an
older relay experiment. Keep your original source tree backed up.

Expected root layout:

```text
your-source/
  MinecraftConsoles.sln
  Minecraft.Client/
    Minecraft.Client.vcxproj
    Common/Network/GameNetworkManager.cpp
  Minecraft.World/
    LevelChunk.cpp
```

The patch is designed for title ID `584111F7`, game `1.0.10.0`, LCE `1.2.3`,
net version `495`, and protocol `39`.

## 1. Verify the baseline

From the patch repository root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\verify-baseline.ps1 -SourceRoot "D:\LCE\source"
```

An exact match is best. A mismatch can mean the source is from another title
update, a file was edited, or line endings/encoding were changed. Do not force
the patch onto a different revision. Port each hunk deliberately instead.

## 2. Check without changing files

```powershell
.\scripts\apply-patches.ps1 -SourceRoot "D:\LCE\source" -CheckOnly
```

This runs `git apply --check` against the source tree. It does not require the
source tree itself to be a Git repository.

## 3. Apply

```powershell
.\scripts\apply-patches.ps1 -SourceRoot "D:\LCE\source"
```

The script performs two operations:

1. Applies `patches/crossplay-core.patch` to existing source and project files.
2. Copies the seven new files from `patches/relay/` into
   `Minecraft.Client/Common/Network/Relay/`.

The script refuses to run if the relay appears to be installed already or if
Git reports that the patch does not match.

## 4. Confirm the result

Confirm these files now exist:

```text
Minecraft.Client/Common/Network/Relay/LegacyRelayPolicy.h
Minecraft.Client/Common/Network/Relay/NetworkPlayerRelay.cpp
Minecraft.Client/Common/Network/Relay/NetworkPlayerRelay.h
Minecraft.Client/Common/Network/Relay/PlatformNetworkManagerRelay.cpp
Minecraft.Client/Common/Network/Relay/PlatformNetworkManagerRelay.h
Minecraft.Client/Common/Network/Relay/RelayTransport.cpp
Minecraft.Client/Common/Network/Relay/RelayTransport.h
```

Confirm the project and source contain `CONSOLE_LEGACY_RELAY`:

```powershell
Select-String -Path "D:\LCE\source\Minecraft.Client\Minecraft.Client.vcxproj" -Pattern "CONSOLE_LEGACY_RELAY"
Select-String -Path "D:\LCE\source\Minecraft.Client\Common\Network\GameNetworkManager.cpp" -Pattern "CPlatformNetworkManagerRelay"
```

## Manual application

If scripts are not allowed in your environment:

```powershell
cd "D:\LCE\source"
git apply --check "D:\legacy-console-crossplay-patches\patches\crossplay-core.patch"
git apply "D:\legacy-console-crossplay-patches\patches\crossplay-core.patch"
```

Then copy the contents of `patches/relay/` to
`Minecraft.Client/Common/Network/Relay/` without changing filenames.

## Reverting

If the patched files have not been edited further:

```powershell
.\scripts\remove-patches.ps1 -SourceRoot "D:\LCE\source"
```

If the reverse check fails, do not force it. Restore your backup or use your
own source-control history.

## Porting to another baseline

The compile-time guards prevent non-relay builds from using most relay paths,
but packet compatibility is not guaranteed across title updates. For another
baseline, verify at minimum:

- `SharedConstants::NETWORK_PROTOCOL_VERSION`
- `MINECRAFT_NET_VERSION`
- packet IDs and field order
- `PlayerUID` size and serialization per platform
- `BlockRegionUpdatePacket` buffer layout
- full-chunk biome payload placement
- movement packet ranges and packing
- session and sign-in menu state transitions

Change the relay build ID whenever any gameplay packet, serializer, source
baseline, or title update changes.
