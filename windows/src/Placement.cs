using System;
using System.Collections.Generic;
using System.Linq;

namespace Countdown
{
    /// <summary>
    /// Where on the taskbar the label goes. Pure, so the self-check covers it.
    ///
    /// As far left as it fits: on Windows 11 that is right after the Widgets button, or the
    /// taskbar's left edge when there is no Widgets button. The free stretches (the parts of
    /// [areaLeft, areaRight] no taskbar button covers) are tried from the left. In each one the
    /// full text is tried first, then the time alone; the label only moves further right when a
    /// stretch cannot hold even the time. Null when nothing fits or the buttons are not known yet.
    /// </summary>
    internal static class Placement
    {
        public static (int x, bool compact)? Choose(int fullWidth, int compactWidth, int areaLeft, int areaRight,
            IReadOnlyList<(int left, int right)>? occupied, int margin)
        {
            if (occupied == null) return null; // not scanned yet: any spot could cover a button
            foreach (var gap in FreeStretches(areaLeft, areaRight, occupied))
            {
                int room = gap.right - gap.left - 2 * margin;
                if (fullWidth > 0 && fullWidth <= room) return (gap.left + margin, false);
                if (compactWidth > 0 && compactWidth <= room) return (gap.left + margin, true);
            }
            return null;
        }

        /// <summary>The complement of the occupied intervals within [left, right], left to right.</summary>
        public static List<(int left, int right)> FreeStretches(int left, int right, IReadOnlyList<(int left, int right)> occupied)
        {
            var gaps = new List<(int left, int right)>();
            int cursor = left;
            foreach (var span in occupied.Where(o => o.right > left && o.left < right).OrderBy(o => o.left))
            {
                if (span.left > cursor) gaps.Add((cursor, span.left));
                cursor = Math.Max(cursor, span.right);
            }
            if (cursor < right) gaps.Add((cursor, right));
            return gaps;
        }
    }
}
