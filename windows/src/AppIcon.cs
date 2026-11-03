using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Countdown
{
    internal static class AppIcon
    {
        static Stream? Open() => typeof(AppIcon).Assembly.GetManifestResourceStream("Countdown.ico");

        /// <summary>The full multi-size icon, for the settings window.</summary>
        public static Icon Load()
        {
            using Stream? stream = Open();
            return stream == null ? (Icon)SystemIcons.Application.Clone() : new Icon(stream);
        }

        /// <summary>The small icon, for the notification-area fallback.</summary>
        public static Icon LoadSmall()
        {
            using Stream? stream = Open();
            return stream == null ? (Icon)SystemIcons.Application.Clone() : new Icon(stream, SystemInformation.SmallIconSize);
        }
    }
}
