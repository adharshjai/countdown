import Foundation

/// Pure text formatting for the menu bar title and the menu's "remaining" row.
///
/// Deliberately no `Calendar`: a day is always 86 400 s, so the figure is the plain difference
/// between two instants and never jumps by an hour across a DST change.
enum CountdownFormatter {
    private static let unitNames = ["day", "hour", "minute", "second"]
    private static let unitSeconds = [86_400, 3_600, 60, 1]

    /// Menu bar text: the largest non-zero unit and the next smaller one (dropped when zero),
    /// then " · " (U+00B7) and the event name. Once the target has passed: "🎉 <name>".
    static func menuBarTitle(remaining: TimeInterval, eventName: String) -> String {
        guard let parts = split(remaining) else { return "🎉 \(eventName)" }
        let first = firstNonZero(parts)
        let text = (first..<min(first + 2, 4))
            .filter { parts[$0] != 0 }
            .map { label(parts[$0], unitNames[$0]) }
            .joined(separator: ", ")
        return text + " · " + eventName
    }

    /// Menu row: three units from the largest non-zero one, zeros kept so the line keeps a
    /// fixed shape ("18 days, 0 hours, 12 minutes"). "Reached" once the target has passed.
    static func fullRemaining(remaining: TimeInterval) -> String {
        guard let parts = split(remaining) else { return "Reached" }
        let first = firstNonZero(parts)
        return (first..<min(first + 3, 4))
            .map { label(parts[$0], unitNames[$0]) }
            .joined(separator: ", ")
    }

    /// `[days, hours, minutes, seconds]` of the whole seconds remaining, or nil when the
    /// countdown is over. Floors, never rounds up: 59.9 s is "59 seconds", 0.9 s is over.
    /// Clamped to 1e12 s (~31 700 years) so an absurd stored date cannot overflow `Int`.
    private static func split(_ remaining: TimeInterval) -> [Int]? {
        guard remaining.isFinite, remaining >= 1 else { return nil }
        var rest = Int(min(remaining, 1e12).rounded(.down))
        return unitSeconds.map { size in
            let count = rest / size
            rest -= count * size
            return count
        }
    }

    /// Index of the first non-zero unit. `split` guarantees at least one second, so the
    /// fallback to the seconds slot is only defensive.
    private static func firstNonZero(_ parts: [Int]) -> Int {
        parts.firstIndex { $0 != 0 } ?? parts.count - 1
    }

    private static func label(_ count: Int, _ unit: String) -> String {
        "\(count) \(unit)" + (count == 1 ? "" : "s")
    }
}
