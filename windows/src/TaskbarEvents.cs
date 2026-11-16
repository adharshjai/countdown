using System;

namespace Countdown
{
    /// <summary>
    /// Window events from the taskbar, so the label reacts at once instead of on its next
    /// one-second tick. Explorer moves its own windows above the label now and then (for example
    /// when an auto-hiding taskbar slides back in), which would hide the label until the tick
    /// brought it back; and an auto-hiding taskbar's slide is a series of moves.
    /// The hooks are out of context: their callbacks arrive on this thread's message loop.
    /// </summary>
    internal sealed class TaskbarEvents : IDisposable
    {
        readonly Native.WinEventProc callback; // kept in a field so the GC never frees it while hooked
        IntPtr taskbar;
        IntPtr reorderHook;
        IntPtr moveHook;

        /// <summary>The taskbar's child windows changed order: put the label back on top.</summary>
        public event Action? Reordered;

        /// <summary>The taskbar window moved or resized (auto-hide sliding, a display change).</summary>
        public event Action? Moved;

        public TaskbarEvents()
        {
            callback = OnEvent;
        }

        /// <summary>Watches this taskbar; again for a new one after Explorer restarts.</summary>
        public void Watch(IntPtr newTaskbar)
        {
            if (newTaskbar == taskbar && reorderHook != IntPtr.Zero) return;
            Unhook();
            taskbar = newTaskbar;
            uint thread = Native.GetWindowThreadProcessId(newTaskbar, out uint process);
            if (thread == 0) return;
            const uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
            reorderHook = Native.SetWinEventHook(Native.EVENT_OBJECT_REORDER, Native.EVENT_OBJECT_REORDER,
                IntPtr.Zero, callback, process, thread, flags);
            moveHook = Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, callback, process, thread, flags);
            Log.Write($"events: watching taskbar 0x{newTaskbar.ToInt64():X} (hooks {(reorderHook != IntPtr.Zero ? "on" : "off")})");
        }

        void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd != taskbar) return;
            if (eventType == Native.EVENT_OBJECT_REORDER)
            {
                Reordered?.Invoke();
            }
            else if (eventType == Native.EVENT_OBJECT_LOCATIONCHANGE && idObject == Native.OBJID_WINDOW)
            {
                Moved?.Invoke();
            }
        }

        void Unhook()
        {
            if (reorderHook != IntPtr.Zero) Native.UnhookWinEvent(reorderHook);
            if (moveHook != IntPtr.Zero) Native.UnhookWinEvent(moveHook);
            reorderHook = IntPtr.Zero;
            moveHook = IntPtr.Zero;
        }

        public void Dispose() => Unhook();
    }
}
