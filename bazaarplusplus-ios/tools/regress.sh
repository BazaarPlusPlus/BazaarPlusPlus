#!/bin/bash
# Simulator regression: install the --test-control build, sign in from the saved login, download
# until ready, launch, and print the lines that show the game started. Fails on the first stage that does not finish.
set -u
cd "$(dirname "$0")/.."; RUN=tools/run-app.py; CTL=tools/app-control.py
snap(){ python3 "$CTL" snapshot 2>/dev/null | python3 -c 'import json,sys; s=json.load(sys.stdin); print(s["state"], "in" if s["loggedIn"] else "out", "busy" if s["busy"] else "idle", s["errorCode"] or "-")'; }
python3 "$RUN" >/dev/null || { echo LAUNCH_FAIL; exit 1; }
for i in $(seq 1 120); do S=$(snap); case "$S" in *" in idle"*|error*) break;; esac; sleep 2; done; echo "restore: $S"
case "$S" in *" in idle"*) ;; *) exit 1;; esac
python3 "$CTL" download >/dev/null
for i in $(seq 1 300); do S=$(snap); case "$S" in "ready "*|error*|"installed "*|"outdated "*) break;; esac; sleep 2; done; echo "prepare: $S"
case "$S" in "ready "*) ;; *) exit 1;; esac
LOG=$(ls -d build/steam-app/verification 2>/dev/null)/stdout.log
START=$(( $(wc -l < "$LOG") ))
python3 "$CTL" launch >/dev/null
for i in $(seq 1 120); do tail -n +$START "$LOG" | grep -qE 'RunTasks complete' && break; sleep 2; done
tail -n +$START "$LOG" | grep -a -E 'RunTasks complete|STEAM_APP_WEB_TICKET|\[BppIos\]|\[BazaarPlusPlus\]' | head -40 | cut -c1-240
tail -n +$START "$LOG" | grep -aq 'RunTasks complete.*Failed: 0' || { echo GAME_START_FAIL; exit 1; }
