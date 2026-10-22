#!/usr/bin/env bash
# The Android host's device loop. Entry points: `just mod::android-*`.
# It drives one device or emulator that already runs the LemonLoader-injected game: it builds,
# pushes the build's files to their MelonLoader Payload paths (release/payload.json), switches the
# patch mode, relaunches the game, and captures evidence. It never installs, uninstalls, or clears
# the game's data, so the device's signed-in account survives every command.
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ANDROID_PROJECT=src/BazaarPlusPlus.Android/BazaarPlusPlus.Android.csproj
ANDROID_CONFIGURATION=Release
ANDROID_OUTPUT="src/BazaarPlusPlus.Android/bin/$ANDROID_CONFIGURATION/net10.0"
ANDROID_DLL="$ANDROID_OUTPUT/BazaarPlusPlus.Android.dll"
GAME_PACKAGE=com.tempogames.thebazaar
GAME_ACTIVITY="$GAME_PACKAGE/com.unity3d.player.UnityPlayerActivity"
# The app's external files directory: melonloader Payload paths are relative to it.
GAME_FILES="/sdcard/Android/data/$GAME_PACKAGE/files"
# MelonEnvironment.MelonBaseDirectory on the device; the Data Root is its sibling.
MELON_BASE="$GAME_FILES/MelonLoader"
SETTINGS_FILE="$MELON_BASE/UserData/BazaarPlusPlus.cfg"
DEVICE_SERIAL="${BPP_ANDROID_SERIAL:-emulator-5556}"

adb_bin() {
    if [[ -n "${ADB:-}" ]]; then
        printf '%s' "$ADB"
    elif command -v adb >/dev/null 2>&1; then
        command -v adb
    elif [[ -x "${ANDROID_HOME:-$HOME/Library/Android/sdk}/platform-tools/adb" ]]; then
        printf '%s' "${ANDROID_HOME:-$HOME/Library/Android/sdk}/platform-tools/adb"
    else
        die "adb not found. Put it on PATH, set ADB=/path/to/adb, or set ANDROID_HOME."
    fi
}

device() {
    "$(adb_bin)" -s "$DEVICE_SERIAL" "$@"
}

require_device() {
    [[ "$(device get-state 2>/dev/null)" == "device" ]] ||
        die "Device '$DEVICE_SERIAL' is not online; start it or set BPP_ANDROID_SERIAL."
}

# build [-p:Name=Value ...]: compile the host against the fetched Interop Snapshot.
cmd_build() {
    collect_props "$@"
    dotnet build "$ANDROID_PROJECT" -c "$ANDROID_CONFIGURATION" ${PROPS[@]+"${PROPS[@]}"}
}

# payload_files: print "<source><TAB><Payload path>" for each melonloader Payload file this build
# has, as the host project's Payload.targets collects them. A dependency the Android build does not
# produce yet (the serializers, #359) is skipped; a missing BPP assembly is printed so deploy fails.
payload_files() {
    local evaluated
    evaluated="$(dotnet msbuild "$ANDROID_PROJECT" -nologo -p:Configuration="$ANDROID_CONFIGURATION" \
        -t:CollectPayloadFiles -getItem:BppPayloadFile)" ||
        die "Could not collect the MelonLoader Payload files: $evaluated"
    # shellcheck disable=SC2016 # the ${...} below is a JavaScript template, not shell.
    printf '%s' "$evaluated" | node -e '
        const fs = require("fs");
        for (const item of JSON.parse(fs.readFileSync(0, "utf8")).Items.BppPayloadFile) {
            if (item.ProductAssembly === "true" || fs.existsSync(item.FullPath)) {
                console.log(`${item.FullPath}\t${item.PayloadPath}`);
            }
        }'
}

