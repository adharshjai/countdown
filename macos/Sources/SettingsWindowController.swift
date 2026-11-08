import AppKit
import SwiftUI

/// The form's values. Owned by the window controller so `show` can repopulate them; the
/// SwiftUI view reads them.
@MainActor
final class SettingsModel: ObservableObject {
    @Published var eventName = ""
    @Published var target = Date()
}

/// The event name field. It is an AppKit text field because the window has to focus it with
/// the whole name selected the moment it opens, so typing replaces the name. SwiftUI's
/// TextField can only be focused, which leaves the cursor after the name. The window
/// controller owns the field and keeps the model in step with it.
struct NameField: NSViewRepresentable {
    let field: NSTextField

    func makeNSView(context: Context) -> NSTextField { field }
    func updateNSView(_ nsView: NSTextField, context: Context) {}
}

/// "Set Countdown": a live preview of the menu bar text, the event name, a calendar with a
/// clock, Cancel / Save.
@MainActor
struct SettingsView: View {
    @ObservedObject var model: SettingsModel
    let nameField: NSTextField
    let onCancel: () -> Void
    let onSave: () -> Void

    private var trimmedName: String {
        model.eventName.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            preview

            field("Event") {
                NameField(field: nameField)
                    .frame(maxWidth: .infinity)
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
final class SettingsWindowController: NSWindowController, NSWindowDelegate, NSTextFieldDelegate {
    var onSave: (Countdown) -> Void = { _ in }

    private let model = SettingsModel()

    private let nameField: NSTextField = {
        let field = NSTextField()
        field.placeholderString = "Trip to Japan"
        field.bezelStyle = .roundedBezel
        field.font = .systemFont(ofSize: NSFont.systemFontSize)
        field.usesSingleLineMode = true
        field.lineBreakMode = .byTruncatingTail
        field.cell?.isScrollable = true
        field.cell?.wraps = false
        // Return saves through the field's action; leaving the field must not.
        (field.cell as? NSTextFieldCell)?.sendsActionOnEndEditing = false
        field.setContentHuggingPriority(.defaultLow, for: .horizontal)
        return field
    }()

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

        nameField.delegate = self
        nameField.target = self
        nameField.action = #selector(nameFieldReturned)

        let hosting = NSHostingController(rootView: SettingsView(
            model: model,
            nameField: nameField,
            onCancel: { [weak self] in self?.close() },
            onSave: { [weak self] in self?.save() }
        ))
        hosting.sizingOptions = .intrinsicContentSize
        window.contentViewController = hosting
        let size = hosting.view.fittingSize
        if size.width > 0, size.height > 0 {
            window.setContentSize(size)
        }
        window.initialFirstResponder = nameField
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    // MARK: - Showing

    /// Populates the form from `countdown` and brings the window to the front. The app is an
    /// LSUIElement, so it must activate itself or the window opens inactive behind others.
    /// macOS 14's cooperative `activate()` leaves a menu bar app that has just launched in the
    /// background (keystrokes then go to the previous app), so this uses the older call, which
    /// still brings it forward, and also orders the window front regardless.
    func show(_ countdown: Countdown) {
        model.eventName = countdown.eventName
        model.target = countdown.target
        nameField.stringValue = countdown.eventName
        NSApp.unhide(nil)
        NSApp.activate(ignoringOtherApps: true)
        window?.center()
        showWindow(nil)
        window?.orderFrontRegardless()
        focusName()
    }

    /// Focuses the name field with the whole name selected, as AppKit does when you tab into a
    /// field, so typing replaces the name. On the first show SwiftUI puts the field into the
    /// window during its first layout pass, so this waits for that, for up to half a second.
    private func focusName(attempt: Int = 0) {
        guard let window, window.isVisible else { return }
        if nameField.window === window {
            nameField.selectText(nil)
            return
        }
        guard attempt < 20 else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.025) { [weak self] in
            MainActor.assumeIsolated {
                self?.focusName(attempt: attempt + 1)
            }
        }
    }

    // MARK: - NSTextFieldDelegate

    /// Keeps the model, and with it the preview and the Save button, in step with typing.
    func controlTextDidChange(_ obj: Notification) {
        model.eventName = nameField.stringValue
    }

    // MARK: - Actions

    @objc private func nameFieldReturned() {
        save()
    }

    private func save() {
        guard window?.isVisible == true else { return }   // Return can reach both the field and Save
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
