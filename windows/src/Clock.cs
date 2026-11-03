using System;

namespace Countdown
{
    internal static class Clock
    {
        /// <summary>
        /// Seconds from the start of the current second to <paramref name="targetUtc"/>, read
        /// fresh from the clock (never decremented). Snapping "now" to its whole second makes a
        /// tick that fires a few ms late still read 60 rather than 59.98, and 🎉 lands exactly
        /// on the target second. The same rule as the macOS app.
        /// </summary>
        public static double SecondsUntil(DateTime targetUtc)
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            long nowWholeSecond = nowTicks - nowTicks % TimeSpan.TicksPerSecond;
            return (targetUtc.Ticks - nowWholeSecond) / (double)TimeSpan.TicksPerSecond;
        }
    }
}
