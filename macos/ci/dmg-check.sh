#!/bin/bash
# CI check of the disk image: what is inside it, the drag-to-Applications install, and
# screenshots of its window in Light and Dark Mode. It switches the appearance and writes to
# /Applications, so it only runs in CI.
set -uo pipefail
if [ -z "${CI:-}" ]; then
    echo "This check changes the appearance and installs into /Applications; it only runs in CI." >&2
    exit 2
fi

DMG="$1"
OUT="$2"
mkdir -p "$OUT"
FAILED=0
MOUNT="$RUNNER_TEMP/countdown-dmg"

note() { echo "== $*" | tee -a "$OUT/dmg.txt"; }
fail() { echo "FAIL: $*" | tee -a "$OUT/dmg.txt"; FAILED=1; }
appearance() {
    osascript -e "tell application \"System Events\" to tell appearance preferences to set dark mode to $1" \
        >/dev/null 2>&1 || note "could not set dark mode to $1"
}

note "what is inside"
mkdir -p "$MOUNT"
if ! hdiutil attach "$DMG" -nobrowse -readonly -mountpoint "$MOUNT" >/dev/null; then
    fail "the disk image does not mount"
    exit 1
fi
[ -d "$MOUNT/Countdown.app" ] || fail "no Countdown.app"
[ "$(readlink "$MOUNT/Applications")" = "/Applications" ] || fail "no Applications shortcut"
[ -f "$MOUNT/.VolumeIcon.icns" ] || fail "no volume icon"
[ -f "$MOUNT/.DS_Store" ] || fail "no window layout"
[ -f "$MOUNT/Countdown.app/Contents/Resources/AppIcon.icns" ] || fail "the app has no icon"
codesign --verify --deep --strict "$MOUNT/Countdown.app" || fail "the app's signature does not verify"
"$MOUNT/Countdown.app/Contents/MacOS/Countdown" --check | tail -1 | tee -a "$OUT/dmg.txt"

note "drag to Applications, then open it from there"
rm -rf /Applications/Countdown.app
ditto "$MOUNT/Countdown.app" /Applications/Countdown.app || fail "copying into Applications failed"
hdiutil detach "$MOUNT" >/dev/null
open /Applications/Countdown.app
sleep 5
if pgrep -x Countdown >/dev/null; then
    note "the installed copy is running"
else
    fail "the installed copy did not start"
fi
osascript -e 'quit app "Countdown"' >/dev/null 2>&1
sleep 2
pkill -x Countdown 2>/dev/null

note "the window, in Light and Dark Mode"
for mode in light dark; do
    if [ "$mode" = dark ]; then appearance true; else appearance false; fi
    sleep 2
    hdiutil attach "$DMG" -autoopen >/dev/null
    sleep 5
    screencapture -x "$OUT/dmg-window-$mode-mode.png" || note "screencapture failed"
    hdiutil detach /Volumes/Countdown -force >/dev/null 2>&1
    sleep 2
done
appearance false

[ "$FAILED" = 0 ] && note "disk image checks passed"
exit "$FAILED"
