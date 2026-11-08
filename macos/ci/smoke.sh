#!/bin/bash
# UI smoke test for CI. Launches Countdown like a fresh Mac and checks the settings window,
# the menu, Launch at Login and Quit, saving screenshots and main-thread samples to $2.
# It drives the app through System Events, which GitHub's macOS runners allow.
set -uo pipefail

# It deletes Countdown's saved event and turns on Launch at Login: never on a real Mac.
if [ -z "${CI:-}" ]; then
    echo "This test resets Countdown's settings; it only runs in CI (set CI=1 to force)." >&2
    exit 2
fi

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
windows() { ax 'count windows'; }

# The status item is the app's last menu bar; the first is its (hidden) main menu. Opening
# the menu and reading its items back goes through the app's main thread, so it doubles as
# the check that the app is responding.
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

reopen_settings() {
    open_menu || fail "the menu did not open ($1)"
    choose "Set Countdown…"
    [ "$(windows)" = "1" ] || fail "Set Countdown did not open the window ($1)"
}

expect_saved() {
    local got
    got=$(saved_name)
    [ "$got" = "$1" ] || fail "saved \"$got\" instead of \"$1\" ($2)"
}

sw_vers | tee "$OUT/steps.txt"
defaults delete "$BUNDLE" >/dev/null 2>&1 || true

note "first launch opens the settings window with the name selected"
open "$APP"
for _ in $(seq 1 40); do PID=$(pgrep -x Countdown | head -1); [ -n "$PID" ] && break; sleep 0.25; done
sleep 5
snap first-launch
if [ -z "$PID" ] || ! kill -0 "$PID" 2>/dev/null; then fail "Countdown did not start"; exit 1; fi
[ "$(windows)" = "1" ] || fail "the settings window did not open"
keys 'keystroke "Trip"'
keys 'key code 36'
sleep 2
snap after-save
expect_saved "Trip" "typing should replace the selected name, Return should save"
[ "$(windows)" = "0" ] || fail "the window stayed open after Save"

note "Esc cancels an edit"
reopen_settings "after the first save"
snap settings-reopened
keys 'keystroke "Draft"'
keys 'key code 53'
sleep 2
expect_saved "Trip" "Esc should discard the edit"
[ "$(windows)" = "0" ] || fail "Esc did not close the window"

note "reopening after a cancelled edit selects the saved name again"
reopen_settings "after Cancel"
keys 'keystroke "Japan"'
keys 'key code 36'
sleep 2
expect_saved "Japan" "the reopened window should select the whole name"

note "Cmd-A selects the name (the Edit menu shortcuts work)"
reopen_settings "for Cmd-A"
keys 'key code 124'
keys 'keystroke "a" using command down'
keys 'keystroke "Tokyo"'
keys 'key code 36'
sleep 2
expect_saved "Tokyo" "Cmd-A should select the name"

note "Launch at Login turns on and the app stays responsive"
open_menu || fail "the menu did not open before Launch at Login"
choose "Launch at Login"
sleep 3
snap after-launch-at-login
keys 'key code 53'
open_menu || fail "the app stopped responding after Launch at Login"
mark=$(ax 'get value of attribute "AXMenuItemMarkChar" of menu item "Launch at Login" of menu 1 of menu bar item 1 of last menu bar')
echo "Launch at Login mark: [$mark]" | tee -a "$OUT/steps.txt"
case "$mark" in
    ""|"missing value"|*rror*) fail "Launch at Login shows no checkmark after turning it on" ;;
esac
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
