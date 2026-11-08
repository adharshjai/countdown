#!/bin/bash
# UI smoke test for CI. Launches Countdown like a fresh Mac and checks the settings window,
# the menu, Launch at Login and Quit, saving screenshots and main-thread samples to $2.
# It drives the app through System Events, which GitHub's macOS runners allow.
set -uo pipefail
APP="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
OUT="$2"
mkdir -p "$OUT"
PID=""
FAILED=0
BUNDLE=com.adhar.Countdown

note() { echo "== $*" | tee -a "$OUT/steps.txt"; }
fail() { echo "FAIL: $*" | tee -a "$OUT/steps.txt"; FAILED=1; }

snap() {
    screencapture -x "$OUT/$1.png" 2>/dev/null || true
    if [ -n "$PID" ] && kill -0 "$PID" 2>/dev/null; then
        sample "$PID" 1 -file "$OUT/sample-$1.txt" >/dev/null 2>&1 || true
    fi
}

ax() { osascript -e "tell application \"System Events\" to tell process \"Countdown\" to $1" 2>&1; }
keys() { osascript -e "tell application \"System Events\" to $1" >/dev/null 2>&1; }
saved_name() { defaults read "$BUNDLE" eventName 2>/dev/null; }

# Opening the status item's menu and reading its items back goes through the app's main
# thread, so it doubles as the check that the app is responding. The status item is the
# app's last menu bar: the first is its (hidden) main menu.
open_menu() {
    (ax 'click menu bar item 1 of last menu bar' >/dev/null) &
    sleep 2
    local items
    items=$(ax 'get name of every menu item of menu 1 of menu bar item 1 of last menu bar')
    echo "menu: $items" >> "$OUT/steps.txt"
    case "$items" in
        *"Set Countdown"*) return 0 ;;
        *) return 1 ;;
    esac
}

choose() {
    (ax "click menu item \"$1\" of menu 1 of menu bar item 1 of last menu bar" >/dev/null) &
    sleep 3
}

sw_vers | tee "$OUT/steps.txt"
defaults delete "$BUNDLE" >/dev/null 2>&1 || true

note "first launch opens the settings window"
open "$APP"
for _ in $(seq 1 40); do PID=$(pgrep -x Countdown | head -1); [ -n "$PID" ] && break; sleep 0.25; done
sleep 5
snap first-launch
if [ -z "$PID" ] || ! kill -0 "$PID" 2>/dev/null; then fail "Countdown did not start"; exit 1; fi
[ "$(ax 'count windows')" = "1" ] || fail "the settings window did not open"

note "typing replaces the selected name, and Return saves"
keys 'keystroke "Trip"'
keys 'key code 36'
sleep 2
snap after-save
[ "$(saved_name)" = "Trip" ] || fail "saved \"$(saved_name)\" instead of \"Trip\""

note "the menu opens while the app is hidden, and Set Countdown reopens the window"
open_menu || fail "the menu did not open"
snap menu
choose "Set Countdown…"
snap settings-reopened
[ "$(ax 'count windows')" = "1" ] || fail "Set Countdown did not reopen the window"

note "Cmd-A selects the name (the Edit menu shortcuts work)"
keys 'key code 124'
keys 'keystroke "a" using command down'
keys 'keystroke "Japan"'
keys 'key code 36'
sleep 2
[ "$(saved_name)" = "Japan" ] || fail "saved \"$(saved_name)\" instead of \"Japan\" after Cmd-A"

note "Launch at Login leaves the app responsive"
open_menu || fail "the menu did not open before Launch at Login"
choose "Launch at Login"
sleep 3
snap after-launch-at-login
keys 'key code 53'
open_menu || fail "the app stopped responding after Launch at Login"
snap menu-after-launch-at-login

note "Quit Countdown quits"
choose "Quit Countdown"
sleep 2
if kill -0 "$PID" 2>/dev/null; then
    fail "still running after Quit"
    snap after-quit
fi

[ "$FAILED" = 0 ] && note "all checks passed"
exit "$FAILED"
