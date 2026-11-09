---
name: setup-device
description: Local development setup and the Android test matrix for BazaarPlusPlus. Use for machine setup, preparing Android emulators and their game sign-in, or choosing resolution and orientation checks.
---

# Set up devices

Read `docs/development.md` (its 本机矩阵与登录准备 section owns the matrix, the verification
layers and why), then the affected project's `AGENTS.md`. Run commands from the
repository root.

## Prepare

1. `just doctor`, then `just setup` for missing dependencies and config projections.
2. `bun scripts/android/setup-device.ts --help` lists the supported hosts and seed images,
   AVD names, dimensions, serials and options. `just list-devices` shows only
   an earlier result.
3. `just setup-device` for every AVD, or `just setup-device NAME` for one.
   Each AVD runs the player's setup: the official game started from the debug
   Android App's launcher, with this checkout's mod deployed. Init signs in from
   config.toml `[game]`; without it, the user signs in in the emulator window.
   A missing official APK, App runtime or interop package needs the release
   config once; init names the command. Run init in the background.
   An AVD on another system image stops init. `--recreate NAME` deletes it with
   its sign-in and copies the seed again: ask the user first.

Done when each selected device reports `READY` in this run: the launcher's Play
reached the main menu with this checkout's mod loaded.

## Verify on devices

1. Open one AVD with `just setup-device NAME --keep-open`, then use the
   `just mod::android-*` deploy, launch, smoke and screen-check recipes; launch
   taps Play on the launcher. Set `ANDROID_SERIAL` to that AVD's serial for these
   commands and walkthrough when other devices are connected; an absent or offline
   target fails. Without it, the commands require exactly one device. Phones may
   stay connected; close an AVD before you open the next.
2. Check each affected panel on all three AVDs, one after another, with
   `just mod::android-screen-check RUN`, then read the image for clipping,
   alignment and sideways content. Add the orientation scenarios from the
   verification table when orientation or safe area changes.

Report the AVDs and scenarios checked, the evidence paths, and what stays
unverified. Evidence holds account data: keep it local.
