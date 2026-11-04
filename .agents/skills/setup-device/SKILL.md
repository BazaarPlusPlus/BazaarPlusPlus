---
name: setup-device
description: Local development setup and the Android and iOS test matrices for BazaarPlusPlus. Use for machine setup, preparing Android emulators or iOS simulators and their game sign-in, or choosing resolution and orientation checks.
---

# Set up devices

Read `docs/development.md` (its 本机矩阵与登录准备 section owns both matrices, the verification
layers and why), then the affected project's `AGENTS.md`. Run commands from the
repository root.

## Prepare

1. `just doctor`, then `just setup` for missing dependencies and config projections.
2. `node scripts/setup-device.mjs --help` lists the AVD and simulator names,
   models, dimensions, serials and options. `just list-devices` shows only
   an earlier result.
3. `just setup-device` for every AVD, or `just setup-device NAME` for one.
   Each AVD runs the player's setup: the official game started from the debug
   Android App's launcher, with this checkout's mod deployed. Init signs in from
   config.ini `[game]`; without it, the user signs in in the emulator window.
   A missing official APK, App runtime or interop package needs the release
   config once; init names the command. Run init in the background.
4. On the Mac that holds the iOS simulators, `just setup-device ios` for all
   three or `just setup-device ios NAME` for one. It needs the `--test-control`
   simulator build. When it starts a QR sign-in, tell the user to scan it on
   the simulator screen with the Steam mobile app and wait; they type any
   password themselves. A first game download takes a long time; run init in
   the background.

Done when each selected device reports `READY` in this run: the launcher's Play
reached the main menu with this checkout's mod loaded.

## Verify on devices

1. Open one AVD with `just setup-device NAME --keep-open`, set
   `BPP_ANDROID_SERIAL` to its serial from `--help`, then use the
   `just mod::android-*` deploy, launch, smoke and screen-check recipes; launch
   taps Play on the launcher. Drive one device at a time.
2. Check each affected panel on all three AVDs with
   `just mod::android-screen-check RUN`, then read the image for clipping,
   alignment and sideways content. Add the orientation scenarios from the
   verification table when orientation or safe area changes.

Report the AVDs and scenarios checked, the evidence paths, and what stays
unverified. Evidence holds account data: keep it local.
