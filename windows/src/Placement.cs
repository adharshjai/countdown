using System;
using System.Collections.Generic;
using System.Linq;

namespace Countdown
{
    /// <summary>
    /// Where on the taskbar the label goes. Pure, so the self-check covers it.
    ///
    /// The free stretches are the parts of [areaLeft, areaRight] that no taskbar button covers.
    /// The label hugs the notification area when the stretch beside it is wide enough (like the
    /// clock), else it takes the widest other stretch, left-aligned (on Windows 11 that is
    /// usually the empty left side next to the centred icons). Null when nothing fits or
    /// the buttons are not known yet.
    /// </summary>
    internal static class Placement
    {
        public static int? Choose(int width, int areaLeft, int areaRight, IReadOnlyList<(int left, int right)>? occupied, int margin)
        {
            if (width <= 0 || areaRight - areaLeft < width) return null;
            if (occupied == null) return null; // not scanned yet: any spot could cover a button

            var gaps = FreeStretches(areaLeft, areaRight, occupied);
            var besideTray = gaps.FirstOrDefault(g => g.right == areaRight);
            if (besideTray.right == areaRight && besideTray.right - besideTray.left >= width + margin)
            {
                return areaRight - width;
            }
            foreach (var gap in gaps.OrderByDescending(g => g.right - g.left))
            {
                if (gap.right == areaRight) continue;
                if (gap.right - gap.left >= width + 2 * margin) return gap.left + margin;
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
