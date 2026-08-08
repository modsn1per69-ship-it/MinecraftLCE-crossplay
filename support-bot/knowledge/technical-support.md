# Legacy Crossplay technical support knowledge

## Tested identities

- PC host: native Windows64 source build 1.3.0495.0, Release|x64.
- Xbox 360: Xbox 360 Edition 1.0.10.0, title ID 584111F7.
- PS3: BLES01976, update 1.84, APP_VER 01.84.
- Shared source/network identity: LCE 1.2.3, net 495, protocol 39.
- Relay build ID: 584111F7-1.0.10.0-lce1.2.3-net495-proto39.
- Relay wire protocol: V2.

## Required troubleshooting flow

1. Establish the platform: PC, Xbox 360/Xenia, or PS3/RPCS3.
2. Establish the topology: emulator-to-emulator, emulator-to-physical console, physical console-to-PC, or VPS.
3. Ask what hosts the world and what joins it.
4. Ask for the relay lines from immediately before and after the failed join.
5. Compare relay host, port, mode, session ID, build ID, protocol and token.
6. Separate a build problem from a network problem. FTP or HTTP working on a console does not prove that the game reaches TCP 61000.
7. Do not claim the problem is fixed until the relay shows both hosting and joining peers and gameplay is verified.

## Xenia profile and endless joining

An endless join spinner on Xenia is not always a relay failure. A missing,
unselected, or incorrectly signed-in Xenia profile can stall Minecraft while the
relay remains healthy.

1. Close Minecraft before changing the active profile.
2. Select one valid Xenia profile and confirm it is signed in.
3. Restart Xenia, launch Minecraft, and retry the join.
4. If it still stalls, back up the existing profile and test with a fresh
   Xenia-generated profile. Do not delete the original profile or its saves.
5. Continue with relay diagnosis only if the fresh signed-in profile also fails.

For Xenia Canary, profile data is normally under the emulator's user directory.
The [official Xenia Canary profile documentation](https://github.com/xenia-canary/xenia-canary/wiki/Profiles)
describes generated profiles, imported profiles, and signed-in user slots.
Profile storage location varies with portable mode, so inspect the user directory
used by that exact Xenia instance.

## Xbox 360 XUI DLC join freeze

Patcher versions before 0.2.3 did not bypass every platform DLC installation
call in the old Xbox 360 XUI create/join scenes. A trace containing
`StartInstallDLCProcess`, `XUI_MultiGameJoinLoad.cpp`, or
`XUI_MultiGameCreate.cpp` requires the 0.2.3 source patch.

Reapply the patch to a clean matching source tree and rebuild `Release|Xbox
360`. Reconfiguring the relay does not modify an existing XEX. In relay builds,
the XUI create/join scenes must use `LegacyRelayPolicy` for platform DLC,
sign-in, multiplayer privilege, online-game, and user-content decisions.

## Address rules

- `127.0.0.1` means the same machine. It is valid only when the relay and client are on the same PC.
- `0.0.0.0` is a server bind address, never a client destination.
- A physical Xbox 360 or PS3 must use the relay PC's numeric LAN IPv4 address.
- The relay should bind to `0.0.0.0:61000` for LAN clients.
- Allow inbound TCP 61000 on the relay PC's Private firewall profile.
- Console relay defaults are compiled into the game. Changing a configuration UI after building does not change an existing XEX or PS3 build; apply and rebuild.

## Interpreting relay logs

- `remote=127.0.0.1` proves only that a process on the relay PC connected.
- A physical console attempt should produce a remote LAN address such as `192.168.x.x` or `10.x.x.x`.
- `hub waiting` means a host registered but no compatible joining peer completed the handshake.
- `host disconnected` closes the session and joining clients cannot complete.
- Host and join must use exactly the same session ID. `local test` and `local testing` are different.
- Host and join must use the exact same build ID and V2 protocol.

## XboxMedia/strings.h

`XboxMedia/strings.h` is part of the Xbox 360 platform media/source environment. If it is missing, the source package is incomplete for the Xbox 360 configuration. The public patch repository does not contain this copyrighted platform media tree.

Do not copy `DurangoMedia/strings.h`, `Windows64Media/strings.h`, or a header from another update. Restore the complete matching `XboxMedia` folder or generated media output from the user's own authorized source environment, then clean and rebuild `Release|Xbox 360`.

## Distribution boundary

Never provide download locations for Minecraft game binaries, XEX/PKG/SELF files, copyrighted media/assets, complete proprietary source trees, console SDKs, firmware, keys, certificates, or license bypasses. Explain how users can patch and build their own legally obtained matching files.
