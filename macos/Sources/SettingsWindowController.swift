import AppKit
import SwiftUI

/// The form's values. Owned by the window controller so `show` can repopulate them; the
/// SwiftUI view only binds to them.
@MainActor
final class SettingsModel: ObservableObject {
    @Published var eventName = ""
    @Published var target = Date()
    /// Bumped by `show` so the view moves focus to the name field every time the window opens.
    @Published var focusToken = 0
}

/// "Set Countdown": a live preview of the menu bar text, the event name, a calendar with a
/// time field, Cancel / Save.
@MainActor
struct SettingsView: View {
    @ObservedObject var model: SettingsModel
    let onCancel: () -> Void
    let onSave: () -> Void

    @FocusState private var nameFocused: Bool

    private var trimmedName: String {
        model.eventName.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            preview

            field("Event") {
                TextField("Trip to Japan", text: $model.eventName)
                    .textFieldStyle(.roundedBorder)
                    .focused($nameFocused)
                    .onSubmit { if !trimmedName.isEmpty { onSave() } }
            }

            field("Date and time") {
                DatePicker(
                    "Date and time",
                    selection: $model.target,
                    displayedComponents: [.date, .hourAndMinute]
                )
                .datePickerStyle(.graphical)
                .labelsHidden()
            }

            HStack(spacing: 12) {
                Spacer()
                Button("Cancel", action: onCancel)
                    .keyboardShortcut(.cancelAction)
                Button("Save", action: onSave)
                    .keyboardShortcut(.defaultAction)
                    .disabled(trimmedName.isEmpty)
            }
        }
        .padding(24)
        .frame(width: 360)
        .onAppear { nameFocused = true }
        .onChange(of: model.focusToken) { _, _ in nameFocused = true }
    }

    /// Exactly what the menu bar will say, ticking once a second like the real thing, drawn
    /// in the menu bar's own font inside a pill.
    private var preview: some View {
        TimelineView(.periodic(from: Date(), by: 1)) { context in
            let name = trimmedName.isEmpty ? "Event name" : trimmedName
            let remaining = model.target.timeIntervalSinceReferenceDate
                - floor(context.date.timeIntervalSinceReferenceDate)
            Text(CountdownFormatter.menuBarTitle(remaining: remaining, eventName: name))
                .font(Font(AppDelegate.menuBarFont as CTFont))
                .lineLimit(1)
                .foregroundStyle(trimmedName.isEmpty ? .secondary : .primary)
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .background(.quaternary, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                .frame(maxWidth: .infinity)
        }
    }

    private func field<Content: View>(_ label: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(label)
                .font(.subheadline.weight(.medium))
                .foregroundStyle(.secondary)
            content()
        }
    }
}

/// Hosts `SettingsView` in a fixed-size window that is built once, kept alive
/// (`isReleasedWhenClosed = false`) and repopulated by `show`.
@MainActor
final class SettingsWindowController: NSWindowController, NSWindowDelegate {
    var onSave: (Countdown) -> Void = { _ in }

    private let model = SettingsModel()

    init() {
        let window = NSWindow(
            contentRect: .zero,
            styleMask: [.titled, .closable],   // no .resizable: the window is a fixed-size form
            backing: .buffered,
            defer: false
        )
        window.title = "Set Countdown"
        window.isReleasedWhenClosed = false
        window.collectionBehavior = .moveToActiveSpace
        super.init(window: window)
        window.delegate = self

        let hosting = NSHostingController(rootView: SettingsView(
            model: model,
            onCancel: { [weak self] in self?.close() },
            onSave: { [weak self] in self?.save() }
        ))
        hosting.sizingOptions = .intrinsicContentSize
        window.contentViewController = hosting
        let size = hosting.view.fittingSize
        if size.width > 0, size.height > 0 {
            window.setContentSize(size)
        }
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    // MARK: - Showing

    /// Populates the form from `countdown` and brings the window to the front. The app is an
    /// LSUIElement, so it must activate itself first or the window would open behind others.
    func show(_ countdown: Countdown) {
        model.eventName = countdown.eventName
        model.target = countdown.target
        model.focusToken += 1
        NSApp.unhide(nil)
        NSApp.activate(ignoringOtherApps: true)
        window?.center()
        showWindow(nil)
    }

    // MARK: - Actions

    private func save() {
        let name = model.eventName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { return }
        onSave(Countdown(eventName: name, target: model.target))
        close()
    }

    // MARK: - NSWindowDelegate

    /// A Dock-less app with no window left would otherwise stay active and strand the keyboard
    /// focus; hiding hands activation back to whatever app was in front before.
    func windowWillClose(_ notification: Notification) {
        NSApp.hide(nil)
    }
}
