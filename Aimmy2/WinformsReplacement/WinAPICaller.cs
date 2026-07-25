using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using static WinformsReplacement.NativeMethods;

namespace Class
{
    public partial class WinAPICaller
    {
        #region Variables

        // Below is written by ChatGPT 3.5
        // Nori

        // Graphics.FromHwnd(IntPtr.Zero) owns a screen HDC, so it used to leak one for the whole
        // lifetime of the process. Read the DPI once and release it right away instead.
        private static readonly (float X, float Y) PrimaryDpi = GetPrimaryDpi();

        public static float scalingFactorX = PrimaryDpi.X / (float)96;
        public static float scalingFactorY = PrimaryDpi.Y / (float)96;

        public static int ScreenWidth = Screen.PrimaryScreen!.Bounds.Width;
        public static int ScreenHeight = Screen.PrimaryScreen!.Bounds.Height;

        // Last cursor position that GetCursorPos actually returned, handed back when it fails.
        private static System.Drawing.Point LastCursorPosition = System.Drawing.Point.Empty;

        //public static int ScreenWidth = Convert.ToInt16(Screen.PrimaryScreen!.Bounds.Width / scalingFactorX);
        //public static int ScreenHeight = Convert.ToInt16(Screen.PrimaryScreen!.Bounds.Height / scalingFactorY);

        #endregion Variables

        #region P/Invoke signatures

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        #endregion P/Invoke signatures

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        #endregion Structures

        #region Functions

        private static (float X, float Y) GetPrimaryDpi()
        {
            using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
            return (graphics.DpiX, graphics.DpiY);
        }

        // https://stackoverflow.com/questions/254197/how-can-i-get-the-active-screen-dimensions
        public void GetScreenWidth(Window MW)
        {
            var hwnd = new WindowInteropHelper(MW).EnsureHandle();
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new NativeMonitorInfo();
                GetMonitorInfo(monitor, monitorInfo);

                ScreenWidth = monitorInfo.Monitor.Right - monitorInfo.Monitor.Left;
                ScreenHeight = monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top;
            }
        }

        public static System.Drawing.Point GetCursorPosition()
        {
            if (GetCursorPos(out POINT lpPoint))
            {
                //                 return new System.Drawing.Point(Convert.ToInt16(lpPoint.X / scalingFactorX), Convert.ToInt16(lpPoint.Y / scalingFactorY));
                LastCursorPosition = new System.Drawing.Point(lpPoint.X, lpPoint.Y);
            }

            // GetCursorPos fails transiently (the secure desktop, a locked session). This is called from
            // the AI loop, so fall back to the last known position instead of throwing at it.
            return LastCursorPosition;
        }

        #endregion Functions
    }
}