#!/usr/bin/env bash
# The Android host's device loop. Entry points: `just mod::android-*`.
# It drives one device or emulator that already runs the LemonLoader-injected game: it builds,
# pushes the build's files to their MelonLoader Payload paths (release/payload.json), switches the
# patch mode, relaunches the game, captures evidence, and runs the three-level smoke over all of it.
# It never installs, uninstalls, or clears the game's data, so the device's signed-in account
# survives every command.
# shellcheck disable=SC2119,SC2120 # smoke calls the argument-less device commands without arguments.
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
DEVICE_LOG="$MELON_BASE/MelonLoader/Latest.log"
DEVICE_SERIAL="${BPP_ANDROID_SERIAL:-emulator-5556}"
# How long smoke waits for the main menu after launch.
SMOKE_TIMEOUT_SECONDS="${BPP_ANDROID_SMOKE_TIMEOUT:-300}"

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
        # adb reads stdin; without </dev/null it would swallow the rest of the file list.
        device shell mkdir -p "$(dirname "$GAME_FILES/$payload_path")" </dev/null
        device push "$source" "$GAME_FILES/$payload_path" </dev/null
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
    write_setting Android PatchMode "$mode"
}

# setting <section> <key> <value>: set one key of the device's settings file, keeping every other
# line, so one feature can be switched for an enabled/disabled capture; the next launch reads it.
cmd_setting() {
    [[ $# -eq 3 && "$1" =~ ^[A-Za-z0-9_]+$ && "$2" =~ ^[A-Za-z0-9_]+$ && "$3" =~ ^[A-Za-z0-9_.-]+$ ]] ||
        die "Usage: just mod::android-setting <section> <key> <value> (letters, digits, _ . -)"
    write_setting "$1" "$2" "$3"
}

write_setting() {
    local section="$1" key="$2" value="$3"
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
    awk -v section="$section" -v key="$key" -v value="$value" '
        /^[[:space:]]*\[/ {
            if (in_section && !done) { print key " = " value; print ""; done = 1 }
            in_section = ($0 ~ "^[[:space:]]*\\[" section "\\][[:space:]]*$")
        }
        in_section && $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
            print key " = " value; done = 1; next
        }
        { print }
        END {
            if (!done) {
                if (!in_section) { print ""; print "[" section "]"; print "" }
                print key " = " value
            }
        }
    ' "$work/current.cfg" >"$work/next.cfg"
    device shell mkdir -p "$(dirname "$SETTINGS_FILE")"
    device push "$work/next.cfg" "$SETTINGS_FILE" >/dev/null
    ok "[$section] $key = $value on $DEVICE_SERIAL; relaunch to apply."
}

# launch: restart the game with an empty logcat buffer, so a capture holds only this run.
cmd_launch() {
    [[ $# -eq 0 ]] || die "Usage: just mod::android-launch"
    require_device
    device shell am force-stop "$GAME_PACKAGE"
    device logcat -c
    device shell am start -n "$GAME_ACTIVITY"
}

# capture <run>: save logs, a screenshot and display metadata under artifacts/android/<run>/.
cmd_capture() {
    local run="${1:-}"
    [[ "$run" =~ ^[A-Za-z0-9_-]+$ ]] || die "Usage: just mod::android-capture <run> (letters, digits, - and _)"
    require_device
    local out="$MOD_ROOT/artifacts/android/$run"
    mkdir -p "$out"
    device logcat -d -v threadtime >"$out/logcat.txt"
    device exec-out screencap -p >"$out/screen.png"
    # Without a Latest.log, MelonLoader never loaded; the capture still holds logcat and the screen.
    device pull "$DEVICE_LOG" "$out/Latest.log" >/dev/null || err "No $DEVICE_LOG on $DEVICE_SERIAL."
    device shell wm size >"$out/wm-size.txt"
    device shell wm density >"$out/wm-density.txt"
    device shell settings get system user_rotation >"$out/user-rotation.txt"
    device shell settings get system accelerometer_rotation >"$out/accelerometer-rotation.txt"
    device shell dumpsys display >"$out/display.txt"
    node "$MOD_ROOT/../scripts/android-screen.mjs" "$out" "$DEVICE_SERIAL"
    ok "Captured $run into $out"
}

# screen-check <run>: capture evidence, then reject a portrait or overridden display.
cmd_screen_check() {
    cmd_capture "$@"
    node "$MOD_ROOT/../scripts/android-screen.mjs" "$MOD_ROOT/artifacts/android/$1" "$DEVICE_SERIAL" --check
}

# smoke <enabled|observe|disabled>: the device smoke. It builds, deploys, sets the mode, relaunches,
# waits for the main menu, captures into artifacts/android/smoke-<mode>/ with the built DLL's
# sha256, the VERSION it was built from, and the history database, then verifies. Every failure
# after launch still captures, so verify always writes smoke.md. A disabled run is expected to
# fail at "Hook hit", an observe run at "Feature" (it counts the label call without writing).
cmd_smoke() {
    local mode="${1:-}"
    case "$mode" in
        enabled | observe | disabled) ;;
        *) die "Usage: just mod::android-smoke <enabled|observe|disabled>" ;;
    esac
    require_device
    local run="smoke-$mode"
    local out="$MOD_ROOT/artifacts/android/$run"
    rm -rf "$out"
    cmd_build
    cmd_deploy
    cmd_mode "$mode"
    # The old log would satisfy the main-menu wait before the new process has written a line.
    device shell rm -f "$DEVICE_LOG"
    cmd_launch
    local menu=reached
    wait_for_main_menu || menu=missed
    cmd_capture "$run"
    built_dll_sha256 >"$out/dll.sha256"
    tr -d '[:space:]' <"$MOD_ROOT/../VERSION" >"$out/version.txt"
    pull_database "$out"
    cmd_verify "$out"
    # The label is written before the menu loads, so the three levels can pass on a run stuck at
    # sign-in; that run still fails here.
    [[ "$menu" == reached ]] ||
        die "The main menu was not reached in ${SMOKE_TIMEOUT_SECONDS}s; see $out/screen.png"
}

# The sha256 of the Android host DLL that deploy pushed to MelonLoader/Mods/.
built_dll_sha256() {
    local source
    source="$(payload_files | awk -F '\t' '$2 == "MelonLoader/Mods/BazaarPlusPlus.Android.dll" { print $1 }')"
    [[ -f "$source" ]] || die "No built BazaarPlusPlus.Android.dll in the Payload files."
    shasum -a 256 "$source" | awk '{ print $1 }'
}

# Waits until the main menu has loaded and a hooks summary follows it in the log, so the counters
# cover the menu. Scene loads log in every patch mode, and each one forces a summary shortly after
# (an unchanged summary is otherwise skipped). Returns 1 on timeout.
wait_for_main_menu() {
    local deadline=$(($(date +%s) + SMOKE_TIMEOUT_SECONDS)) log
    while (($(date +%s) < deadline)); do
        log="$(device shell cat "$DEVICE_LOG" </dev/null 2>/dev/null || true)"
        if awk '/event=android\.scene\.loaded .*name=MainMenuScene/ { menu = 1 }
            menu && /event=android\.hooks\.summary/ { found = 1; exit }
            END { exit !found }' <<<"$log"; then
            ok "Main menu reached; one hooks summary followed it."
            return 0
        fi
        sleep 5
    done
    err "No main menu and following hooks summary within ${SMOKE_TIMEOUT_SECONDS}s; capturing anyway."
    return 1
}

# Pulls the history database the record chain logged it opened, and lists each table's row count.
pull_database() {
    local out="$1" path table
    if [[ ! -f "$out/Latest.log" ]]; then
        echo "No Latest.log; no database path to pull." >"$out/database.txt"
        return 0
    fi
    path="$(sed -n 's/.*event=android.record_chain.started .*database_path=\([^ ]*\).*/\1/p' "$out/Latest.log" | head -n 1)"
    if [[ -z "$path" ]]; then
        echo "The record chain did not start; no database to pull." >"$out/database.txt"
        return 0
    fi
    device pull "$path" "$out/bazaarplusplus.db" >/dev/null || {
        echo "adb pull $path failed." >"$out/database.txt"
        return 0
    }
    command -v sqlite3 >/dev/null 2>&1 || {
        echo "sqlite3 is not on PATH; the database is pulled but not counted." >"$out/database.txt"
        return 0
    }
    {
        echo "$path"
        echo "user_version $(sqlite3 "$out/bazaarplusplus.db" 'PRAGMA user_version')"
        for table in $(sqlite3 "$out/bazaarplusplus.db" \
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"); do
            echo "$table $(sqlite3 "$out/bazaarplusplus.db" "SELECT count(*) FROM \"$table\"")"
        done
    } >"$out/database.txt"
}

# verify <dir>: assert the three levels on a smoke capture and write <dir>/smoke.md. Injection: the
# loaded plugin's sha256 equals the built DLL's. Hook hit: VersionLabelPatch installed and its count,
# above zero in the last hooks summary. Feature: the version label written with the captured
# version.txt (the checkout's VERSION for a capture without one). Database row counts are evidence
# only: the main menu records nothing.
cmd_verify() {
    local dir="${1:-}"
    [[ -d "$dir" ]] || die "Usage: just mod::android-verify <capture dir>"
    local log="$dir/Latest.log" version built="" loaded sha installed first_hits last_hits label
    if [[ -f "$dir/version.txt" ]]; then
        version="$(tr -d '[:space:]' <"$dir/version.txt")"
    else
        version="$(tr -d '[:space:]' <"$MOD_ROOT/../VERSION")"
    fi
    [[ -f "$dir/dll.sha256" ]] && built="$(tr -d '[:space:]' <"$dir/dll.sha256")"
    [[ -f "$log" ]] || log=/dev/null

    loaded="$(grep -m 1 'event=android.plugin.loaded ' "$log" || true)"
    sha="$(sed -n 's/.* sha256=\([0-9a-f]*\).*/\1/p' <<<"$loaded")"
    installed="$(grep -c 'event=android.patch.installed patch_type=BazaarPlusPlus.Android.Patches.VersionLabelPatch ' "$log" || true)"
    first_hits="$(sed -n 's/.*event=android.hooks.summary .*version_label_hits=\([0-9]*\).*/\1/p' "$log" | head -n 1)"
    last_hits="$(sed -n 's/.*event=android.hooks.summary .*version_label_hits=\([0-9]*\).*/\1/p' "$log" | tail -n 1)"
    label="$(grep 'event=android.version_label.hit .*write=true' "$log" |
        sed -n 's/.* after="\([^"]*\)".*/\1/p' | grep -F "| BPP $version " | head -n 1 || true)"

    local injection=fail hooks=fail feature=fail
    [[ ${#built} -eq 64 && "$sha" == "$built" ]] && injection=pass
    [[ "$installed" -gt 0 && "${last_hits:-0}" -gt 0 ]] && hooks=pass
    [[ -n "$label" ]] && feature=pass
    # The label itself holds a pipe, which would split its table cell.
    local label_cell="${label//|/\\|}"

    {
        echo "# Android smoke: $(basename "$dir")"
        echo
        echo "| Level | Result | Evidence |"
        echo "|---|---|---|"
        echo "| Injection | $injection | plugin.loaded sha256 \`${sha:-none}\`, built \`${built:-none}\` |"
        echo "| Hook hit | $hooks | VersionLabelPatch installed $installed time(s); version_label_hits ${first_hits:-none} in the first summary, ${last_hits:-none} in the last |"
        echo "| Feature | $feature | version label \`${label_cell:-not written with BPP $version}\` |"
        echo
        echo "Loaded: \`${loaded:-no android.plugin.loaded line}\`"
        if [[ -f "$dir/database.txt" ]]; then
            echo
            echo "Database (evidence only):"
            echo
            sed 's/^/    /' "$dir/database.txt"
        fi
    } >"$dir/smoke.md"

    local level
    for level in "Injection:$injection" "Hook hit:$hooks" "Feature:$feature"; do
        [[ "${level##*:}" == pass ]] || die "Smoke failed at \"${level%:*}\"; see $dir/smoke.md"
    done
    ok "Smoke passed all three levels; see $dir/smoke.md"
}

dispatch "$@"
