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
            { "Disable Anti Recoil Keybind", "End"},
            { "Gun 1 Key", "D1"},
            { "Gun 2 Key", "D2"},
            { "Gun 3 Key", "D3"},
        };

        public static Dictionary<string, dynamic> sliderSettings = new()
        {
            { "Suggested Model", ""},
            { "FOV Size", 640 },
            { "Dynamic FOV Size", 280 },
            { "Mouse Sensitivity (+/-)", 0.90 },
            { "Mouse Jitter", 4 },
            { "Sticky Aim Threshold", 50 },
            { "Y Offset (Up/Down)", 0 },
            { "Y Offset (%)", 50 },
            { "X Offset (Left/Right)", 0 },
            { "X Offset (%)", 50 },
            { "EMA Smoothening", 0.5},
            { "Kalman Lead Time", 0.15 },
            { "WiseTheFox Lead Time", 0.15 },
            { "Shalloe Lead Multiplier", 4.0 },
            { "Auto Trigger Delay", 0.1 },
            { "AI Minimum Confidence", 30 },
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
            { "Sticky Aim", false },
            { "Constant AI Tracking", false },
            { "Constant AI Shooting", false },
            { "Predictions", false },
            { "EMA Smoothening", false },
            { "Enable Model Switch Keybind", true },
            { "Enable Gun Switching Keybind", false },
            { "Auto Trigger", false },
            { "Anti Recoil", false },
            { "Adaptive Recoil", false },
            { "FOV", false },
            { "Dynamic FOV", false },
            { "Third Person Support", false },
            { "Masking", false },
            { "Show Detected Player", false },
            { "Cursor Check", false },
            { "Spray Mode", false },
            { "Show AI Confidence", false },
            { "Show Tracers", false },
            { "Collect Data While Playing", false },
            { "Auto Label Data", false },
            { "LG HUB Mouse Movement", false },
            { "Mouse Background Effect", true },
            { "UI TopMost", false },
            { "Debug Mode", false },
            { "StreamGuard", false },
            { "X Axis Percentage Adjustment", false },
            { "Y Axis Percentage Adjustment", false }
        };

        public static Dictionary<string, dynamic> minimizeState = new()
        {
            { "Aim Assist", false },
            { "Aim Config", false },
            { "Predictions", false },
            { "Auto Trigger", false },
            { "Anti Recoil", false},
            { "Anti Recoil Config", false },
            { "FOV Config", false },
            { "ESP Config", false },
            { "Settings Menu", false },
            { "Model Settings", false },
            { "X/Y Percentage Adjustment", false },
            { "Theme Settings", false },
            { "Screen Settings", false}
        };

        public static Dictionary<string, dynamic> dropdownState = new()
        {
            { "Prediction Method", "Kalman Filter" },
            { "Detection Area Type", "Closest to Center Screen" },
            { "Aiming Boundaries Alignment", "Center" },
            { "Target Priority", "Best Confidence" },
            { "Target Class", "Smart Detection" },
            { "Movement Path", "Cubic Bezier" },
            { "Mouse Movement Method", "Mouse Event" },
            { "Screen Capture Method", "DirectX" },
            { "Tracer Position", "Top" }

        };

        public static Dictionary<string, dynamic> colorState = new()
        {
            { "FOV Color", "#FF8080FF"},
            { "Detected Player Color", "#FF00FFFF"},
            { "Theme Color", "#FF722ED1" }
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

        public static Dictionary<string, dynamic> filelocationState = new()
        {
            { "ddxoft DLL Location", ""},
            { "Gun 1 Config", "" },
            { "Gun 2 Config", "" },
            { "Gun 3 Config", "" }
        };
    }
}