#!/bin/bash
# Packages macos/build/Countdown.app into macos/build/Countdown.dmg: a window with the app,
# an arrow and an Applications shortcut, ready to drag and drop. Run build.sh first.
# Needs dmgbuild (python3 -m pip install dmgbuild).
set -euo pipefail
cd "$(dirname "$0")"
APP="build/Countdown.app"
[ -d "$APP" ] || { echo "No $APP: run build.sh first." >&2; exit 1; }
BACKGROUND_DIR="${BACKGROUND_DIR:-dmg}"

# One TIFF holding both resolutions, so the window is sharp on Retina displays.
tiffutil -cathidpicheck "$BACKGROUND_DIR/background.png" "$BACKGROUND_DIR/background@2x.png" \
    -out build/dmg-background.tiff >/dev/null

VOLUME_ICON=()
if [ -f "$APP/Contents/Resources/AppIcon.icns" ]; then
    VOLUME_ICON=(-D icon="$APP/Contents/Resources/AppIcon.icns")
fi

rm -f build/Countdown.dmg
# ${VOLUME_ICON[@]+...}: macOS's bash 3.2 treats an empty array as unset under set -u.
dmgbuild -s dmg/settings.py -D app="$APP" -D background=build/dmg-background.tiff ${VOLUME_ICON[@]+"${VOLUME_ICON[@]}"} \
    "Countdown" build/Countdown.dmg
hdiutil verify build/Countdown.dmg >/dev/null && echo "build/Countdown.dmg"
