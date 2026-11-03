import Foundation

/// Built-in regression table, run with `Countdown --check`. Only the pure pieces are
/// exercised (`CountdownFormatter`, `CountdownStore.validate`): no UserDefaults, no
/// NSApplication, so it runs headless from the build script.
enum SelfCheck {
    private struct Result {
        let input: String
        let got: String
        let expected: String
        var passed: Bool { got == expected }
    }

    /// Prints one `PASS`/`FAIL` line per case and a summary; true when every case passed.
    static func run() -> Bool {
        var results: [Result] = []

        func check(_ input: String, got: String, expected: String) {
            results.append(Result(input: input, got: got, expected: expected))
        }

        // menuBarTitle: two largest non-zero units, floor, singular/plural, 🎉 once passed.
        let menuBarCases: [(remaining: TimeInterval, name: String, note: String, expected: String)] = [
            (1_566_725, "X", "18d3h12m5s", "18 days, 3 hours · X"),
            (1_555_920, "X", "18d0h12m", "18 days · X"),
            (90_000, "X", "", "1 day, 1 hour · X"),
            (89_999, "X", "1d0h59m59s", "1 day · X"),
            (11_550, "X", "", "3 hours, 12 minutes · X"),
            (3_659, "X", "1h0m59s", "1 hour · X"),
            (330, "X", "", "5 minutes, 30 seconds · X"),
            (61, "X", "", "1 minute, 1 second · X"),
            (45, "X", "", "45 seconds · X"),
            (59.9, "X", "", "59 seconds · X"),
            (0.9, "X", "", "🎉 X"),
            (0, "X", "", "🎉 X"),
            (-1, "Trip to Japan", "", "🎉 Trip to Japan"),
            (-34_560_000, "X", "−400 days", "🎉 X"),
            (1e13, "X", "clamped", "11574074 days, 1 hour · X"),
            (.nan, "X", "non-finite", "🎉 X"),
        ]
        for c in menuBarCases {
            let note = c.note.isEmpty ? "" : " (\(c.note))"
            check(
                "menuBarTitle(\(describe(c.remaining))\(note), \"\(c.name)\")",
                got: CountdownFormatter.menuBarTitle(remaining: c.remaining, eventName: c.name),
                expected: c.expected
            )
        }

        // fullRemaining: three units from the largest non-zero one, zeros kept.
        let fullCases: [(remaining: TimeInterval, expected: String)] = [
            (1_566_725, "18 days, 3 hours, 12 minutes"),
            (1_555_920, "18 days, 0 hours, 12 minutes"),
            (3_659, "1 hour, 0 minutes, 59 seconds"),
            (330, "5 minutes, 30 seconds"),
            (45, "45 seconds"),
            (0, "Reached"),
            (-1, "Reached"),
        ]
        for c in fullCases {
            check(
                "fullRemaining(\(describe(c.remaining)))",
                got: CountdownFormatter.fullRemaining(remaining: c.remaining),
                expected: c.expected
            )
        }

        // CountdownStore.validate: blank or missing values fall back, good values are kept
        // (name trimmed). The date is a fixed instant that differs from the fallback's.
        let date = Date(timeIntervalSinceReferenceDate: 800_000_000)
        let validateCases: [(name: Any?, date: Any?, expected: String)] = [
            (nil, nil, "name=New Year; target=fallback; usedFallback=true"),
            ("  \n", date, "name=New Year; target=fallback; usedFallback=true"),
            ("Trip", date, "name=Trip; target=kept; usedFallback=false"),
            ("  Trip  ", date, "name=Trip; target=kept; usedFallback=false"),
            ("Trip", Date(timeIntervalSinceReferenceDate: .infinity), "name=New Year; target=fallback; usedFallback=true"),
            ("Trip", "2027-01-01", "name=New Year; target=fallback; usedFallback=true"),
            (42, date, "name=New Year; target=fallback; usedFallback=true"),
        ]
        for c in validateCases {
            let (countdown, usedFallback) = CountdownStore.validate(name: c.name, date: c.date)
            let target: String
            if countdown.target == CountdownStore.fallback.target {
                target = "fallback"
            } else if countdown.target == date {
                target = "kept"
            } else {
                target = "other"
            }
            check(
                "validate(name: \(describe(c.name)), date: \(describe(c.date)))",
                got: "name=\(countdown.eventName); target=\(target); usedFallback=\(usedFallback)",
                expected: c.expected
            )
        }

        var failures = 0
        for r in results {
            if r.passed {
                print("PASS \(r.input) -> \"\(r.got)\"")
            } else {
                failures += 1
                print("FAIL \(r.input) -> \"\(r.got)\" expected \"\(r.expected)\"")
            }
        }
        print("\(results.count - failures)/\(results.count) passed")
        return failures == 0
    }

    /// Whole seconds print as integers ("1566725", not "1566725.0"); the rest as Swift prints them.
    private static func describe(_ seconds: TimeInterval) -> String {
        if seconds.isFinite, seconds == seconds.rounded(), abs(seconds) < 1e15 {
            return String(Int(seconds))
        }
        return String(seconds)
    }

    private static func describe(_ value: Any?) -> String {
        guard let value else { return "nil" }
        if let string = value as? String { return string.debugDescription }
        if let date = value as? Date { return "Date(\(describe(date.timeIntervalSinceReferenceDate)))" }
        return String(describing: value)
    }
}
