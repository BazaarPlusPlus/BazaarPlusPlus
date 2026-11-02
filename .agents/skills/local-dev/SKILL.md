---
name: local-dev
description: Local development setup and the Android test matrix for BazaarPlusPlus. Use for machine setup, preparing Android emulators and their game sign-in, or choosing resolution and orientation checks.
---

# Local development

Read `docs/development.md` (its Android section owns the matrix, the verification
layers and why), then the affected project's `AGENTS.md`. Run commands from the
repository root.

## Prepare

1. `just doctor`, then `just setup` for missing dependencies and config projections.
2. `node scripts/local-dev.mjs --help` lists the AVD names, dimensions, density,
   serials and options. `just local-dev-status` shows only an earlier result.
3. `just local-dev-init` for every AVD, or `just local-dev-init NAME` for one.
   When the game asks for sign-in, the user completes it in the emulator window;
   credentials stay in that window.

Done when each selected AVD reports `READY` in this run. Init proves the installed
game signs in; it does not build or deploy this checkout.

## Verify on devices

1. Open one AVD with `just local-dev-init NAME --keep-open`, set
   `BPP_ANDROID_SERIAL` to its serial from `--help`, then use the existing
   `just mod::android-*` deploy, smoke and screen-check recipes. Drive one device
   at a time.
2. Check each affected panel on all three AVDs with
   `just mod::android-screen-check RUN`, then read the image for clipping,
   alignment and sideways content. Add the orientation scenarios from the
   verification table when orientation or safe area changes.

Report the AVDs and scenarios checked, the evidence paths, and what stays
unverified. Evidence holds account data: keep it local.
