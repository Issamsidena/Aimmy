using Aimmy2.Class;
using Aimmy2.MouseMovementLibraries.GHubSupport;
using Class;
using MouseMovementLibraries.ddxoftSupport;
using MouseMovementLibraries.RazerSupport;
using MouseMovementLibraries.SendInputSupport;
using System.Drawing;
using System.Runtime.InteropServices;

namespace InputLogic
{
    internal class MouseManager
    {
        private static readonly double ScreenWidth = WinAPICaller.ScreenWidth;
        private static readonly double ScreenHeight = WinAPICaller.ScreenHeight;

        private static DateTime LastClickTime = DateTime.MinValue;

        private static readonly int[] permutation = new int[512];

        /// <summary>Fractional mouse pixels left over so small X/Y slider steps change speed smoothly (no 149→1px vs 150→2px cliffs).</summary>
        private static double _antiRecoilResidualX;
        private static double _antiRecoilResidualY;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private static double previousX = 0;
        private static double previousY = 0;
        public static double smoothingFactor = 0.5;
        public static bool IsEMASmoothingEnabled = false;

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        private static Random MouseRandom = new();

        private static Point CubicBezier(Point start, Point end, Point control1, Point control2, double t)
        {
            double u = 1 - t;
            double tt = t * t;
            double uu = u * u;

            double x = uu * u * start.X + 3 * uu * t * control1.X + 3 * u * tt * control2.X + tt * t * end.X;
            double y = uu * u * start.Y + 3 * uu * t * control1.Y + 3 * u * tt * control2.Y + tt * t * end.Y;

            if (IsEMASmoothingEnabled)
            {
                x = EmaSmoothing(previousX, x, smoothingFactor);
                y = EmaSmoothing(previousY, y, smoothingFactor);
            }

            return new Point((int)x, (int)y);
        }

        private static Point Lerp(Point start, Point end, double t)
        {
            int x = (int)(start.X + (end.X - start.X) * t);
            int y = (int)(start.Y + (end.Y - start.Y) * t);
            return new Point(x, y);
        }

        private static Point Exponential(Point start, Point end, double t, double exponent)
        {
            double x = start.X + (end.X - start.X) * Math.Pow(t, exponent);
            double y = start.Y + (end.Y - start.Y) * Math.Pow(t, exponent);
            return new Point((int)x, (int)y);
        }

        /// <summary>Adaptive path: smooth blend between quick far sweeps and tighter near-target moves.</summary>
        private static Point Adaptive(Point start, Point end, double t, double threshold)
        {
            double distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            if (distance < threshold)
            {
                return Lerp(start, end, t);
            }
            else
            {
                Point control1 = new Point(start.X + (end.X - start.X) / 3, start.Y + (end.Y - start.Y) / 3);
                Point control2 = new Point(start.X + 2 * (end.X - start.X) / 3, start.Y + 2 * (end.Y - start.Y) / 3);
                return CubicBezier(start, end, control1, control2, t);
            }
        }

        private static Point PerlinNoise(Point start, Point end, double t, double amplitude = 10.0, double frequency = 0.1)
        {
            double baseX = start.X + (end.X - start.X) * t;
            double baseY = start.Y + (end.Y - start.Y) * t;

            double noiseX = Noise(t * frequency, 0) * amplitude;
            double noiseY = Noise(t * frequency, 100) * amplitude;

            double perpX = -(end.Y - start.Y);
            double perpY = end.X - start.X;
            double perpLength = Math.Sqrt(perpX * perpX + perpY * perpY);

            if (perpLength > 0)
            {
                perpX /= perpLength;
                perpY /= perpLength;
            }

            double finalX = baseX + perpX * noiseX + noiseY * 0.3;
            double finalY = baseY + perpY * noiseX + noiseY * 0.3;

            return new Point((int)finalX, (int)finalY);
        }

        private static double Fade(double t)
        {
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + t * (b - a);
        }

