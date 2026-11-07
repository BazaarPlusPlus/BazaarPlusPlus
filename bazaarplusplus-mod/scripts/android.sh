#!/usr/bin/env bash
# The Android host's device loop. Entry points: `just mod::android-*`.
# It drives one device or emulator with the official game and the debug Android App, launching the
# game from the App's launcher as a player does (ADR 0012): it builds, puts the build's files at
# their MelonLoader Payload paths (release/payload.json) in the App's game files, switches the
# patch mode, relaunches, captures evidence, and runs the three-level smoke over all of it. It
# never installs, uninstalls, or clears data, so the App's signed-in game account survives every
# command.
# shellcheck disable=SC2119,SC2120 # smoke calls the argument-less device commands without arguments.
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ANDROID_PROJECT=src/BazaarPlusPlus.Android/BazaarPlusPlus.Android.csproj
ANDROID_CONFIGURATION=Release
ANDROID_OUTPUT="src/BazaarPlusPlus.Android/bin/$ANDROID_CONFIGURATION/net10.0"
ANDROID_DLL="$ANDROID_OUTPUT/BazaarPlusPlus.Android.dll"
APP_PACKAGE=com.bazaarplusplus.android.dev
LAUNCHER_ACTIVITY="$APP_PACKAGE/com.bazaarplusplus.android.launch.LauncherActivity"
# The game's external files directory inside the App's sandbox (GameStorage.root), relative to
# run-as's working directory: Payload paths are relative to it. Only run-as reaches it, and a file
# adb push puts there belongs to shell, which the App cannot read.
GAME_FILES=no_backup/game-external
# MelonEnvironment.MelonBaseDirectory in the App; the Data Root is its sibling.
MELON_BASE="$GAME_FILES/MelonLoader"
# Where a file waits between adb push and the App's own copy of it.
STAGING=/data/local/tmp/bpp-staging
# The launcher's primary button when BPP launch is ready: the App's "play" string in each locale it
# ships. Its other actions (install, update, retry) reuse the button, so the text decides.
PLAY_LABELS=$'Play\n开始游戏\n開始遊戲'
# How long launch waits for the launcher to offer Play: it checks the installed game first.
LAUNCHER_TIMEOUT_SECONDS=90
SETTINGS_FILE="$MELON_BASE/UserData/BazaarPlusPlus.cfg"
DEVICE_LOG="$MELON_BASE/MelonLoader/Latest.log"
# The explicit ANDROID_SERIAL, or the only connected device. Keep it for the whole run.
DEVICE_SERIAL=
# How long smoke waits for the main menu after launch.
SMOKE_TIMEOUT_SECONDS="${BPP_ANDROID_SMOKE_TIMEOUT:-300}"

adb_bin() {
    if [[ -n "${ADB:-}" ]]; then
        printf '%s' "$ADB"
    elif command -v adb >/dev/null 2>&1; then
        command -v adb
    elif [[ -x "${ANDROID_HOME:-$HOME/Library/Android/sdk}/platform-tools/adb" ]]; then
        printf '%s' "${ANDROID_HOME:-$HOME/Library/Android/sdk}/platform-tools/adb"
    elif [[ -n "${LOCALAPPDATA:-}" && -x "$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe" ]]; then
        printf '%s' "$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"
    else
        die "adb not found. Put it on PATH, set ADB=/path/to/adb, or set ANDROID_HOME."
    fi
}

device() {
    # Android paths must not become host paths in Git Bash. Host inputs are relative or native.
    MSYS_NO_PATHCONV=1 "$(adb_bin)" -s "$DEVICE_SERIAL" "$@"
}

# The same rule as listedDevices and onlyDevice in scripts/setup-device.ts.
find_device() {
    local listed serial state
    # Every device adb knows, whatever its state: a booting or unauthorized one is running too.
    listed="$("$(adb_bin)" devices | tr -d '\r' | awk '/^List of devices attached/ { list = 1; next } list && NF >= 2 { print $1, $2 }')" ||
        die "adb devices failed."
    if [[ -n "${ANDROID_SERIAL:-}" ]]; then
        listed="$(awk -v target="$ANDROID_SERIAL" '$1 == target' <<<"$listed")"
        [[ -n "$listed" ]] || die "Selected Android device '$ANDROID_SERIAL' is not connected."
    fi
    [[ -n "$listed" ]] ||
        die "No Android device is online; open one AVD with 'just setup-device NAME --keep-open'."
    [[ "$listed" != *$'\n'* ]] ||
        die "More than one Android device is running ($(awk 'NR > 1 { printf ", " } { printf "%s", $0 }' <<<"$listed")); close all but one."
    read -r serial state <<<"$listed"
    [[ "$state" == device ]] ||
        die "Device '$serial' is $state; wait for it or close it (a stale entry with no emulator running clears with: adb kill-server)."
    DEVICE_SERIAL="$serial"
}

require_device() {
    if [[ -z "$DEVICE_SERIAL" ]]; then
        find_device
    elif [[ "$(device get-state 2>/dev/null | tr -d '\r')" != device ]]; then
        die "Device '$DEVICE_SERIAL' went offline."
    fi
    device shell run-as "$APP_PACKAGE" true </dev/null >/dev/null 2>&1 ||
        die "No debug Android App on '$DEVICE_SERIAL'; run 'just setup-device' for its AVD."
}

# The App's files, as the App's own user: run one shell line, print one file, copy a host file in.
app_sh() {
    device shell "run-as $APP_PACKAGE sh -c '$1'" </dev/null
}

