import Foundation

/// What the app counts down to. `target` is an absolute instant; the formatter works in
/// whole seconds from it, so the countdown never shifts when DST changes.
struct Countdown {
    var eventName: String
    var target: Date
}

/// Persistence in UserDefaults. `validate` is pure so the self-check can exercise it.
enum CountdownStore {
    static let eventNameKey = "eventName"
    static let targetDateKey = "targetDate"

    /// Shown until the user saves something: "New Year", 2027-01-01 00:00 in the local time
    /// zone. Always Gregorian, whatever calendar the system is set to; the epoch literal
    /// (2027-01-01T00:00:00Z) is only used if the calendar refuses the components.
    static let fallback: Countdown = {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .current
        return Countdown(
            eventName: "New Year",
            target: calendar.date(from: DateComponents(year: 2027, month: 1, day: 1))
                ?? Date(timeIntervalSince1970: 1_798_761_600)
        )
    }()

    /// Accepts a `String` that is non-empty after trimming whitespace/newlines (stored trimmed)
    /// together with a finite `Date`; anything else (missing keys, wrong types, blank names,
    /// a corrupted date) falls back so the app never shows a nonsense countdown.
    static func validate(name: Any?, date: Any?) -> (Countdown, usedFallback: Bool) {
        guard let rawName = name as? String else { return (fallback, true) }
        let trimmedName = rawName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedName.isEmpty else { return (fallback, true) }
        guard let date = date as? Date, date.timeIntervalSinceReferenceDate.isFinite else {
            return (fallback, true)
        }
        return (Countdown(eventName: trimmedName, target: date), false)
    }

    /// The fallback is never written back: an unsaved app keeps reopening the settings window
    /// on every launch until the person saves their own event.
    static func load() -> (Countdown, usedFallback: Bool) {
        let defaults = UserDefaults.standard
        return validate(
            name: defaults.object(forKey: eventNameKey),
            date: defaults.object(forKey: targetDateKey)
        )
    }

    static func save(_ countdown: Countdown) {
        let defaults = UserDefaults.standard
        defaults.set(countdown.eventName, forKey: eventNameKey)
        defaults.set(countdown.target, forKey: targetDateKey)
    }
}
