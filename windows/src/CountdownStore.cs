using System;
using System.Globalization;
using Microsoft.Win32;

namespace Countdown
{
    /// <summary>What the app counts down to: a name and an absolute instant in UTC.</summary>
    internal readonly struct CountdownTarget
    {
        public CountdownTarget(string name, DateTime whenUtc)
        {
            Name = name;
            WhenUtc = whenUtc;
        }

        public string Name { get; }
        public DateTime WhenUtc { get; }
    }

    /// <summary>
    /// Persistence under HKEY_CURRENT_USER\Software\Countdown: EventName and Target, the
    /// target as an ISO 8601 UTC string. <see cref="Validate"/> is pure so the self-check can
    /// exercise it.
    /// </summary>
    internal static class CountdownStore
    {
        public const string KeyPath = @"Software\Countdown";
        public const string NameValue = "EventName";
        public const string TargetValue = "Target";

        /// <summary>
        /// Shown until the person saves something: "New Year", 2027-01-01 00:00 in the local
        /// time zone. DateTime's constructor is always Gregorian, whatever the system calendar.
        /// </summary>
        public static readonly CountdownTarget Fallback = new CountdownTarget(
            "New Year", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime());

        /// <summary>The calendar's range (1753 to 9998), with a day of slack for time zones.</summary>
        static readonly DateTime EarliestUtc = new DateTime(1753, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        static readonly DateTime LatestUtc = new DateTime(9998, 12, 30, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Accepts a string name that is non-empty once trimmed (kept trimmed) together with a
        /// parseable date string; anything else (missing values, wrong types, a blank name, a
        /// corrupted date) falls back so the app never shows a nonsense countdown.
        /// </summary>
        public static (CountdownTarget target, bool usedFallback) Validate(object? name, object? target)
        {
            if (!(name is string rawName)) return (Fallback, true);
            string trimmed = rawName.Trim();
            if (trimmed.Length == 0) return (Fallback, true);
            if (!(target is string rawTarget)) return (Fallback, true);
            if (!DateTime.TryParse(rawTarget, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime whenUtc))
            {
                return (Fallback, true);
            }
            whenUtc = DateTime.SpecifyKind(whenUtc, DateTimeKind.Utc);
            if (whenUtc < EarliestUtc || whenUtc > LatestUtc) return (Fallback, true);
            return (new CountdownTarget(trimmed, whenUtc), false);
        }

        /// <summary>
        /// The fallback is never written back: an unsaved app keeps opening the settings window
        /// on every launch until the person saves their own event.
        /// </summary>
        public static (CountdownTarget target, bool usedFallback) Load()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
                return Validate(key?.GetValue(NameValue), key?.GetValue(TargetValue));
            }
            catch (Exception e)
            {
                Log.Write("load failed: " + e.Message);
                return (Fallback, true);
            }
        }

        public static void Save(CountdownTarget target)
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(NameValue, target.Name, RegistryValueKind.String);
            key.SetValue(TargetValue, target.WhenUtc.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);
        }
    }
}
