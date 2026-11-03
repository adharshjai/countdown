using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Countdown
{
    /// <summary>
    /// Built-in regression table, run with <c>Countdown.exe --check</c>. Only the pure pieces
    /// are exercised (the formatter, store validation, text-run splitting): no registry, no
    /// windows, so it runs headless in CI. The formatter cases match the macOS app's table.
    /// </summary>
    internal static class SelfCheck
    {
        public static bool Run()
        {
            var results = new List<(string input, string got, string expected)>();
            void Check(string input, string got, string expected) => results.Add((input, got, expected));

            var titleCases = new (double remaining, string name, string note, string expected)[]
            {
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
                (-34_560_000, "X", "-400 days", "🎉 X"),
                (1e13, "X", "clamped", "11574074 days, 1 hour · X"),
                (double.NaN, "X", "non-finite", "🎉 X"),
            };
            foreach (var c in titleCases)
            {
                string note = c.note.Length == 0 ? "" : $" ({c.note})";
                Check($"TaskbarTitle({Describe(c.remaining)}{note}, \"{c.name}\")",
                    CountdownFormatter.TaskbarTitle(c.remaining, c.name), c.expected);
            }

            var fullCases = new (double remaining, string expected)[]
            {
                (1_566_725, "18 days, 3 hours, 12 minutes"),
                (1_555_920, "18 days, 0 hours, 12 minutes"),
                (3_659, "1 hour, 0 minutes, 59 seconds"),
                (330, "5 minutes, 30 seconds"),
                (45, "45 seconds"),
                (0, "Reached"),
                (-1, "Reached"),
            };
            foreach (var c in fullCases)
            {
                Check($"FullRemaining({Describe(c.remaining)})", CountdownFormatter.FullRemaining(c.remaining), c.expected);
            }

            // Compact taskbar text: the same units without the name.
            var compactCases = new (double remaining, string expected)[]
            {
                (1_566_725, "18 days, 3 hours"),
                (45, "45 seconds"),
                (0, "\U0001F389"),
            };
            foreach (var c in compactCases)
            {
                Check($"TaskbarCompact({Describe(c.remaining)})", CountdownFormatter.TaskbarCompact(c.remaining), c.expected);
            }

            // Placement on a taskbar whose label area runs from x=10 to x=800 (the tray).
            var placementCases = new (string note, int width, (int, int)[]? occupied, string expected)[]
            {
                ("not scanned yet", 200, null, "none"),
                ("room beside the tray", 200, new[] { (0, 100), (300, 500) }, "600"),
                ("tray side crowded, room on the left", 200, new[] { (300, 700) }, "14"),
                ("picks the widest free stretch", 200, new[] { (200, 230), (480, 700) }, "234"),
                ("nothing fits", 200, new[] { (0, 790) }, "none"),
                ("wider than the taskbar", 900, new (int, int)[0], "none"),
            };
            foreach (var c in placementCases)
            {
                int? x = Placement.Choose(c.width, 10, 800, c.occupied, 4);
                Check($"Placement({c.note})", x.HasValue ? x.Value.ToString(CultureInfo.InvariantCulture) : "none", c.expected);
            }

            // Store validation: blank, missing or mistyped values fall back; good values are
            // kept (name trimmed). The date is a fixed instant that differs from the fallback's.
            const string goodDate = "2027-03-14T15:09:26.0000000Z";
            DateTime goodUtc = new DateTime(2027, 3, 14, 15, 9, 26, DateTimeKind.Utc);
            var validateCases = new (object? name, object? date, string expected)[]
            {
                (null, null, "name=New Year; target=fallback; usedFallback=True"),
                ("  \n", goodDate, "name=New Year; target=fallback; usedFallback=True"),
                ("Trip", goodDate, "name=Trip; target=kept; usedFallback=False"),
                ("  Trip  ", goodDate, "name=Trip; target=kept; usedFallback=False"),
                ("Trip", "2027-03-14T10:09:26-05:00", "name=Trip; target=kept; usedFallback=False"),
                ("Trip", "soon", "name=New Year; target=fallback; usedFallback=True"),
                ("Trip", 42, "name=New Year; target=fallback; usedFallback=True"),
                ("Trip", "1700-01-01T00:00:00Z", "name=New Year; target=fallback; usedFallback=True"),
                ("Trip", "9999-06-01T00:00:00Z", "name=New Year; target=fallback; usedFallback=True"),
                (42, goodDate, "name=New Year; target=fallback; usedFallback=True"),
            };
            foreach (var c in validateCases)
            {
                var (target, usedFallback) = CountdownStore.Validate(c.name, c.date);
                string kind = target.WhenUtc == CountdownStore.Fallback.WhenUtc ? "fallback"
                    : target.WhenUtc == goodUtc ? "kept"
                    : "other";
                Check($"Validate({DescribeValue(c.name)}, {DescribeValue(c.date)})",
                    $"name={target.Name}; target={kind}; usedFallback={usedFallback}", c.expected);
            }

            // Shortening never splits an emoji's surrogate pair.
            string party = "\U0001F389";
            var ellipsisCases = new (string text, int max, string expected)[]
            {
                ("Trip", 48, "Trip"),
                ("abcdefgh", 5, "abcd…"),
                ("abc" + party + "def", 5, "abc…"),
                ("abc " + party, 6, "abc " + party),
                ("ab   cdef", 6, "ab…"),
            };
            foreach (var c in ellipsisCases)
            {
                Check($"Ellipsize({DescribeValue(c.text)}, {c.max})", CountdownFormatter.Ellipsize(c.text, c.max), c.expected);
            }

            // Text runs: emoji go to Segoe UI Emoji, everything else to Segoe UI.
            var runCases = new (string text, string expected)[]
            {
                ("18 days · Home", "text:\"18 days · Home\""),
                ("🎉 Trip", "emoji:\"🎉\" text:\" Trip\""),
                ("Home ❤️ now", "text:\"Home \" emoji:\"❤️\" text:\" now\""),
                ("", ""),
            };
            foreach (var c in runCases)
            {
                string got = string.Join(" ", TextRaster.SplitRuns(c.text)
                    .Select(r => (r.emoji ? "emoji:" : "text:") + "\"" + r.text + "\""));
                Check($"SplitRuns({DescribeValue(c.text)})", got, c.expected);
            }

            int failures = 0;
            foreach (var r in results)
            {
                if (r.got == r.expected)
                {
                    Console.WriteLine($"PASS {r.input} -> \"{r.got}\"");
                }
                else
                {
                    failures++;
                    Console.WriteLine($"FAIL {r.input} -> \"{r.got}\" expected \"{r.expected}\"");
                }
            }
            Console.WriteLine($"{results.Count - failures}/{results.Count} passed");
            return failures == 0;
        }

        /// <summary>Whole seconds print as integers; the rest as round-trip doubles.</summary>
        static string Describe(double seconds)
        {
            if (!double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds == Math.Floor(seconds) && Math.Abs(seconds) < 1e15)
            {
                return ((long)seconds).ToString(CultureInfo.InvariantCulture);
            }
            return seconds.ToString("R", CultureInfo.InvariantCulture);
        }

        static string DescribeValue(object? value) => value switch
        {
            null => "null",
            string s => "\"" + s.Replace("\n", "\\n") + "\"",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?",
        };
    }
}
