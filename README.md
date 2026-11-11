# Countdown

A small countdown that lives in the macOS menu bar or the Windows taskbar:

```
18 days, 3 hours · Trip to Japan
```

- It shows the two largest units left, then the event name, and updates every second.
- When the moment arrives it switches to `🎉 Trip to Japan` and stays that way.
- Click it for the full date, the exact time left, and the settings.
- One event at a time. On first launch it counts down to New Year and asks for yours.

## macOS

Requires macOS 14 or later, on Apple silicon or Intel.

1. Download `Countdown.dmg` from [Releases](../../releases) and open it.
2. In the window that opens, drag Countdown onto the Applications folder.
3. Open Countdown from Applications. The app is not notarized, so the first open needs a
   confirmation: allow it under **System Settings › Privacy & Security** ("Open Anyway").
4. Click the countdown in the menu bar and choose **Set Countdown…** to pick your event.
   **Launch at Login** in the same menu starts it with your Mac.

Build it yourself with Xcode or its command line tools:

```bash
bash macos/build.sh
```

The app lands in `macos/build/Countdown.app`, built for both Apple silicon and Intel. The
build script also runs the app's self-check, `Countdown --check`, which tests the text
formatting. To package it as the disk image, install [dmgbuild](https://github.com/dmgbuild/dmgbuild)
and run:

```bash
bash macos/make-dmg.sh
```

That writes `macos/build/Countdown.dmg`. The app icon and the disk image background are
drawn by the scripts in `macos/tools`. On GitHub, CI also drives the app's settings window and
menu on macOS 15 and 26, installs it from the disk image, and screenshots the disk image window.

## Windows

Requires Windows 10 or 11. It uses .NET Framework 4.8, which Windows already includes.

1. Download `Countdown.exe` from [Releases](../../releases) and put it somewhere permanent,
   for example a `Countdown` folder in your user folder.
2. Windows SmartScreen may warn that the app is unrecognized because it is not signed.
   Choose **More info**, then **Run anyway**.
3. The countdown appears in the taskbar, just left of the clock and tray icons. Click it and
   choose **Set Countdown…** to pick your event. **Start with Windows** starts it at sign-in.

It never covers app buttons. When the space beside the clock is taken it moves to the
taskbar's free space, usually the empty left side of a centred Windows 11 taskbar; with no
room at all it shows just the time, and as a last resort, or on a vertical taskbar, it
becomes a tray icon whose tooltip shows the countdown.
Settings are stored under `HKEY_CURRENT_USER\Software\Countdown`.

Build it yourself with the [.NET SDK](https://dotnet.microsoft.com/download):

```bash
dotnet build windows/Countdown.csproj -c Release -o windows/out
```

Then run `windows\out\Countdown.exe --check` to test the text formatting, or
`--diagnose` to print what the app sees of the taskbar.

## How it works

- **macOS** (Swift, AppKit and SwiftUI): an `NSStatusItem` with a monospaced-digit title and a
  SwiftUI settings window.
- **Windows** (C#, .NET Framework 4.8): a transparent layered window hosted inside the taskbar,
  drawn with the taskbar's own font and text color, light or dark. It finds the notification
  area through the classic window tree on Windows 10 and through UI Automation on Windows 11,
  and rebuilds itself if Explorer restarts.
- Both compute the time left from the clock on every tick instead of counting down, so the
  display stays correct after sleep, clock changes and daylight saving changes.

```
macos/      Swift sources, Info.plist and build.sh
windows/    C# sources, project file, manifest, icon and the CI smoke test
.github/    Builds on macOS and Windows, with screenshots from both Windows taskbars
```
