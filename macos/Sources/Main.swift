import AppKit

@main
enum Main {
    /// `NSApplication.delegate` is a weak reference, so something has to own the delegate
    /// for the lifetime of the process.
    @MainActor private static var appDelegate: AppDelegate?

    @MainActor
    static func main() {
        // `--check` runs the pure formatter/store table and exits before `NSApplication.shared`
        // is ever touched: no GUI, no window-server connection, no UserDefaults, so the build
        // script can run it headless.
        if CommandLine.arguments.contains("--check") {
            exit(SelfCheck.run() ? 0 : 1)
        }

        let app = NSApplication.shared
        let delegate = AppDelegate()
        appDelegate = delegate
        app.delegate = delegate
        app.run()
    }
}
