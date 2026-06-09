using Visuality;

namespace Aimmy2.Class
{
    public static class Dictionary
    {
        public static string lastLoadedModel = "N/A";
        public static string lastLoadedConfig = "N/A";
        public static DetectedPlayerWindow? DetectedPlayerOverlay;
        public static FOV? FOVWindow;

        public static Dictionary<string, dynamic> bindingSettings = new()
        {
            { "Aim Keybind", "Right"},
            { "Second Aim Keybind", "LMenu"},
            { "Auto Trigger Keybind", "Right"},
            { "Dynamic FOV Keybind", "Left"},
            { "Emergency Stop Keybind", "Delete"},
            { "Model Switch Keybind", "OemPipe"},
            { "Anti Recoil Keybind", "Left"},
            { "Enable/Disable Anti Recoil Keybind", "End"},
            { "Rapid Fire Keybind", "Left"},
            { "Gun 1 Key", "D1"},
            { "Gun 2 Key", "D2"},
            { "Gun 3 Key", "D3"}
        };

        public static Dictionary<string, dynamic> sliderSettings = new()
        {
            { "Suggested Model", ""},
            { "FOV Size", 640 },
            { "Dynamic FOV Size", 200 },
            { "Mouse Sensitivity (+/-)", 0.80 },
            { "Mouse Jitter", 4 },
            // Movement Path per-curve tuning. Defaults reproduce the old hardcoded behavior:
            { "Curve Strength", 0.0 },        // Cubic Bezier: 0 = collinear control points (original straight bow)
            { "Exponent Strength", 3.0 },     // Exponential: was MovementPaths.Exponential(..., 3.0)
            { "Adaptation Strength", 100.0 }, // Adaptive: was the default threshold of 100.0
            { "Noise Level", 20.0 },          // Perlin Noise: was the amplitude of 20
            // Anti Recoil Timeout: per-axis timeout in seconds (0.0 - 20.0), shown only when the toggle is on.
            { "Timeout Y", 0.0 },
            { "Timeout X", 0.0 },
            { "Sticky Aim Threshold", 50 },
            { "Approach Speed", 0.6 },
            { "Approach Threshold", 50 },
            { "Y Offset (Up/Down)", 0 },
            { "Y Offset (%)", 50 },
            { "X Offset (Left/Right)", 0 },
            { "X Offset (%)", 50 },
            { "EMA Smoothening", 0.5},
            { "Prediction Blend", 50 },
            { "Kalman Lead Time", 0.10 },
            { "Kalman Smoothness", 0.5 },
            { "WiseTheFox Lead Time", 0.15 },
            { "Shalloe Lead Multiplier", 3.0 },
            { "Auto Trigger Delay", 0.1 },
            { "Rapid Fire Delay", 50 },
            { "AI Minimum Confidence", 45 },
            { "AI Confidence Font Size", 20 },
            { "Corner Radius", 0 },
            { "Border Thickness", 1 },
            { "Opacity", 1 }
        };

        // Make sure the Settings Name is the EXACT Same as the Toggle Name or I will smack you :joeangy:
        // nori
        public static Dictionary<string, dynamic> toggleState = new()
        {
            { "Aim Assist", false },
            { "Persistent Target Lock", false },
            { "Sticky Aim", false },
            { "Snap Lock", false },
            { "Constant AI Tracking", false },
            { "Constant AI Shooting", false },
            { "Predictions", false },
            { "EMA Smoothening", false },
            { "Enable Model Switch Keybind", true },
            { "Auto Trigger", false },
            { "Rapid Fire", false },
            { "FOV", false },
            { "Dynamic FOV", false },
            { "Third Person Support", false },
            { "Masking", false },
            { "Show Detected Player", false },
            { "Cursor Check", false },
            { "Spray Mode", false },
            //{ "Only When Held", false },
            { "Anti Recoil", false },
            { "Anti Recoil Timeout", false },
            { "Adaptive Recoil", false },
            { "Enable Gun Switching Keybind", false },
            { "Show FOV", true },
            { "Show AI Confidence", false },
            { "Show Tracers", false },
            { "Collect Data While Playing", false },
            { "Auto Label Data", false },
            { "LG HUB Mouse Movement", false },
            { "Mouse Background Effect", true },
            { "Debug Mode", false },
            { "UI TopMost", false },
            //--
            { "StreamGuard", false },
            //--
            { "X Axis Percentage Adjustment", false },
            { "Y Axis Percentage Adjustment", false }
        };

        public static Dictionary<string, dynamic> minimizeState = new()
        {
            { "Aim Assist", false },
            { "Aim Config", false },
            { "Predictions", false },
            { "Auto Trigger", false },
            { "Rapid Fire", false },
            { "Anti Recoil", false },
            { "Anti Recoil Config", false },
            { "FOV Config", false },
            { "ESP Config", false },
            { "Model Settings", false },
            { "Settings Menu", false },
            { "X/Y Percentage Adjustment", false },
            { "Theme Settings", false },
            { "Screen Settings", false}
        };

        public static Dictionary<string, dynamic> dropdownState = new()
        {
            { "Prediction Method", "Kalman Filter" },
            { "Detection Area Type", "Closest to Center Screen" },
            { "Aiming Boundaries Alignment", "Center" },
            { "Mouse Movement Method", "Mouse Event" },
            { "Screen Capture Method", "DirectX" },
            { "Tracer Position", "Bottom" },
            { "Movement Path", "None" },
            { "Mouse Curve", "Linear" },
            { "Image Size", "640" },
            { "Target Class", "Smart Detection" },
            { "Target Priority", "Best Confidence" }
        };

        public static Dictionary<string, dynamic> colorState = new()
        {
            { "FOV Color", "#FF8080FF"},
            { "Detected Player Color", "#FF00FFFF"},
            { "Theme Color", "#FF722ED1" }
        };

        public static Dictionary<string, dynamic> filelocationState = new()
        {
            { "ddxoft DLL Location", ""},
            { "Gun 1 Config", "" },
            { "Gun 2 Config", "" },
            { "Gun 3 Config", "" }
        };

        public static Dictionary<string, dynamic> AntiRecoilSettings = new()
        {
            { "Hold Time", 1.0 },
            { "Fire Rate", 1.0 },
            { "Y Recoil (Up/Down)", 0.00 },
            { "X Recoil (Left/Right)", 0.00 },
            { "Adaptive Recoil", false },
            { "Drift Compensation X (Left/Right)", 0.0 },
            { "Drift Compensation X Speed", 1.0 },
            { "Drift Compensation Y (Up/Down)", 0.0 },
            { "Drift Compensation Y Speed", 1.0 },
            { "Spray Fade X", 0.0 },
            { "Spray Fade Y", 0.0 },
            { "Spray Fade X Speed", 1.0 },
            { "Spray Fade Y Speed", 1.0 }
        };
    }
}