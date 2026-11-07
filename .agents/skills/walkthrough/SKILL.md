---
name: walkthrough
description: Walk every matrix device and the desktop through the mod's screens and build a local HTML grid of screenshots for review.
disable-model-invocation: true
---

# Device walkthrough

Build this checkout, bring each device up one at a time, reach every scenario in
[scenarios.md](scenarios.md), capture it, and hand the user one HTML grid
(scenario × device). The user finds the bugs; your job is complete, honest
coverage.

`bun scripts/walkthrough.ts --help` owns the command syntax, device names and
paths. `docs/development.md` (本机矩阵与登录准备) owns the device matrix and its
sign-in rules. Read both first.

Pick a RUN id once, such as `2026-11-04-a`, and use it for every command.

## 1. Build

- Android: `just mod::android-build`.
- Desktop: `just mod::build --deploy`.

Done when both build. A failed build ends the walkthrough: report it.

## 2. Walk each device

Drive one device at a time. For each device, in the order `walkthrough.ts devices` prints:

1. **Bring it up with this checkout's mod running at the main menu.**
   - Android: `just setup-device NAME --keep-open`. It deploys this
     checkout's mod into the debug Android App and starts the game from the
     launcher's Play, as a player does, then waits for the main menu.
   - Desktop: the grid is 16:9, and `capture` rejects any other window. With
     the game closed, set a size that fits the Mac's display (the game reads
     pixels; a remote virtual display can be under 500 points high):
     `defaults write com.TempoStorm.TheBazaar "Screenmanager Resolution Width" -int 1600`,
     the same with `Height` and `900`, and `"Screenmanager Fullscreen mode" -int 3`.
     Then `open "steam://run/1617400"` and look at the game window
     until the main menu shows; the log names no scene. Capture needs Screen
     Recording for this app, and macOS asks once: that grant is the user's.
   A sign-in prompt is the user's too: tell them, and wait.
2. **Walk the scenarios in scenarios.md order.** For each one, reach the
   screen, let it settle, then
   `bun scripts/walkthrough.ts capture RUN NAME SCENARIO`. Add `--note` only
   for a fact the screenshot cannot show (the panel took 20 s to open, a tap
   did nothing). When a screen cannot be reached after a real attempt, record
   `skip … --reason` with what blocked it.
3. **Shut it down**: Android `adb -s SERIAL emu kill`; desktop quit the game.

Done for a device when every scenario in scenarios.md is captured or skipped
on it. A skipped cell names its blocker.

### Reaching a screen

- **Panels**: `bun scripts/walkthrough.ts panel NAME collection|history|live_build`.
  It returns once the game consumed the request. Then capture.
- **Everything else** (History tabs, card inspection, settings, starting a
  run, shop tooltips, closing a panel): tap the game. Look first, then tap:
  - Android: `adb -s SERIAL exec-out screencap -p > SCRATCH.png`, read the
    image, `adb -s SERIAL shell input tap X Y` in screenshot pixels. Close with
    the back key: `adb -s SERIAL shell input keyevent KEYCODE_BACK`.
  - Desktop: the computer-use tools on the game window; Escape closes a panel.
- Verify each tap by a fresh look before you capture. A capture of the wrong
  screen is worse than a skip.

## 3. Report

`bun scripts/walkthrough.ts report RUN`, then send
`artifacts/walkthrough/RUN/index.html` with `SendUserFile`. The screenshots show
the signed-in account: the report stays local, never published or committed.

In the closing message, list per device what was skipped and why, and any
cell whose capture printed problems. Describe what you saw only when the user
asks; they review the grid themselves.
