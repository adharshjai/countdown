#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
APP="build/Countdown.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "build/ModuleCache"

# One universal binary, for Apple silicon and Intel Macs.
for ARCH in arm64 x86_64; do
  swiftc -O -parse-as-library -swift-version 5 -target "$ARCH-apple-macos14.0" \
    -module-cache-path "$PWD/build/ModuleCache" \
    -o "build/Countdown-$ARCH" Sources/*.swift
done
lipo -create build/Countdown-arm64 build/Countdown-x86_64 -output "$APP/Contents/MacOS/Countdown"
rm -f build/Countdown-arm64 build/Countdown-x86_64

cp "Info.plist" "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"
codesign --force --deep --sign - "$APP"

"$APP/Contents/MacOS/Countdown" --check
# The Intel half too, where Rosetta is installed to run it.
if arch -x86_64 /usr/bin/true 2>/dev/null; then
  echo "Intel build:"
  arch -x86_64 "$APP/Contents/MacOS/Countdown" --check | tail -1
fi
