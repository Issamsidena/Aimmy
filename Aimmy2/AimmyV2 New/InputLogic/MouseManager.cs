using Vector2.Class;
using Vector2.MouseMovementLibraries.GHubSupport;
using Class;
using MouseMovementLibraries.ddxoftSupport;
using MouseMovementLibraries.RazerSupport;
using MouseMovementLibraries.SendInputSupport;
using MouseMovementLibraries.ArduinoSupport;
using MouseMovementLibraries.MakcuSupport;
using System.Drawing;
using System.Runtime.InteropServices;
using Other;
using LogLevel = Other.LogManager.LogLevel;
using System.Windows.Forms;

namespace InputLogic
{
    internal class MouseManager
    {
        private static readonly double ScreenWidth = WinAPICaller.ScreenWidth;
        private static readonly double ScreenHeight = WinAPICaller.ScreenHeight;
        private static bool _isRapidFireActive = false;

        private static DateTime LastClickTime = DateTime.MinValue;
        private static bool isSpraying = false;
        private static ArduinoInput? _arduinoMouse = null;        
        private static ArduinoInput GetArduinoMouse()
        {
            if (_arduinoMouse == null)
            {
                _arduinoMouse = new ArduinoInput();
            }
            return _arduinoMouse;
        }

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

        private static double EmaSmoothing(double previousValue, double currentValue, double smoothingFactor) => (currentValue * smoothingFactor) + (previousValue * (1 - smoothingFactor));

        // Cleanup
        private static (Action down, Action up) GetMouseActions()
        {
            string mouseMovementMethod = Dictionary.dropdownState["Mouse Movement Method"];
            Action mouseDownAction;
            Action mouseUpAction;

            switch (mouseMovementMethod)
            {
                case "Arduino":
                    mouseDownAction = () => GetArduinoMouse().SendMouseCommand(0, 0, 1);
                    mouseUpAction = () => GetArduinoMouse().SendMouseCommand(0, 0, 0);
                    break;
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
                case "Makcu":
                    mouseDownAction = () => MakcuMain.MakcuInstance.Press(MakcuMouseButton.Left);
                    mouseUpAction = () => MakcuMain.MakcuInstance.Release(MakcuMouseButton.Left);    
                    break;
                default:
                    mouseDownAction = () => mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouseUpAction = () => mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    break;
            }

            return (mouseDownAction, mouseUpAction);
        }

