using Aimmy2.Class;
using Aimmy2.MouseMovementLibraries.GHubSupport;
using Class;
using MouseMovementLibraries.ddxoftSupport;
using MouseMovementLibraries.RazerSupport;
using MouseMovementLibraries.SendInputSupport;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace InputLogic
{
    internal class MouseManager
    {
        // Read the SELECTED display every call. These used to be static readonly snapshots of
        // WinAPICaller's PRIMARY-monitor size, taken once at type load, while AIManager located
        // targets on DisplayManager's selected display -- so on any multi-monitor setup where the two
        // differ, every tick picked up a constant directional bias.
        private static double ScreenWidth => DisplayManager.ScreenWidth;
        private static double ScreenHeight => DisplayManager.ScreenHeight;

        // Where the crosshair actually is, in the same absolute screen space AIManager reports
        // targets in (its detection box is centred here).
        private static double CrosshairX => DisplayManager.ScreenLeft + (DisplayManager.ScreenWidth / 2.0);
        private static double CrosshairY => DisplayManager.ScreenTop + (DisplayManager.ScreenHeight / 2.0);

        private static DateTime LastClickTime = DateTime.MinValue;
        private static bool isSpraying = false;
        // Guards the physical button state so Auto Trigger and Rapid Fire cannot interleave a
        // down/up pair and strand the button held.
        private static readonly object _buttonLock = new();

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

        // Same trick for AIM movement. Without it every per-tick command below 1px truncated to zero
        // permanently, so slow settings froze instead of moving slowly and the only thing still
        // moving the mouse near the target was the random jitter.
        private static double _aimResidualX;
        private static double _aimResidualY;

        // Aim Strength target low-pass state: the smoothed target the crosshair actually aims at.
        private static double _smoothedTargetX;
        private static double _smoothedTargetY;
        private static bool _hasSmoothedTarget;

        // Per-tick timing so the response rate is defined per SECOND rather than per FRAME. Every
        // gain here used to be a per-frame fraction, which meant aim speed silently changed with
        // framerate, resolution, GPU load and the AI FPS Limit slider.
        private static readonly Stopwatch _moveClock = Stopwatch.StartNew();
        private static double _lastMoveSeconds = -1.0;

        // The framerate at which a given Sensitivity value behaves exactly as it always did. Away
        // from it the per-tick fraction is re-derived from elapsed time instead of being applied raw.
        private const double ReferenceFrameSeconds = 1.0 / 144.0;

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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

        // Only inject clicks while a window OTHER than our own UI has focus. Without this, holding the
        // Auto Trigger keybind (default Right) anywhere -- desktop, browser, the Aimmy window itself
        // -- injected synthetic LEFT clicks into whatever happened to be under the cursor.
        private static bool IsExternalWindowFocused()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;

            GetWindowThreadProcessId(foreground, out uint pid);
            return pid != 0 && pid != (uint)Environment.ProcessId;
        }

        public static async Task DoTriggerClick(RectangleF? detectionBox = null)
        {
            // Gate by Auto Trigger Keybind / Constant AI Shooting (no longer tied to Aim Keybind).
            if (!(Dictionary.toggleState["Constant AI Shooting"]
                  || InputBindingManager.IsHoldingBinding("Auto Trigger Keybind"))
                || !IsExternalWindowFocused())
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

                HoldMouseButton();
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

            LastClickTime = DateTime.UtcNow;
            await EmitClickAsync(clickDelayMilliseconds);
        }

        // Single synthetic click for Rapid Fire.
        public static async Task DoRapidFireClick(int clickHoldMilliseconds = 10)
        {
            await EmitClickAsync(clickHoldMilliseconds);
        }

        // Serializes Auto Trigger and Rapid Fire so their down/up pairs cannot interleave, captures
        // ONE backend for the whole pair so the up can never be sent through a different driver than
        // the down, registers the echo so the global hook ignores our own click, and releases in a
        // finally so an exception between down and up cannot strand the button held.
        private static readonly SemaphoreSlim _clickGate = new(1, 1);

        private static async Task EmitClickAsync(int holdMilliseconds)
        {
            if (!await _clickGate.WaitAsync(250)) return;

            try
            {
                var (mouseDown, mouseUp) = GetMouseActions();
                InputBindingManager.RegisterInjectedClick("Left");

                try
                {
                    mouseDown.Invoke();
                    await Task.Delay(holdMilliseconds);
                }
                finally
                {
                    try { mouseUp.Invoke(); } catch { /* never leave the button down */ }
                }
            }
            finally
            {
                _clickGate.Release();
            }
        }

        #region Spray Mode Methods

        // The exact "up" that pairs with the "down" we actually sent, captured at hold time.
        private static Action? _sprayReleaseAction;

        // Deadman switch. The button may only stay held while something keeps refreshing the hold.
        // Previously the ONLY release paths lived inside the AI loop's AutoTrigger(), so losing the
        // target, releasing the key, or pressing Emergency Stop could each leave the physical mouse
        // button pressed with no code path left running to release it.
        private static long _sprayRefreshTicks;
        private static readonly System.Threading.Timer _sprayWatchdog =
            new(_ => SprayWatchdogTick(), null, 200, 200);

        private static void SprayWatchdogTick()
        {
            if (!isSpraying) return;

            long idleMs = (DateTime.UtcNow.Ticks - Interlocked.Read(ref _sprayRefreshTicks))
                          / TimeSpan.TicksPerMillisecond;
            if (idleMs > 300)
            {
                ReleaseMouseButton();
            }
        }

        public static void HoldMouseButton()
        {
            lock (_buttonLock)
            {
                Interlocked.Exchange(ref _sprayRefreshTicks, DateTime.UtcNow.Ticks);
                if (isSpraying) return;

                var (mouseDown, mouseUp) = GetMouseActions();
                try
                {
                    mouseDown.Invoke();
                    _sprayReleaseAction = mouseUp;
                    isSpraying = true;
                }
                catch
                {
                    _sprayReleaseAction = null;
                }
            }
        }

        public static void ReleaseMouseButton()
        {
            lock (_buttonLock)
            {
                if (!isSpraying) return;

                try { (_sprayReleaseAction ?? GetMouseActions().up).Invoke(); }
                catch { /* fall through: never stay stuck believing we still hold the button */ }
                finally
                {
                    _sprayReleaseAction = null;
                    isSpraying = false;
                }
            }
        }

        public static void ResetSprayState()
        {
            if (isSpraying)
            {
                ReleaseMouseButton();
            }
        }
        #endregion

        // Mouse Curve multiplier applied to the per-tick aim movement.
        private static double GetMouseCurveFactor()
        {
            if (Dictionary.dropdownState.TryGetValue("Mouse Curve", out var v))
            {
                switch (v?.ToString())
                {
                    case "Smooth/Legit": return 0.6;
                    case "Aggressive": return 1.6;
                    case "Linear": return 1.0;
                }
            }
            return 1.0;
        }

        public static void MoveCrosshair(int detectedX, int detectedY)
        {
            // Aim Strength: stabilize the TARGET before we aim at it, instead of touching the movement.
            // Detection boxes wobble a few pixels every frame; that wobble is exactly what turns a fast
            // (low-Sensitivity) aim into jitter. Low-pass the target so the crosshair locks onto a steady
            // point -- so you can push Sensitivity fast for an "instant" feel and this cancels the jitter
            // that speed would normally cause. Speed/feel stay owned by Sensitivity / Mouse Curve /
            // Movement Path (they still fully apply). 0 = off (raw target, original behavior).
            // Elapsed time for this tick, clamped so a frame hitch (or the first move after idling)
            // cannot turn into one enormous step. Computed first because Aim Strength needs it too.
            double nowSeconds = _moveClock.Elapsed.TotalSeconds;
            double dtSeconds = _lastMoveSeconds < 0 ? ReferenceFrameSeconds : nowSeconds - _lastMoveSeconds;
            _lastMoveSeconds = nowSeconds;
            dtSeconds = Math.Clamp(dtSeconds, 0.0005, 0.05);

            double aimStrength = AimSettings.AimStrength;
            if (aimStrength > 0)
            {
                if (!_hasSmoothedTarget)
                {
                    _smoothedTargetX = detectedX;
                    _smoothedTargetY = detectedY;
                    _hasSmoothedTarget = true;
                }
                else
                {
                    double jump = Math.Sqrt(
                        Math.Pow(detectedX - _smoothedTargetX, 2) +
                        Math.Pow(detectedY - _smoothedTargetY, 2));

                    // Scale the "this is a different target" radius with the display instead of
                    // hardcoding 200px, which was far too tight at 1440p and 4K.
                    double reacquireRadius = Math.Max(120.0, ScreenHeight * 0.18);

                    if (jump > reacquireRadius)
                    {
                        // Big jump = a new/different target: snap the filter straight there so it does
                        // not crawl across the screen from the last target's position.
                        _smoothedTargetX = detectedX;
                        _smoothedTargetY = detectedY;
                    }
                    else
                    {
                        // alpha = how much of the raw detection is folded in. Higher strength -> lower
                        // alpha -> heavier smoothing -> steadier lock (slightly more lag). Time-adjusted
                        // so the same slider value smooths identically at 60 and 240 FPS.
                        double perFrameAlpha = 1.0 - aimStrength * 0.85; // 1.0 -> 0.15, 0.5 -> 0.575
                        double alpha = TimeAdjustedFraction(perFrameAlpha, dtSeconds);
                        _smoothedTargetX += (detectedX - _smoothedTargetX) * alpha;
                        _smoothedTargetY += (detectedY - _smoothedTargetY) * alpha;
                    }
                }

                detectedX = (int)Math.Round(_smoothedTargetX);
                detectedY = (int)Math.Round(_smoothedTargetY);
            }
            else
            {
                _hasSmoothedTarget = false;
            }

            // Offset from the crosshair, in real screen pixels on BOTH axes.
            double targetX = detectedX - CrosshairX;
            double targetY = detectedY - CrosshairY;

            double sensitivity = Convert.ToDouble(Dictionary.sliderSettings["Mouse Sensitivity (+/-)"]);
            double perFrameFraction = Math.Clamp(1.0 - sensitivity, 0.0, 1.0);
            double t = TimeAdjustedFraction(perFrameFraction, dtSeconds);

            PointF start = new(0f, 0f);
            PointF end = new((float)targetX, (float)targetY);
            PointF newPosition;

            switch (Dictionary.dropdownState["Movement Path"])
            {
                // "Curve Strength" bows the control points perpendicular to the line. Default 0 keeps
                // them collinear -- and collinear thirds are exactly a straight line, which is why
                // this option is indistinguishable from Lerp until Curve Strength is raised.
                case "Cubic Bezier":
                    {
                        PointF control1 = new(start.X + (end.X - start.X) / 3f, start.Y + (end.Y - start.Y) / 3f);
                        PointF control2 = new(start.X + 2 * (end.X - start.X) / 3f, start.Y + 2 * (end.Y - start.Y) / 3f);
                        double curveStrength = Convert.ToDouble(Dictionary.sliderSettings["Curve Strength"]);
                        if (curveStrength != 0)
                        {
                            double perpX = -(end.Y - start.Y);
                            double perpY = end.X - start.X;
                            double perpLen = Math.Sqrt(perpX * perpX + perpY * perpY);
                            if (perpLen > 0)
                            {
                                perpX /= perpLen;
                                perpY /= perpLen;
                                control1 = new PointF(control1.X + (float)(perpX * curveStrength), control1.Y + (float)(perpY * curveStrength));
                                control2 = new PointF(control2.X + (float)(perpX * curveStrength), control2.Y + (float)(perpY * curveStrength));
                            }
                        }
                        newPosition = MovementPaths.CubicBezier(start, end, control1, control2, t);
                        break;
                    }
                case "Smoothstep":
                    newPosition = MovementPaths.Smoothstep(start, end, t);
                    break;
                case "Exponential":
                    // No -0.2 offset any more. That skew pushed t above 1.0 for any Sensitivity below
                    // 0.2, so the path commanded more than the remaining distance and oscillated.
                    newPosition = MovementPaths.Exponential(start, end, t, Convert.ToDouble(Dictionary.sliderSettings["Exponent Strength"]));
                    break;
                case "Adaptive":
                    newPosition = MovementPaths.Adaptive(start, end, t, Convert.ToDouble(Dictionary.sliderSettings["Adaptation Strength"]));
                    break;
                case "Perlin Noise":
                    newPosition = MovementPaths.PerlinNoise(start, end, t, Convert.ToDouble(Dictionary.sliderSettings["Noise Level"]), 0.5);
                    break;
                // "None" and "Straight" are the same straight-line interpolation. Both are kept so
                // existing saved configs keep loading.
                case "None":
                case "Straight":
                default:
                    newPosition = MovementPaths.Lerp(start, end, t);
                    break;
            }

            double moveX = newPosition.X;
            double moveY = newPosition.Y;

            // EMA smooths the raw path output against the PREVIOUS RAW path output. previousX/Y used
            // to be assigned the fully processed, post-jitter, post-aspect value, so the filter was
            // recycling its own random noise and its Y state was on a different scale to its input.
            if (IsEMASmoothingEnabled)
            {
                double emaAlpha = TimeAdjustedFraction(Math.Clamp(smoothingFactor, 0.0, 1.0), dtSeconds);
                moveX = EmaSmoothing(previousX, moveX, emaAlpha);
                moveY = EmaSmoothing(previousY, moveY, emaAlpha);
            }

            previousX = moveX;
            previousY = moveY;

            // Mouse Curve response: scale the per-tick movement (Smooth/Legit slower, Aggressive faster).
            double mouseCurveFactor = GetMouseCurveFactor();
            moveX *= mouseCurveFactor;
            moveY *= mouseCurveFactor;

            // Per-tick speed limit. There is deliberately no aspect-ratio division here: the target
            // offset is now real screen pixels on both axes, so scaling Y by the aspect ratio would
            // just make vertical aim 1.78x slower than horizontal for no reason. It only ever looked
            // right because it happened to cancel the old ScreenHeight/IMAGE_SIZE scaling.
            moveX = Math.Clamp(moveX, -150.0, 150.0);
            moveY = Math.Clamp(moveY, -150.0, 150.0);

            // Zero-mean jitter. Random.Next(-J, J) excludes its upper bound, so the old jitter
            // averaged -0.5px per axis per tick -- a constant drift up and to the left. Random.Shared
            // is also safe to touch from the pool threads this runs on; a shared Random was not.
            double jitterAmount = Convert.ToDouble(Dictionary.sliderSettings["Mouse Jitter"]);
            if (jitterAmount > 0)
            {
                moveX += (Random.Shared.NextDouble() * 2.0 - 1.0) * jitterAmount;
                moveY += (Random.Shared.NextDouble() * 2.0 - 1.0) * jitterAmount;
            }

            // Carry the sub-pixel remainder into the next tick instead of truncating it away.
            _aimResidualX += moveX;
            _aimResidualY += moveY;
            int sendX = (int)Math.Truncate(_aimResidualX);
            int sendY = (int)Math.Truncate(_aimResidualY);
            _aimResidualX -= sendX;
            _aimResidualY -= sendY;

            if (sendX != 0 || sendY != 0)
            {
                SendMouseMove(sendX, sendY);
            }

            if (!Dictionary.toggleState["Auto Trigger"])
            {
                ResetSprayState();
            }
        }

        // Converts a "fraction per reference frame" into the equivalent fraction for the time that
        // actually elapsed, so a given setting converges at the same RATE at any framerate.
        private static double TimeAdjustedFraction(double perFrameFraction, double dtSeconds)
        {
            if (perFrameFraction <= 0.0) return 0.0;
            if (perFrameFraction >= 1.0) return 1.0;
            return 1.0 - Math.Pow(1.0 - perFrameFraction, dtSeconds / ReferenceFrameSeconds);
        }

        // Single place that talks to the selected backend, shared by aim and anti-recoil so the two
        // can never disagree about units or clamping.
        private static void SendMouseMove(int dx, int dy)
        {
            switch (Dictionary.dropdownState["Mouse Movement Method"])
            {
                case "SendInput":
                    SendInputMouse.SendMouseCommand(MOUSEEVENTF_MOVE, dx, dy);
                    break;

                case "LG HUB":
                    LGMouse.Move(0, dx, dy, 0);
                    break;

                case "Razer Synapse (Require Razer Peripheral)":
                    RZMouse.mouse_move(dx, dy, true);
                    break;

                case "ddxoft Virtual Input Driver":
                    DdxoftMain.ddxoftInstance.movR!(dx, dy);
                    break;

                default:
                    mouse_event(MOUSEEVENTF_MOVE,
                        unchecked((uint)dx),
                        unchecked((uint)dy),
                        0, 0);
                    break;
            }
        }

        internal static void ResetAimState()
        {
            _aimResidualX = 0;
            _aimResidualY = 0;
            _hasSmoothedTarget = false;
            previousX = 0;
            previousY = 0;
            _lastMoveSeconds = -1.0;
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

            // Anti Recoil Timeout: stop compensating on an axis once it has been recoiling for
            // longer than the per-axis timeout (in seconds). 0 = no timeout (compensate forever).
            if (Dictionary.toggleState.TryGetValue("Anti Recoil Timeout", out var timeoutObj)
                && timeoutObj is true)
            {
                double timeoutY = Dictionary.sliderSettings.TryGetValue("Timeout Y", out var ty)
                    ? Convert.ToDouble(ty) : 0.0;
                double timeoutX = Dictionary.sliderSettings.TryGetValue("Timeout X", out var tx)
                    ? Convert.ToDouble(tx) : 0.0;

                if (timeoutY > 0 && sustainedAfterDelaySec >= timeoutY) yPerSecond = 0;
                if (timeoutX > 0 && sustainedAfterDelaySec >= timeoutX) xPerSecond = 0;
            }

            AccumulateAntiRecoilPixels(xPerSecond, yPerSecond, dtSeconds, out int xRecoil, out int yRecoil);

            if (xRecoil == 0 && yRecoil == 0) return;

            // Same emit path as aim, so the two subsystems cannot disagree about units or clamping.
            SendMouseMove(xRecoil, yRecoil);
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