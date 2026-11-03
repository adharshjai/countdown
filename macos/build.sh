#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
APP="build/Countdown.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "build/ModuleCache"
swiftc -O -parse-as-library -swift-version 5 -target arm64-apple-macos14.0 \
  -module-cache-path "$PWD/build/ModuleCache" \
  -o "$APP/Contents/MacOS/Countdown" Sources/*.swift
cp "Info.plist" "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"
codesign --force --deep --sign - "$APP"
"$APP/Contents/MacOS/Countdown" --check