# deploy: push this build's files to their MelonLoader Payload paths: BPP's assemblies into
# MelonLoader/Mods/, third-party ones into MelonLoader/UserLibs/. A BPP assembly left in UserLibs/
# is outside the Payload and could shadow the Mods/ copy, so it is removed.
cmd_deploy() {
    [[ $# -eq 0 ]] || die "Usage: just mod::android-deploy"
    [[ -f "$ANDROID_DLL" ]] || die "No build at $ANDROID_DLL; run 'just mod::android-build' first."
    require_device
    device shell test -d "$MELON_BASE/Mods" ||
        die "$MELON_BASE/Mods is missing on '$DEVICE_SERIAL'; install the LemonLoader-injected game first."
    local files source payload_path
    files="$(payload_files)"
    device shell "rm -f '$MELON_BASE/UserLibs/'BazaarPlusPlus.*.dll"
    while IFS=$'\t' read -r source payload_path; do
        [[ -f "$source" ]] || die "Payload file $source is missing; run 'just mod::android-build' first."
        device shell mkdir -p "$(dirname "$GAME_FILES/$payload_path")"
        device push "$source" "$GAME_FILES/$payload_path"
    done <<<"$files"
    ok "Deployed the MelonLoader Payload to $DEVICE_SERIAL; it loads on the next launch."
}

# mode <enabled|observe|disabled>: set [Android] PatchMode in the device's settings file,
# keeping every other line; it takes effect on the next launch.
cmd_mode() {
    local mode="${1:-}"
    case "$mode" in
        enabled) mode=Enabled ;;
        observe) mode=Observe ;;
        disabled) mode=Disabled ;;
        *) die "Usage: just mod::android-mode <enabled|observe|disabled>" ;;
    esac
    require_device
    local work
    work="$(mktemp -d)"
    # shellcheck disable=SC2064 # expand now: the trap must remove this run's directory.
    trap "rm -rf -- '$work'" EXIT
    if device shell test -f "$SETTINGS_FILE"; then
        device pull "$SETTINGS_FILE" "$work/current.cfg" >/dev/null
    else
        : >"$work/current.cfg"
    fi
    awk -v mode="$mode" '
        /^[[:space:]]*\[/ {
            if (in_android && !done) { print "PatchMode = " mode; print ""; done = 1 }
            in_android = ($0 ~ /^[[:space:]]*\[Android\][[:space:]]*$/)
        }
        in_android && /^[[:space:]]*PatchMode[[:space:]]*=/ {
            print "PatchMode = " mode; done = 1; next
        }
        { print }
        END {
            if (!done) {
                if (!in_android) { print ""; print "[Android]"; print "" }
                print "PatchMode = " mode
            }
        }
    ' "$work/current.cfg" >"$work/next.cfg"
    device shell mkdir -p "$(dirname "$SETTINGS_FILE")"
    device push "$work/next.cfg" "$SETTINGS_FILE" >/dev/null
    ok "PatchMode = $mode on $DEVICE_SERIAL; relaunch to apply."
}

# launch: restart the game with an empty logcat buffer, so a capture holds only this run.
cmd_launch() {
    [[ $# -eq 0 ]] || die "Usage: just mod::android-launch"
    require_device
    device shell am force-stop "$GAME_PACKAGE"
    device logcat -c
    device shell am start -n "$GAME_ACTIVITY"
}

# capture <run>: save logcat, a screenshot, and MelonLoader's Latest.log under artifacts/android/<run>/.
cmd_capture() {
    local run="${1:-}"
    [[ "$run" =~ ^[A-Za-z0-9_-]+$ ]] || die "Usage: just mod::android-capture <run> (letters, digits, - and _)"
    require_device
    local out="$MOD_ROOT/artifacts/android/$run"
    mkdir -p "$out"
    device logcat -d -v threadtime >"$out/logcat.txt"
    device exec-out screencap -p >"$out/screen.png"
    device pull "$MELON_BASE/MelonLoader/Latest.log" "$out/Latest.log" >/dev/null
    ok "Captured $run into $out"
}

dispatch "$@"
