using System.Runtime.InteropServices;

namespace Aimmy2.Class
{
    internal class ClickThroughOverlay
    {
        // Thanks to cobble (@castme) for giving me the hint :)
        // Based on: https://social.msdn.microsoft.com/Forums/en-US/a3cb7db6-5014-430f-a5c2-c9746b077d4f/click-through-windows-and-child-image-issue?forum=wpf
        // Nori

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;   // clicks pass through
        private const int WS_EX_TOOLWINDOW = 0x00000080;    // excluded from alt-tab / fullscreen detection
        private const int WS_EX_LAYERED = 0x00080000;       // required for true click-through
        private const int WS_EX_NOACTIVATE = 0x08000000;    // never steals focus / never becomes foreground

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        public static void MakeClickThrough(IntPtr hwnd)
        {
            // NOACTIVATE + TOOLWINDOW keep Windows from treating this transparent full-screen
            // overlay as the foreground "fullscreen app", which would otherwise hide/freeze the taskbar.
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

            // Apply the new styles without moving/resizing/reordering the window.
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
    }
}