        private static double Grad(int hash, double x, double y)
        {
            int h = hash & 15;
            double u = h < 8 ? x : y;
            double v = h < 4 ? y : h == 12 || h == 14 ? x : 0;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        private static double Noise(double x, double y)
        {
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;

            x -= Math.Floor(x);
            y -= Math.Floor(y);

            double u = Fade(x);
            double v = Fade(y);

            int A = permutation[X] + Y;
            int AA = permutation[A];
            int AB = permutation[A + 1];
            int B = permutation[X + 1] + Y;
            int BA = permutation[B];
            int BB = permutation[B + 1];

            return Lerp(Lerp(Grad(permutation[AA], x, y),
                           Grad(permutation[BA], x - 1, y), u),
                      Lerp(Grad(permutation[AB], x, y - 1),
                           Grad(permutation[BB], x - 1, y - 1), u), v);
        }

        private static double EmaSmoothing(double previousValue, double currentValue, double smoothingFactor) => (currentValue * smoothingFactor) + (previousValue * (1 - smoothingFactor));

        public static async Task DoTriggerClick(RectangleF lastDetectionBox)
        {
            int timeSinceLastClick = (int)(DateTime.UtcNow - LastClickTime).TotalMilliseconds;
            int triggerDelayMilliseconds = (int)(Dictionary.sliderSettings["Auto Trigger Delay"] * 1000);
            const int clickDelayMilliseconds = 20;

            if (timeSinceLastClick < triggerDelayMilliseconds && LastClickTime != DateTime.MinValue)
            {
                return;
            }

            string mouseMovementMethod = Dictionary.dropdownState["Mouse Movement Method"];
            Action mouseDownAction;
            Action mouseUpAction;

            switch (mouseMovementMethod)
            {
                case "SendInput":
                    mouseDownAction = () => SendInputMouse.SendMouseCommand(MOUSEEVENTF_LEFTDOWN);
                    mouseUpAction = () => SendInputMouse.SendMouseCommand(MOUSEEVENTF_LEFTUP);
                    break;

                case "LG HUB":
                    mouseDownAction = () => LGMouse.Move(1, 0, 0, 0);
                    mouseUpAction = () => LGMouse.Move(0, 0, 0, 0);
                    break;

                case "Razer Synapse (Require Razer Peripheral)":
                    mouseDownAction = () => RZMouse.mouse_click(1);
                    mouseUpAction = () => RZMouse.mouse_click(0);
                    break;

                case "ddxoft Virtual Input Driver":
                    mouseDownAction = () => DdxoftMain.ddxoftInstance.btn!(1);
                    mouseUpAction = () => DdxoftMain.ddxoftInstance.btn(2);
                    break;

                default:
                    mouseDownAction = () => mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouseUpAction = () => mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    break;
            }

            mouseDownAction.Invoke();
            await Task.Delay(clickDelayMilliseconds);
            mouseUpAction.Invoke();

            LastClickTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Adds (value * dt) to residuals and returns whole pixels to send now.
        /// This keeps anti-recoil mathematically continuous even though mouse APIs take integers.
        /// </summary>
        private static void AccumulateAntiRecoilPixels(double rawXPerSecond, double rawYPerSecond, double dtSeconds, out int dx, out int dy)
        {
            _antiRecoilResidualX += rawXPerSecond * dtSeconds;
            _antiRecoilResidualY += rawYPerSecond * dtSeconds;

            // Truncate toward zero keeps +/- scaling symmetric for small values.
            dx = (int)Math.Truncate(_antiRecoilResidualX);
            dy = (int)Math.Truncate(_antiRecoilResidualY);
            _antiRecoilResidualX -= dx;
            _antiRecoilResidualY -= dy;
        }

        /// <summary>
        /// Maps |slider| to movement per second. Anchors (before Fire Rate timing):
        /// ~10 = slow, ~100 = medium, ~1000 = fast — with smooth growth between (no dead zones).
        /// Symmetric for +/- values; uses only float math (no rounding).
        /// </summary>
        private static double ApplyAntiRecoilCurve(double raw)
        {
            const double maxInput = 1000.0;

            // Target speeds (px/s) at reference slider magnitudes — tuned so 10/100/1000 feel distinct.
            const double speedAt10 = 5.0;      // slow, still visible drift
            const double speedAt100 = 50.0;    // medium compensation
            const double speedAt1000 = 500.0;  // strong / faster end of range

            double sign = Math.Sign(raw);
            double x = Math.Abs(raw);
            if (x <= 0) return 0.0;
            if (x >= maxInput)
                return sign * speedAt1000;

            double speed;
            if (x <= 10.0)
            {
                // 0..10: linear from 0 → slow anchor
                speed = (speedAt10 / 10.0) * x;
            }
            else if (x <= 100.0)
            {
                // 10..100: linear ramp slow → medium (smooth, predictable)
                speed = speedAt10 + (speedAt100 - speedAt10) * (x - 10.0) / 90.0;
            }
            else
            {
                // 100..1000: ease toward fast — slightly progressive so high values pull harder
                double t = (x - 100.0) / 900.0; // 0..1
                double u = t * t * (3.0 - 2.0 * t); // smoothstep: no harsh kink, still monotonic
                speed = speedAt100 + (speedAt1000 - speedAt100) * u;
            }

            return sign * speed;
        }

        /// <summary>
        /// Fire Rate timing curve:
        /// 1ms = fastest, 10ms = fast, 100ms = medium, 1000ms = slow.
        /// Returns a multiplier applied to recoil speed (dt-based, so still framerate independent).
        /// </summary>
        private static double ApplyAntiRecoilFireRateTiming(double fireRateMs)
        {
            // Clamp to UI range.
            double fr = Math.Clamp(fireRateMs, 1.0, 1000.0);

            // Use 100ms as the "neutral" baseline (medium).
            // Inverse progressive curve so small ms speeds up, large ms slows down.
            double ratio = 100.0 / fr;              // 1ms -> 100, 10ms -> 10, 100ms -> 1, 1000ms -> 0.1
            double multiplier = Math.Pow(ratio, 0.5); // sqrt to avoid insane jumps but keep strong separation

            // Keep within sane bounds.
            return Math.Clamp(multiplier, 0.25, 10.0);
        }

        /// <summary>
        /// Apply anti-recoil using dt-based linear scaling.
        /// Slider values are passed through a progressive curve then interpreted as pixels-per-second
        /// (or equivalent mouse units per second).
        /// </summary>
        public static void DoAntiRecoil(double dtSeconds)
        {
            if (dtSeconds <= 0)
                return;

            double rawX = Convert.ToDouble(Dictionary.AntiRecoilSettings["X Recoil (Left/Right)"]);
            double rawY = Convert.ToDouble(Dictionary.AntiRecoilSettings["Y Recoil (Up/Down)"]);
            double fireRateMs = Convert.ToDouble(Dictionary.AntiRecoilSettings["Fire Rate"]);

            double xPerSecond = ApplyAntiRecoilCurve(rawX);
            double yPerSecond = ApplyAntiRecoilCurve(rawY);
            double timing = ApplyAntiRecoilFireRateTiming(fireRateMs);

            xPerSecond *= timing;
            yPerSecond *= timing;

            AccumulateAntiRecoilPixels(xPerSecond, yPerSecond, dtSeconds, out int xRecoil, out int yRecoil);

            if (xRecoil == 0 && yRecoil == 0)
                return;

            switch (Dictionary.dropdownState["Mouse Movement Method"])
            {
                case "SendInput":
                    SendInputMouse.SendMouseCommand(MOUSEEVENTF_MOVE, xRecoil, yRecoil);
                    break;

                case "LG HUB":
                    LGMouse.Move(0, xRecoil, yRecoil, 0);
                    break;

                case "Razer Synapse (Require Razer Peripheral)":
                    RZMouse.mouse_move(xRecoil, yRecoil, true);
                    break;

                case "ddxoft Virtual Input Driver":
                    DdxoftMain.ddxoftInstance.movR!(xRecoil, yRecoil);
                    break;

                default:
                    mouse_event(MOUSEEVENTF_MOVE, unchecked((uint)(short)Math.Clamp(xRecoil, short.MinValue, short.MaxValue)),
                        unchecked((uint)(short)Math.Clamp(yRecoil, short.MinValue, short.MaxValue)), 0, 0);
                    break;
            }
        }

        public static void DoAntiRecoil()
        {
            // Backwards-compatible fallback path; dt-driven loop should call the overload with dt.
            DoAntiRecoil(0.001);
        }

        public static void MoveCrosshair(int detectedX, int detectedY)
        {
            int halfScreenWidth = (int)ScreenWidth / 2;
            int halfScreenHeight = (int)ScreenHeight / 2;

            int targetX = detectedX - halfScreenWidth;
            int targetY = detectedY - halfScreenHeight;

            double aspectRatioCorrection = ScreenWidth / ScreenHeight;
            double sensitivity = Convert.ToDouble(Dictionary.sliderSettings["Mouse Sensitivity (+/-)"]);
            double t = Math.Clamp(1 - sensitivity, 0.0, 1.0);
            string movementPath = Dictionary.dropdownState.TryGetValue("Movement Path", out var pathValue)
                ? pathValue?.ToString() ?? "Cubic Bezier"
                : "Cubic Bezier";

            int MouseJitter = (int)Dictionary.sliderSettings["Mouse Jitter"];
            int jitterX = MouseRandom.Next(-MouseJitter, MouseJitter);
            int jitterY = MouseRandom.Next(-MouseJitter, MouseJitter);

            Point start = new(0, 0);
            Point end = new(targetX, targetY);
            Point control1 = new(start.X + (end.X - start.X) / 3, start.Y + (end.Y - start.Y) / 3);
            Point control2 = new(start.X + 2 * (end.X - start.X) / 3, start.Y + 2 * (end.Y - start.Y) / 3);
            Point newPosition = movementPath switch
            {
                // Very smooth + slower + more accurate
                "Exponential" => Exponential(start, end, Math.Clamp(t + 0.10, 0.0, 1.0), 2.9),
                // Keep as previously tuned: a bit faster, still accurate
                "Linear" => Lerp(start, end, Math.Min(1.0, t * 1.1 + 0.035)),
                "Adaptive" => Adaptive(start, end, t, 76.0),
                // No jitter + slower + more accurate + low smooth (noise disabled)
                "Perlin Noise" => PerlinNoise(start, end, Math.Clamp(t + 0.06, 0.0, 1.0), 10.0, 0.1),
                _ => CubicBezier(start, end, control1, control2, t)
            };

            targetX = Math.Clamp(targetX, -150, 150);
            targetY = Math.Clamp(targetY, -150, 150);

            targetY = (int)(targetY * aspectRatioCorrection);

            targetX += jitterX;
            targetY += jitterY;

            switch (Dictionary.dropdownState["Mouse Movement Method"])
            {
                case "SendInput":
                    SendInputMouse.SendMouseCommand(MOUSEEVENTF_MOVE, newPosition.X, newPosition.Y);
                    break;

                case "LG HUB":
                    LGMouse.Move(0, newPosition.X, newPosition.Y, 0);
                    break;

                case "Razer Synapse (Require Razer Peripheral)":
                    RZMouse.mouse_move(newPosition.X, newPosition.Y, true);
                    break;

                case "ddxoft Virtual Input Driver":
                    DdxoftMain.ddxoftInstance.movR!(newPosition.X, newPosition.Y);
                    break;

                default:
                    mouse_event(MOUSEEVENTF_MOVE, (uint)newPosition.X, (uint)newPosition.Y, 0, 0);
                    break;
            }

            if (Dictionary.toggleState["Auto Trigger"])
            {
                _ = Task.Run(async () =>
                {
                    try { await DoTriggerClick(default); }
                    catch { /* avoid unobserved fault killing the process */ }
                });
            }
        }

        internal static void ResetAntiRecoilState()
        {
            _antiRecoilResidualX = 0;
            _antiRecoilResidualY = 0;
        }

        internal static Task DoTriggerClick()
        {
            return DoTriggerClick(default);
        }

        internal static void ResetSprayState()
        {
            LastClickTime = DateTime.MinValue;
        }
    }
}