app_cat() {
    device exec-out run-as "$APP_PACKAGE" cat "$1" </dev/null
}

app_put() {
    local source="$1" target="$2"
    # device disables MSYS conversion for Android paths; convert only this host input.
    if command -v cygpath >/dev/null 2>&1; then
        source="$(cygpath -m "$source")"
    fi
    device push "$source" "$STAGING" >/dev/null </dev/null
    app_sh "mkdir -p $(dirname "$target") && cp $STAGING $target"
    device shell rm -f "$STAGING" </dev/null
}

# put <file> <path>: copy a host file to <path> under the game's files directory in the App.
cmd_put() {
    [[ $# -eq 2 && -f "$1" ]] || die "Usage: android.sh put <file> <path under the game's files>"
    require_device
    app_put "$1" "$GAME_FILES/$2"
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
    printf '%s' "$evaluated" | bun -e '
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
    local files source payload_path
    files="$(payload_files)"
    app_sh "rm -f $MELON_BASE/UserLibs/BazaarPlusPlus.*.dll"
    while IFS=$'\t' read -r source payload_path; do
        [[ -f "$source" ]] || die "Payload file $source is missing; run 'just mod::android-build' first."
        # The App's runtime owns the Updater: it deploys its own copy and deletes this path when it
        # resets LemonLoader (ADR 0012).
        [[ "$payload_path" == MelonLoader/Plugins/* ]] && continue
        app_put "$source" "$GAME_FILES/$payload_path"
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
    app_cat "$SETTINGS_FILE" >"$work/current.cfg" 2>/dev/null || : >"$work/current.cfg"
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
    app_put "$work/next.cfg" "$SETTINGS_FILE"
    ok "[$section] $key = $value on $DEVICE_SERIAL; relaunch to apply."
}

# launch: restart the App with an empty logcat buffer and no old Latest.log, so a capture holds only
# this run, then tap Play on its launcher, as a player starts a BPP launch.
cmd_launch() {
    [[ $# -eq 0 ]] || die "Usage: just mod::android-launch"
    require_device
    open_launcher
    tap_play
}

# launcher: the same restart, stopping at the launcher.
cmd_launcher() {
    [[ $# -eq 0 ]] || die "Usage: android.sh launcher"
    require_device
    open_launcher
}

open_launcher() {
    device shell am force-stop "$APP_PACKAGE"
    device logcat -c
    app_sh "rm -f $DEVICE_LOG"
    device shell am start -n "$LAUNCHER_ACTIVITY" >/dev/null
}

# Waits until the launcher's primary button offers Play (any shipped locale) and taps its centre.
# Another action there (install, update, retry) means the game or the App needs attention first.
tap_play() {
    local deadline=$(($(date +%s) + LAUNCHER_TIMEOUT_SECONDS)) node text bounds
    while (($(date +%s) < deadline)); do
        device shell uiautomator dump /data/local/tmp/bpp-ui.xml </dev/null >/dev/null 2>&1 || true
        node="$(device exec-out cat /data/local/tmp/bpp-ui.xml </dev/null 2>/dev/null | tr '>' '\n' |
            grep -F "resource-id=\"$APP_PACKAGE:id/primary\"" || true)"
        text="$(sed -n 's/.* text="\([^"]*\)".*/\1/p' <<<"$node")"
        if [[ -n "$text" ]] && grep -qxF "$text" <<<"$PLAY_LABELS"; then
            bounds="$(sed -n 's/.* bounds="\[\([0-9]*\),\([0-9]*\)\]\[\([0-9]*\),\([0-9]*\)\]".*/\1 \2 \3 \4/p' <<<"$node")"
            read -r left top right bottom <<<"$bounds"
            device shell input tap $(((left + right) / 2)) $(((top + bottom) / 2)) </dev/null
            ok "Tapped Play on the launcher of $DEVICE_SERIAL."
            return 0
        fi
        sleep 2
    done
    die "The launcher on $DEVICE_SERIAL did not offer Play within ${LAUNCHER_TIMEOUT_SECONDS}s (it shows \"${text:-no primary button}\"); look at its screen."
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
    app_cat "$DEVICE_LOG" >"$out/Latest.log" 2>/dev/null || err "No $DEVICE_LOG on $DEVICE_SERIAL."
    device shell wm size >"$out/wm-size.txt"
    device shell wm density >"$out/wm-density.txt"
    device shell settings get system user_rotation >"$out/user-rotation.txt"
    device shell settings get system accelerometer_rotation >"$out/accelerometer-rotation.txt"
    device shell dumpsys display >"$out/display.txt"
    bun "$MOD_ROOT/../scripts/android-screen.ts" "$out" "$DEVICE_SERIAL"
    ok "Captured $run into $out"
}

# screen-check <run>: capture evidence, then reject a portrait or overridden display.
cmd_screen_check() {
    cmd_capture "$@"
    bun "$MOD_ROOT/../scripts/android-screen.ts" "$MOD_ROOT/artifacts/android/$1" "$DEVICE_SERIAL" --check
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
    # Launch removes the old log, which would satisfy the main-menu wait before the new process writes.
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
        log="$(app_cat "$DEVICE_LOG" 2>/dev/null || true)"
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
    app_cat "$path" >"$out/bazaarplusplus.db" 2>/dev/null || {
        echo "run-as cat $path failed." >"$out/database.txt"
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
