#!/usr/bin/env bash
# Development only: opens Main Overlay Panels in the running desktop game and saves a capture of
# each, taken inside the game (no screen-recording permission). Entry point: `just mod::ui-shots`.
# It drives the Walkthrough Control: `open <panel id>`, waits for the open in the log, then
# `shot <name>`, and waits for <Data Root>/ui-shots/<name>.png. Debug builds also register the Kit
# Gallery (`kit-gallery`).
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
    cat <<'EOF2'
Usage: just mod::ui-shots [panel id ...] [--out DIR] [--prefix NAME]

  panel id   kit-gallery (default), HistoryPanel, LiveBuildPanel, CollectionPanel
  --out      also copy the captures here
  --prefix   name the captures <prefix>-<panel id>.png (default: the panel id alone)

The game must be running with the mod (open "steam://run/1617400"), outside combat. Captures land
in <Data Root>/ui-shots/. BPP_GAME_ROOT overrides the discovered game.
EOF2
}

panels=()
out=""
prefix=""
while [[ $# -gt 0 ]]; do
    case "$1" in
    -h | --help) usage; exit 0 ;;
    --out) out="$2"; shift 2 ;;
    --prefix) prefix="$2"; shift 2 ;;
    *) panels+=("$1"); shift ;;
    esac
done
[[ ${#panels[@]} -gt 0 ]] || panels=(kit-gallery)

root="$(game_root)"
[[ -n "$root" ]] || die "No game found; set BPP_GAME_ROOT."
data="$root/BazaarPlusPlusV6"
log="$root/BepInEx/LogOutput.log"
command_file="$data/walkthrough-command.txt"
[[ -d "$data" ]] || die "No Data Root at $data; start the game with the mod once."
[[ -f "$log" ]] || die "No BepInEx log at $log; is the game running with the mod?"

# Waits up to $2 seconds for the command file to be taken.
send() {
    printf '%s\n' "$1" >"$command_file"
    local waited=0
    while [[ -f "$command_file" ]]; do
        sleep 0.25
        waited=$((waited + 1))
        [[ $waited -lt $(($2 * 4)) ]] || die "The game did not take \`$1\`; is it running with the mod?"
    done
}

# Waits for a new log line after byte offset $1 that matches $2.
await_log() {
    local offset="$1" pattern="$2" waited=0
    until tail -c +"$((offset + 1))" "$log" | grep -q -- "$pattern"; do
        sleep 0.25
        waited=$((waited + 1))
        [[ $waited -lt 60 ]] || die "Timed out waiting for: $pattern"
    done
}

[[ -z "$out" ]] || mkdir -p "$out"
for panel in "${panels[@]}"; do
    name="${prefix:+$prefix-}$(printf '%s' "$panel" | tr '[:upper:]' '[:lower:]')"
    offset=$(wc -c <"$log")
    send "open $panel" 10
    await_log "$offset" "overlay_panels.walkthrough.command.*panel_id=$panel.*open_panel_id=$panel"
    # Native art and portraits arrive over a few frames after the open.
    sleep 2
    offset=$(wc -c <"$log")
    send "shot $name" 10
    await_log "$offset" "overlay_panels.walkthrough.shot.*name=$name"
    shot="$data/ui-shots/$name.png"
    [[ -z "$out" ]] || cp "$shot" "$out/"
    echo "$shot"
done
