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
            // DisplayManager writes the chosen monitor here; the loader is strict, so it needs a
            // default entry or the saved selection is dropped and the display reverts to primary.
            { "SelectedDisplay", 0 },
            { "FOV Size", 640 },
            { "Dynamic FOV Size", 200 },
            { "Mouse Sensitivity (+/-)", 0.80 },
            // Aim Strength: 0-100 blend of the per-tick step toward the full target.
            // 0 = normal aim, 100 = instant lock/snap. Deterministic (adds no jitter of its own).
            { "Aim Strength", 0 },
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
            { "AI FPS Limit", 0 },
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
            { "Show Detected Player", false },
            { "Cursor Check", false },
            { "Spray Mode", false },
            //{ "Only When Held", false },
            { "Anti Recoil", false },
            { "Anti Recoil Timeout", false },
            { "Adaptive Recoil", false },
            { "Enable Gun Switching Keybind", false },
            { "Show AI Confidence", false },
            { "Show Tracers", false },
            { "Collect Data While Playing", false },
            { "Auto Label Data", false },
            { "Mouse Background Effect", true },
            { "Debug Mode", false },
            { "UI TopMost", false },
            //--
            { "StreamGuard", false },
            { "Show Screen Capture", false },
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
            { "FOV Style", "Circle" },
            { "Tracer Position", "Bottom" },
            { "Movement Path", "None" },
            { "Mouse Curve", "Linear" },
            { "Image Size", "640" },
            { "Target Class", "Smart Detection" },
            { "Target Priority", "Best Confidence" },
            // Aim Bone: which body part to lock onto within the detection box. Bone presets aim at a
            // fixed fraction of the box (recomputed each frame, so they track the part as it moves).
            // "Custom Offsets" falls back to Aiming Boundaries Alignment + the manual X/Y offsets.
            { "Aim Bone", "Custom Offsets" }
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
            // Fire Rate is in ms and 100 is neutral for the timing multiplier, so a 1.0 default
            // pinned anti recoil at its 10x ceiling. These match BindAntiRecoilSlider's fallbacks.
            { "Hold Time", 10.0 },
            { "Fire Rate", 200.0 },
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