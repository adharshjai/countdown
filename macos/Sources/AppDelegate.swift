import AppKit
import ServiceManagement

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    /// The menu bar's own font with tabular digits, so the title does not wobble as the
    /// seconds change. Only the font is set: the bar's own text colours still apply.
    static let menuBarFont = NSFont.monospacedDigitSystemFont(
        ofSize: NSFont.menuBarFont(ofSize: 0).pointSize, weight: .regular)

    private var countdown = CountdownStore.fallback
    private lazy var statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private lazy var settings: SettingsWindowController = {
        // Built once; `onSave` is wired here so it cannot be attached twice.
        let controller = SettingsWindowController()
        controller.onSave = { [weak self] countdown in self?.apply(countdown) }
        return controller
    }()

    private var timer: Timer?
    private var observers: [NSObjectProtocol] = []
    private var currentTitle: String?

    // Grey information rows at the top of the menu, like Apple's Battery menu.
    private let nameItem = NSMenuItem()
    private let dateItem = NSMenuItem()
    private let remainingItem = NSMenuItem()
    private let launchAtLoginItem = NSMenuItem()

    /// "Thursday, January 1, 2027 at 12:00 AM"
    private let dateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateStyle = .full
        formatter.timeStyle = .short
        return formatter
    }()

    // MARK: - NSApplicationDelegate

    func applicationDidFinishLaunching(_ notification: Notification) {
        let (loaded, usedFallback) = CountdownStore.load()
        countdown = loaded

        let menu = NSMenu()
        menu.autoenablesItems = false   // the info rows stay disabled, the actions stay enabled
        menu.delegate = self
        for item in [nameItem, dateItem, remainingItem] {
            item.isEnabled = false
            menu.addItem(item)
        }
        menu.addItem(.separator())

        let setItem = NSMenuItem(title: "Set Countdown…", action: #selector(showSettings), keyEquivalent: "")
        setItem.target = self
        menu.addItem(setItem)

        launchAtLoginItem.title = "Launch at Login"
        launchAtLoginItem.target = self
        launchAtLoginItem.action = #selector(toggleLaunchAtLogin)
        menu.addItem(launchAtLoginItem)
        menu.addItem(.separator())

        let quitItem = NSMenuItem(title: "Quit Countdown", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        quitItem.target = NSApp
        menu.addItem(quitItem)

        statusItem.menu = menu

        tick()
        scheduleNextTick()
        installObservers()

        // Nothing saved yet: show the default countdown and ask for the person's own event.
        if usedFallback { showSettings() }
    }

    /// Relaunching a Dock-less app would otherwise look like nothing happened.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showSettings()
        return false
    }

    /// Silences the "secure coding not enabled for restorable state" console warning.
    func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool { true }

    // MARK: - Countdown state

    /// Seconds left, read fresh from the clock on every tick (never decremented), snapped to
    /// the whole second the tick is aligned to: a tick that fires a few ms late still reads 60
    /// rather than 59.998 ("59 seconds"), and 🎉 lands exactly on the target second.
    private func remainingNow() -> TimeInterval {
        countdown.target.timeIntervalSinceReferenceDate - floor(Date().timeIntervalSinceReferenceDate)
    }

    private func tick() {
        let title = CountdownFormatter.menuBarTitle(remaining: remainingNow(), eventName: countdown.eventName)
        guard title != currentTitle else { return }
        currentTitle = title
        statusItem.button?.attributedTitle = NSAttributedString(
            string: title, attributes: [.font: Self.menuBarFont])
    }

    private func apply(_ newCountdown: Countdown) {
        countdown = newCountdown
        CountdownStore.save(newCountdown)
        tick()
    }

    // MARK: - Timer

    /// One-shot timers, each armed for 5 ms past the next whole-second boundary and re-armed
    /// from the wall clock on every tick. Timer deadlines run on mach time, which drifts from
    /// the wall clock by a few ms a day and jumps after sleep; re-arming from `Date()` every
    /// second means that drift can never accumulate, and the 5 ms margin keeps a tick from
    /// landing a hair before the boundary and reading the previous second. Added in `.common`
    /// mode so it keeps ticking while the status menu is open (menu tracking runs the run loop
    /// in event-tracking mode).
    private func scheduleNextTick() {
        timer?.invalidate()
        let boundary = floor(Date().timeIntervalSinceReferenceDate) + 1
        let newTimer = Timer(
            fire: Date(timeIntervalSinceReferenceDate: boundary + 0.005), interval: 0, repeats: false
        ) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.tick()
                self.scheduleNextTick()
            }
        }
        newTimer.tolerance = 0
        RunLoop.main.add(newTimer, forMode: .common)
        timer = newTimer
    }

    /// After sleep or a clock change the pending deadline is meaningless: refresh the title at
    /// once and arm a fresh timer from the current wall clock.
    private func resync() {
        tick()
        scheduleNextTick()
    }

    /// Both observers are closure-based and delivered on the main queue: NSSystemClockDidChange
    /// may be posted from a background thread.
    private func installObservers() {
        let wake = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.resync()
            }
        }
        let clock = NotificationCenter.default.addObserver(
            forName: .NSSystemClockDidChange, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.resync()
            }
        }
        observers = [wake, clock]
    }

    // MARK: - NSMenuDelegate

    /// The figures are refreshed every time the menu opens; the checkmark is re-read because
    /// System Settings › Login Items can change it behind the app's back. A dash means macOS
    /// has the item but is still waiting for the person's approval.
    func menuWillOpen(_ menu: NSMenu) {
        let bold = NSFont.boldSystemFont(ofSize: NSFont.menuFont(ofSize: 0).pointSize)
        nameItem.attributedTitle = NSAttributedString(string: countdown.eventName, attributes: [.font: bold])
        dateItem.title = dateFormatter.string(from: countdown.target)
        remainingItem.title = CountdownFormatter.fullRemaining(remaining: remainingNow())
        switch SMAppService.mainApp.status {
        case .enabled: launchAtLoginItem.state = .on
        case .requiresApproval: launchAtLoginItem.state = .mixed
        default: launchAtLoginItem.state = .off
        }
    }

    // MARK: - Actions

    @objc private func showSettings() {
        settings.show(countdown)
    }

    @objc private func toggleLaunchAtLogin() {
        let service = SMAppService.mainApp

        // Already registered, waiting for approval: the only useful move is System Settings.
        if service.status == .requiresApproval {
            SMAppService.openSystemSettingsLoginItems()
            return
        }

        do {
            if service.status == .enabled {
                try service.unregister()
            } else {
                try service.register()
            }
        } catch {
            NSApp.activate(ignoringOtherApps: true)
            NSAlert(error: error).runModal()
            return
        }

        // macOS registered the item but is waiting for the person to approve it.
        if service.status == .requiresApproval {
            NSApp.activate(ignoringOtherApps: true)
            let alert = NSAlert()
            alert.messageText = "Approve Countdown under System Settings › Login Items"
            alert.informativeText = "macOS needs your approval before Countdown can launch at login."
            alert.addButton(withTitle: "Open Login Items")
            let later = alert.addButton(withTitle: "Not Now")
            later.keyEquivalent = "\u{1b}"
            if alert.runModal() == .alertFirstButtonReturn {
                SMAppService.openSystemSettingsLoginItems()
            }
        }
    }
}
