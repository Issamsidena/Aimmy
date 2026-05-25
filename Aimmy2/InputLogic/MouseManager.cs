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
        private static bool isSpraying = false;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        internal static double previousX = 0;
        internal static double previousY = 0;
        public static double smoothingFactor = 0.5;
        public static bool IsEMASmoothingEnabled = false;

        // Fractional mouse pixels left over so small X/Y slider steps change speed smoothly.
        private static double _antiRecoilResidualX;
        private static double _antiRecoilResidualY;

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        private static Random MouseRandom = new();

        internal static double EmaSmoothing(double previousValue, double currentValue, double smoothingFactor) => (currentValue * smoothingFactor) + (previousValue * (1 - smoothingFactor));

        // Cleanup
        private static (Action down, Action up) GetMouseActions()
        {
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

            return (mouseDownAction, mouseUpAction);
        }

        public static async Task DoTriggerClick(RectangleF? detectionBox = null)
        {
            // Gate by Auto Trigger Keybind / Constant AI Shooting (no longer tied to Aim Keybind).
            if (!(Dictionary.toggleState["Constant AI Shooting"]
                  || InputBindingManager.IsHoldingBinding("Auto Trigger Keybind")))
            {
                ResetSprayState();
                return;
            }

            if (Dictionary.toggleState["Spray Mode"])
            {
                if (Dictionary.toggleState["Cursor Check"])
                {
                    Point mousePos = WinAPICaller.GetCursorPosition();

                    if (detectionBox.HasValue && !detectionBox.Value.Contains(mousePos.X, mousePos.Y))
                    {
                        if (isSpraying) ReleaseMouseButton();
                        return;
                    }
                }

                if (!isSpraying) HoldMouseButton();
                return;
            }

            // Single click logic if spray mode off
            int timeSinceLastClick = (int)(DateTime.UtcNow - LastClickTime).TotalMilliseconds;
            int triggerDelayMilliseconds = (int)(Dictionary.sliderSettings["Auto Trigger Delay"] * 1000);
            const int clickDelayMilliseconds = 20;

            if (timeSinceLastClick < triggerDelayMilliseconds && LastClickTime != DateTime.MinValue)
            {
                return;
            }

            var (mouseDown, mouseUp) = GetMouseActions();

            mouseDown.Invoke();
            await Task.Delay(clickDelayMilliseconds);
            mouseUp.Invoke();

            LastClickTime = DateTime.UtcNow;
        }

        #region Spray Mode Methods
        public static void HoldMouseButton()
        {
            if (isSpraying) return;

            var (mouseDown, _) = GetMouseActions();
            mouseDown.Invoke();
            isSpraying = true;
        }

        public static void ReleaseMouseButton()
        {
            if (!isSpraying) return;

            var (_, mouseUp) = GetMouseActions();
            mouseUp.Invoke();
            isSpraying = false;
        }

        public static void ResetSprayState()
        {
            if (isSpraying)
            {
                ReleaseMouseButton();
            }
        }
        #endregion

        public static void MoveCrosshair(int detectedX, int detectedY)
        {
            int halfScreenWidth = (int)ScreenWidth / 2;
            int halfScreenHeight = (int)ScreenHeight / 2;

            int targetX = detectedX - halfScreenWidth;
            int targetY = detectedY - halfScreenHeight;

            double aspectRatioCorrection = ScreenWidth / ScreenHeight;

            int MouseJitter = (int)Dictionary.sliderSettings["Mouse Jitter"];
            int jitterX = MouseRandom.Next(-MouseJitter, MouseJitter);
            int jitterY = MouseRandom.Next(-MouseJitter, MouseJitter);

            Point start = new(0, 0);
            Point end = new(targetX, targetY);
            Point newPosition = new Point(0, 0);

            switch (Dictionary.dropdownState["Movement Path"])
            {
                // "None" mirrors v2.6.5 Cubic Bezier exactly (default behavior).
                case "None":
                    {
                        Point noneC1 = new Point(start.X + (end.X - start.X) / 3, start.Y + (end.Y - start.Y) / 3);
                        Point noneC2 = new Point(start.X + 2 * (end.X - start.X) / 3, start.Y + 2 * (end.Y - start.Y) / 3);
                        newPosition = MovementPaths.CubicBezier(start, end, noneC1, noneC2, 1 - Dictionary.sliderSettings["Mouse Sensitivity (+/-)"]);
                        break;
                    }
                // "Cubic Bezier" mirrors legacy v2.5.5 — math only, EMA applied below by MoveCrosshair.
                case "Cubic Bezier":
                    Point control1 = new Point(start.X + (end.X - start.X) / 3, start.Y + (end.Y - start.Y) / 3);
                    Point control2 = new Point(start.X + 2 * (end.X - start.X) / 3, start.Y + 2 * (end.Y - start.Y) / 3);
                    newPosition = MovementPaths.CubicBezierLegacy(start, end, control1, control2, 1 - Dictionary.sliderSettings["Mouse Sensitivity (+/-)"]);
                    break;
                case "Linear":
                    newPosition = MovementPaths.Lerp(start, end, 1 - Dictionary.sliderSettings["Mouse Sensitivity (+/-)"]);
                    break;
                case "Exponential":
                    newPosition = MovementPaths.Exponential(start, end, 1 - (Dictionary.sliderSettings["Mouse Sensitivity (+/-)"] - 0.2), 3.0);
                    break;
                case "Adaptive":
                    newPosition = MovementPaths.Adaptive(start, end, 1 - Dictionary.sliderSettings["Mouse Sensitivity (+/-)"]);
                    break;
                case "Perlin Noise":
                    newPosition = MovementPaths.PerlinNoise(start, end, 1 - Dictionary.sliderSettings["Mouse Sensitivity (+/-)"], 20, 0.5);
                    break;
                default:
                    newPosition = MovementPaths.Lerp(start, end, 1 - Dictionary.sliderSettings["Mouse Sensitivity (+/-)"]);
                    break;
            }

            // "None" applies EMA internally (v2.6.5 behavior); skip post-call only for None to avoid double-smoothing.
            // "Cubic Bezier" stays on the legacy v2.5.5 path — EMA must run here.
            if (IsEMASmoothingEnabled && Dictionary.dropdownState["Movement Path"] != "None")
            {
                newPosition.X = (int)EmaSmoothing(previousX, newPosition.X, smoothingFactor);
                newPosition.Y = (int)EmaSmoothing(previousY, newPosition.Y, smoothingFactor);
            }

            newPosition.X = Math.Clamp(newPosition.X, -150, 150);
            newPosition.Y = Math.Clamp(newPosition.Y, -150, 150);

            newPosition.Y = (int)(newPosition.Y / aspectRatioCorrection);

            newPosition.X += jitterX;
            newPosition.Y += jitterY;

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

            previousX = newPosition.X;
            previousY = newPosition.Y;

            if (!Dictionary.toggleState["Auto Trigger"])
            {
                ResetSprayState();
            }
        }

        #region Anti Recoil

        // Adds (value * dt) to residuals and returns whole pixels to send now.
        private static void AccumulateAntiRecoilPixels(double rawXPerSecond, double rawYPerSecond, double dtSeconds, out int dx, out int dy)
        {
            _antiRecoilResidualX += rawXPerSecond * dtSeconds;
            _antiRecoilResidualY += rawYPerSecond * dtSeconds;

            dx = (int)Math.Truncate(_antiRecoilResidualX);
            dy = (int)Math.Truncate(_antiRecoilResidualY);
            _antiRecoilResidualX -= dx;
            _antiRecoilResidualY -= dy;
        }

        // Maps |slider| to movement per second. ~10 = slow, ~100 = medium, ~1000 = fast.
        private static double ApplyAntiRecoilCurve(double raw)
        {
            const double maxInput = 1000.0;
            const double speedAt10 = 5.0;
            const double speedAt100 = 50.0;
            const double speedAt1000 = 500.0;

            double sign = Math.Sign(raw);
            double x = Math.Abs(raw);
            if (x <= 0) return 0.0;
            if (x >= maxInput)
                return sign * speedAt1000;

            double speed;
            if (x <= 10.0)
                speed = (speedAt10 / 10.0) * x;
            else if (x <= 100.0)
                speed = speedAt10 + (speedAt100 - speedAt10) * (x - 10.0) / 90.0;
            else
            {
                double t = (x - 100.0) / 900.0;
                double u = t * t * (3.0 - 2.0 * t);
                speed = speedAt100 + (speedAt1000 - speedAt100) * u;
            }

            return sign * speed;
        }

        // Fire Rate timing curve: 1ms = fastest, 100ms = neutral, 1000ms = slow.
        private static double ApplyAntiRecoilFireRateTiming(double fireRateMs)
        {
            double fr = Math.Clamp(fireRateMs, 1.0, 1000.0);
            double ratio = 100.0 / fr;
            double multiplier = Math.Pow(ratio, 0.5);
            return Math.Clamp(multiplier, 0.25, 10.0);
        }

        private static double ReadAntiRecoilDouble(string key, double defaultValue) =>
            Dictionary.AntiRecoilSettings.TryGetValue(key, out var v) ? Convert.ToDouble(v) : defaultValue;

        public static void DoAntiRecoil(double dtSeconds, double sustainedAfterDelaySec = 0.0)
        {
            if (dtSeconds <= 0) return;

            double rawX = Convert.ToDouble(Dictionary.AntiRecoilSettings["X Recoil (Left/Right)"]);
            double rawY = Convert.ToDouble(Dictionary.AntiRecoilSettings["Y Recoil (Up/Down)"]);
            double fireRateMs = Convert.ToDouble(Dictionary.AntiRecoilSettings["Fire Rate"]);

            double xPerSecond = ApplyAntiRecoilCurve(rawX);
            double yPerSecond = ApplyAntiRecoilCurve(rawY);
            double timing = ApplyAntiRecoilFireRateTiming(fireRateMs);

            xPerSecond *= timing;
            yPerSecond *= timing;

            if (Dictionary.toggleState.TryGetValue("Adaptive Recoil", out var adaptiveObj)
                && adaptiveObj is true
                && sustainedAfterDelaySec > 0)
            {
                double driftRawX = ReadAntiRecoilDouble("Drift Compensation X (Left/Right)",
                    ReadAntiRecoilDouble("Drift Compensation X", 0.0));
                double driftRawY = ReadAntiRecoilDouble("Drift Compensation Y (Up/Down)",
                    ReadAntiRecoilDouble("Drift Compensation Y", 0.0));
                double sprayFadeX = Convert.ToDouble(Dictionary.AntiRecoilSettings["Spray Fade X"]);
                double sprayFadeY = Convert.ToDouble(Dictionary.AntiRecoilSettings["Spray Fade Y"]);
                double sprayFadeXSpeedSec = Math.Clamp(
                    ReadAntiRecoilDouble("Spray Fade X Speed", 1.0), 1.0, 120.0);
                double sprayFadeYSpeedSec = Math.Clamp(
                    ReadAntiRecoilDouble("Spray Fade Y Speed", 1.0), 1.0, 120.0);
                double driftXSpeedSec = Math.Clamp(
                    ReadAntiRecoilDouble("Drift Compensation X Speed", 1.0), 1.0, 120.0);
                double driftYSpeedSec = Math.Clamp(
                    ReadAntiRecoilDouble("Drift Compensation Y Speed", 1.0), 1.0, 120.0);

                double driftTx = Math.Min(1.0, sustainedAfterDelaySec / driftXSpeedSec);
                double driftTy = Math.Min(1.0, sustainedAfterDelaySec / driftYSpeedSec);
                double driftBoostX = ApplyAntiRecoilCurve(driftRawX) * driftTx;
                double driftBoostY = ApplyAntiRecoilCurve(driftRawY) * driftTy;

                double fadeX = sprayFadeX <= 0
                    ? 1.0
                    : Math.Max(0.0, 1.0 - (sprayFadeX / 100.0) * (sustainedAfterDelaySec / sprayFadeXSpeedSec));
                double fadeY = sprayFadeY <= 0
                    ? 1.0
                    : Math.Max(0.0, 1.0 - (sprayFadeY / 100.0) * (sustainedAfterDelaySec / sprayFadeYSpeedSec));

                xPerSecond = xPerSecond * fadeX + driftBoostX * timing;
                yPerSecond = yPerSecond * fadeY + driftBoostY * timing;
            }

            AccumulateAntiRecoilPixels(xPerSecond, yPerSecond, dtSeconds, out int xRecoil, out int yRecoil);

            if (xRecoil == 0 && yRecoil == 0) return;

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
                    mouse_event(MOUSEEVENTF_MOVE,
                        unchecked((uint)(short)Math.Clamp(xRecoil, short.MinValue, short.MaxValue)),
                        unchecked((uint)(short)Math.Clamp(yRecoil, short.MinValue, short.MaxValue)),
                        0, 0);
                    break;
            }
        }

        public static void DoAntiRecoil()
        {
            DoAntiRecoil(0.001, 0.0);
        }

        internal static void ResetAntiRecoilState()
        {
            _antiRecoilResidualX = 0;
            _antiRecoilResidualY = 0;
        }

        #endregion
    }
}