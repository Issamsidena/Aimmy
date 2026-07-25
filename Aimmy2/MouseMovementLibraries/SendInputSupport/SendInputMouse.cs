using Other;
using System.Runtime.InteropServices;

namespace MouseMovementLibraries.SendInputSupport
{
    internal class SendInputMouse
    {
        // Admittedly written by ChatGPT, I accidentially had ChatGPT cook this up while asking it to rewrite some
        // python script someone sent me over "Raw Input Manipulation" (never heard of it) and it came up with a SendInput Class
        // I know I know, it's similar to Mouse Event, but I decided to add it anyways :shrug:

        // Nori

        // SendInput returns the number of events it actually inserted; 0 means the injection was
        // blocked (UIPI, an anti-cheat filter, another hook) and used to be reported as success.
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(int nInputs, INPUT[] pInputs, int cbSize);

        private static bool BlockedNotified = false;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static void SendMouseCommand(uint MouseCommand, int x = 0, int y = 0)
        {
            INPUT input = new INPUT
            {
                type = 0,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = x,
                        dy = y,
                        dwFlags = MouseCommand
                    }
                }
            };

            if (SendInput(1, [input], Marshal.SizeOf(typeof(INPUT))) == 0)
            {
                NotifyInjectionBlocked(Marshal.GetLastWin32Error());
            }
        }

        private static void NotifyInjectionBlocked(int errorCode)
        {
            if (BlockedNotified) return;
            BlockedNotified = true;

            try
            {
                LogManager.Log(LogManager.LogLevel.Error, $"SendInput was blocked (error {errorCode}), no mouse input is being delivered. Please try a different Mouse Movement Method.", true);
            }
            catch
            {
                // Notifying is best effort, it must never throw back into the AI loop.
            }
        }
    }
}