        public static async Task DoRapidFire()
        {
            if (!Dictionary.toggleState["Rapid Fire"])
            {
                return;
            }
            
            double fireDelay = Dictionary.sliderSettings["Rapid Fire Delay"];
            string fireKeybind = Dictionary.bindingSettings["Rapid Fire Keybind"];
            bool isLeftClickBind = fireKeybind == "Left";

            var (mouseDown, mouseUp) = GetMouseActions();

            // For left click, we need to track expected release times
            if (isLeftClickBind)
            {
                // Make sure the binding is actually held before starting
                if (!InputBindingManager.IsHoldingBinding("Rapid Fire Keybind"))
                    return;
                    
                try
                {
                    while (InputBindingManager.IsHoldingBinding("Rapid Fire Keybind"))
                    {
                        // Simulate mouse down
                        mouseDown.Invoke();
                        
                        // Wait for the fire delay
                        await Task.Delay((int)fireDelay);
                        
                        // Mark when we expect this mouse up to happen (now)
                        // We use UtcNow for consistency
                        InputBindingManager.MarkExpectedRelease(MouseButtons.Left, DateTime.UtcNow);
                        
                        // Simulate mouse up
                        mouseUp.Invoke();
                        
                        // Wait before next cycle
                        await Task.Delay((int)fireDelay);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, $"RapidFire exception: {ex.Message}");
                }
            }
            else
            {
                // For keyboard keys, use the normal approach
                try
                {
                    while (InputBindingManager.IsHoldingBinding("Rapid Fire Keybind"))
                    {
                        mouseDown.Invoke();
                        await Task.Delay((int)fireDelay);
                        mouseUp.Invoke();
                        await Task.Delay((int)fireDelay);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, $"RapidFire exception: {ex.Message}");
                }
            }
        }
        public static async Task DoTriggerClick(RectangleF? detectionBox = null)
        {
            // Early release if no keybinds held
            if (!(InputBindingManager.IsHoldingBinding("Aim Keybind") || InputBindingManager.IsHoldingBinding("Second Aim Keybind")))
            {
                ResetSprayState();
                return;
            }

            if (Dictionary.toggleState["Spray Mode"])
            {
                if (!detectionBox.HasValue)
                {
                    if (isSpraying)
                    {
                        ReleaseMouseButton();
                    }
                    return;
                }
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
            double mouseSensitivity = Dictionary.sliderSettings["Mouse Sensitivity (+/-)"];
            int mouseJitter = (int)Dictionary.sliderSettings["Mouse Jitter"];
            string movementPath = Dictionary.dropdownState["Movement Path"];
            string mouseMovementMethod = Dictionary.dropdownState["Mouse Movement Method"];
            bool autoTrigger = Dictionary.toggleState["Auto Trigger"];
            
            bool emaEnabled = IsEMASmoothingEnabled;
            double cachedSmoothingFactor = smoothingFactor;
            double cachedPreviousX = previousX;
            double cachedPreviousY = previousY;

            int halfScreenWidth = (int)ScreenWidth / 2;
            int halfScreenHeight = (int)ScreenHeight / 2;  

            var currentMousePos = WinAPICaller.GetCursorPosition();

            int targetX = detectedX - halfScreenWidth;
            int targetY = detectedY - halfScreenHeight;

            double aspectRatioCorrection = ScreenWidth / ScreenHeight;

            int jitterX = MouseRandom.Next(-mouseJitter, mouseJitter);
            int jitterY = MouseRandom.Next(-mouseJitter, mouseJitter);

            Point start = new(0, 0);
            Point end = new(targetX, targetY);
            Point newPosition;

            switch (Dictionary.dropdownState["Movement Path"])
            {
                case "Cubic Bezier":
                    Point control1 = new(start.X + (end.X - start.X) / 3, start.Y + (end.Y - start.Y) / 3);
                    Point control2 = new(start.X + 2 * (end.X - start.X) / 3, start.Y + 2 * (end.Y - start.Y) / 3);
                    newPosition = MovementPaths.CubicBezier(start, end, control1, control2, 1 - mouseSensitivity);
                    break;
                case "Exponential":
                    newPosition = MovementPaths.Exponential(start, end, 1 - (mouseSensitivity - 0.2), 2.7);
                    break;
                case "Adaptive":
                    newPosition = MovementPaths.Adaptive(start, end, 1 - mouseSensitivity);
                    break;
                case "Smoothstep":
                    newPosition = MovementPaths.Smoothstep(start, end, 1 - mouseSensitivity);
                    break;     
                case "Perlin Noise":
                    newPosition = MovementPaths.PerlinNoise(start, end, 1 - mouseSensitivity, 20, 0.5);
                    break;
                default:
                    newPosition = MovementPaths.Lerp(start, end, 1 - mouseSensitivity);
                    break;
            }

            if (emaEnabled && cachedSmoothingFactor > 0 && cachedSmoothingFactor <= 1)
            {
                double smoothedX = EmaSmoothing(cachedPreviousX, newPosition.X, cachedSmoothingFactor);
                double smoothedY = EmaSmoothing(cachedPreviousY, newPosition.Y, cachedSmoothingFactor);
                
                if (!double.IsNaN(smoothedX) && !double.IsInfinity(smoothedX) &&
                    !double.IsNaN(smoothedY) && !double.IsInfinity(smoothedY))
                {
                    newPosition.X = (int)smoothedX;
                    newPosition.Y = (int)smoothedY;
                    
                    previousX = smoothedX;
                    previousY = smoothedY;
                }
            }
            // Clamp the movement, but use doubles
            double moveXDouble = Math.Clamp(newPosition.X, -150, 150);
            double moveYDouble = Math.Clamp(newPosition.Y, -150, 150);
            
            moveYDouble = moveYDouble * aspectRatioCorrection;

            moveXDouble += jitterX;
            moveYDouble += jitterY;

            // then round to int
            int moveX = (int)Math.Round(moveXDouble);
            int moveY = (int)Math.Round(moveYDouble);

            switch (Dictionary.dropdownState["Mouse Movement Method"])
            {
                case "Arduino":
                    GetArduinoMouse().SendMouseCommand(moveX, moveY, 0);
                    break;
                case "SendInput":
                    SendInputMouse.SendMouseCommand(MOUSEEVENTF_MOVE, moveX, moveY);
                    break;

                case "LG HUB":
                    LGMouse.Move(0, moveX, moveY, 0);
                    break;

                case "Razer Synapse (Require Razer Peripheral)":
                    RZMouse.mouse_move(moveX, moveY, true);
                    break;

                case "ddxoft Virtual Input Driver":
                    DdxoftMain.ddxoftInstance.movR!(moveX, moveY);
                    break;
                case "Makcu":
                    MakcuMain.MakcuInstance.Move(moveX, moveY);
                    break; 

                default:
                    mouse_event(MOUSEEVENTF_MOVE, (uint)moveX, (uint)moveY, 0, 0);
                    break;
            }


            if (!autoTrigger)
            {
                ResetSprayState();
            }
        }
    }
}