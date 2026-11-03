using System;
using System.Collections.Generic;
using System.Globalization;

namespace Countdown
{
    /// <summary>
    /// Pure text formatting, a line-for-line port of the macOS CountdownFormatter so both apps
    /// print exactly the same strings. A day is always 86 400 s: the figure is the plain
    /// difference between two instants and never jumps by an hour across a DST change.
    /// </summary>
    internal static class CountdownFormatter
    {
        static readonly string[] UnitNames = { "day", "hour", "minute", "second" };
        static readonly long[] UnitSeconds = { 86_400, 3_600, 60, 1 };

        /// <summary>
        /// Taskbar text: the largest non-zero unit and the next smaller one (dropped when zero),
        /// then " · " (U+00B7) and the event name. Once the target has passed: "🎉 name".
        /// </summary>
        public static string TaskbarTitle(double remaining, string eventName)
        {
            long[]? parts = Split(remaining);
            if (parts == null) return "\U0001F389 " + eventName;
            return TwoUnits(parts) + " · " + eventName;
        }

        /// <summary>
        /// The same text without the event name ("18 days, 3 hours", or the party popper), for a
        /// taskbar too crowded for the full line.
        /// </summary>
        public static string TaskbarCompact(double remaining)
        {
            long[]? parts = Split(remaining);
            return parts == null ? "\U0001F389" : TwoUnits(parts);
        }

        static string TwoUnits(long[] parts)
        {
            int first = FirstNonZero(parts);
            var labels = new List<string>();
            for (int i = first; i < Math.Min(first + 2, 4); i++)
            {
                if (parts[i] != 0) labels.Add(Label(parts[i], UnitNames[i]));
            }
            return string.Join(", ", labels);
        }

        /// <summary>
        /// Menu row: three units from the largest non-zero one, zeros kept so the line keeps a
        /// fixed shape ("18 days, 0 hours, 12 minutes"). "Reached" once the target has passed.
        /// </summary>
        public static string FullRemaining(double remaining)
        {
            long[]? parts = Split(remaining);
            if (parts == null) return "Reached";
            int first = FirstNonZero(parts);
            var labels = new List<string>();
            for (int i = first; i < Math.Min(first + 3, 4); i++)
            {
                labels.Add(Label(parts[i], UnitNames[i]));
            }
            return string.Join(", ", labels);
        }

        /// <summary>
        /// [days, hours, minutes, seconds] of the whole seconds remaining, or null when the
        /// countdown is over. Floors, never rounds up: 59.9 s is "59 seconds", 0.9 s is over.
        /// Clamped to 1e12 s (~31 700 years) so an absurd stored date cannot overflow.
        /// </summary>
        static long[]? Split(double remaining)
        {
            if (double.IsNaN(remaining) || double.IsInfinity(remaining) || remaining < 1) return null;
            long rest = (long)Math.Floor(Math.Min(remaining, 1e12));
            var parts = new long[4];
            for (int i = 0; i < 4; i++)
            {
                parts[i] = rest / UnitSeconds[i];
                rest -= parts[i] * UnitSeconds[i];
            }
            return parts;
        }

        /// <summary>Index of the first non-zero unit; Split guarantees at least one second.</summary>
        static int FirstNonZero(long[] parts)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != 0) return i;
            }
            return parts.Length - 1;
        }

        /// <summary>
        /// Cuts <paramref name="text"/> to at most <paramref name="maxChars"/> UTF-16 units,
        /// ellipsis included, never between the two halves of a surrogate pair (an emoji).
        /// </summary>
        public static string Ellipsize(string text, int maxChars)
        {
            if (text.Length <= maxChars) return text;
            int keep = Math.Max(0, maxChars - 1);
            if (keep > 0 && char.IsHighSurrogate(text[keep - 1])) keep--;
            return text.Substring(0, keep).TrimEnd() + "…";
        }

        static string Label(long count, string unit) =>
            count.ToString(CultureInfo.InvariantCulture) + " " + unit + (count == 1 ? "" : "s");
    }
}
