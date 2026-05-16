using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Other
{
    /// <summary>
    /// Detects when a fullscreen / borderless / windowed-fullscreen game is focused so overlay notices can be suppressed.
    /// </summary>
    internal static class GameplayNoticeFilter
    {
        private const double MinScreenCoverage = 0.88;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// True when another app is focused and its window covers most of the monitor (typical for games).
        /// </summary>
        public static bool ShouldSuppressOverlayDuringGameplay()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;

            GetWindowThreadProcessId(hwnd, out uint foregroundPid);
            if (foregroundPid == 0 || foregroundPid == (uint)Process.GetCurrentProcess().Id)
                return false;

            if (!GetWindowRect(hwnd, out RECT rect))
                return false;

            int windowWidth = Math.Max(0, rect.Right - rect.Left);
            int windowHeight = Math.Max(0, rect.Bottom - rect.Top);
            if (windowWidth < 100 || windowHeight < 100)
                return false;

            try
            {
                var screen = Screen.FromHandle(hwnd);
                int screenWidth = screen.Bounds.Width;
                int screenHeight = screen.Bounds.Height;
                if (screenWidth <= 0 || screenHeight <= 0)
                    return false;

                double widthRatio = windowWidth / (double)screenWidth;
                double heightRatio = windowHeight / (double)screenHeight;
                return widthRatio >= MinScreenCoverage && heightRatio >= MinScreenCoverage;
            }
            catch
            {
                return false;
            }
        }
    }
}
