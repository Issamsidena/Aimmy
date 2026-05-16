using Class;
using Newtonsoft.Json;
using System.Globalization;
using System.IO;

namespace Aimmy2.Class
{
    /// <summary>
    /// Canonical shapes for main (aim) config and anti-recoil config JSON when saving.
    /// </summary>
    internal static class ConfigPersistence
    {
        private static readonly string[] MainSliderOnlyKeys =
        {
            "Suggested Model",
            "FOV Size",
            "Dynamic FOV Size",
            "Mouse Sensitivity (+/-)",
            "Mouse Jitter",
            "Sticky Aim Threshold",
            "Y Offset (Up/Down)",
            "Y Offset (%)",
            "X Offset (Left/Right)",
            "X Offset (%)",
            "EMA Smoothening",
            "Kalman Lead Time",
            "WiseTheFox Lead Time",
            "Shalloe Lead Multiplier",
            "Auto Trigger Delay",
            "AI Minimum Confidence",
            "AI Confidence Font Size",
            "Corner Radius",
            "Border Thickness",
            "Opacity"
        };

        private static readonly string[] MainRecoilMirrorKeys =
        {
            "Y Recoil (Up/Down)",
            "X Recoil (Left/Right)",
            "Drift Compensation X (Left/Right)",
            "Drift Compensation Y (Up/Down)"
        };

        private static readonly string[] KeysToStripFromSliderSettings =
        {
            "Sticky Aim",
            "Y Recoil (Up/Down)",
            "X Recoil (Left/Right)",
            "Drift Compensation X (Left/Right)",
            "Drift Compensation Y (Up/Down)"
        };

        private static readonly string[] AntiRecoilSaveKeyOrder =
        {
            "Hold Time",
            "Fire Rate",
            "Y Recoil (Up/Down)",
            "X Recoil (Left/Right)",
            "Adaptive Recoil",
            "Drift Compensation X (Left/Right)",
            "Drift Compensation X Speed",
            "Drift Compensation Y (Up/Down)",
            "Drift Compensation Y Speed",
            "Spray Fade X",
            "Spray Fade Y",
            "Spray Fade X Speed",
            "Spray Fade Y Speed"
        };

        private static readonly Dictionary<string, dynamic> AntiRecoilDefaults = new()
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

        /// <summary>
        /// Clears merged state from a previously loaded profile so each .cfg load is authoritative.
        /// </summary>
        public static void ResetAntiRecoilSettingsToDefaults()
        {
            Dictionary.AntiRecoilSettings.Clear();
            foreach (var (key, value) in AntiRecoilDefaults)
                Dictionary.AntiRecoilSettings[key] = value;
        }

        public static bool ReadAdaptiveRecoilFromSettings()
        {
            if (!Dictionary.AntiRecoilSettings.TryGetValue("Adaptive Recoil", out var raw))
                return false;
            return CoerceBool(raw);
        }

        public static Dictionary<string, dynamic> BuildMainSliderConfigForSave(
            string suggestedModelBaseName,
            string extraStrings)
        {
            var s = Dictionary.sliderSettings;
            var ar = Dictionary.AntiRecoilSettings;
            var t = Dictionary.toggleState;

            string suggested = CoerceString(GetOr(s, "Suggested Model", ""));
            if (!string.IsNullOrWhiteSpace(suggestedModelBaseName) && s.ContainsKey("Suggested Model"))
                suggested = suggestedModelBaseName + ".onnx" + extraStrings;

            return new Dictionary<string, dynamic>
            {
                ["Suggested Model"] = suggested,
                ["FOV Size"] = CoerceDouble(GetOr(s, "FOV Size", 640)),
                ["Dynamic FOV Size"] = CoerceDouble(GetOr(s, "Dynamic FOV Size", 280)),
                ["Mouse Sensitivity (+/-)"] = CoerceDouble(GetOr(s, "Mouse Sensitivity (+/-)", 0.9)),
                ["Mouse Jitter"] = CoerceDouble(GetOr(s, "Mouse Jitter", 4)),
                ["Sticky Aim"] = CoerceBool(GetOr(t, "Sticky Aim", false)),
                ["Sticky Aim Threshold"] = CoerceDouble(GetOr(s, "Sticky Aim Threshold", 50)),
                ["Y Offset (Up/Down)"] = CoerceDouble(GetOr(s, "Y Offset (Up/Down)", 0)),
                ["Y Offset (%)"] = CoerceDouble(GetOr(s, "Y Offset (%)", 50)),
                ["X Offset (Left/Right)"] = CoerceDouble(GetOr(s, "X Offset (Left/Right)", 0)),
                ["X Offset (%)"] = CoerceDouble(GetOr(s, "X Offset (%)", 50)),
                ["EMA Smoothening"] = CoerceDouble(GetOr(s, "EMA Smoothening", 0.5)),
                ["Kalman Lead Time"] = CoerceDouble(GetOr(s, "Kalman Lead Time", 0.15)),
                ["WiseTheFox Lead Time"] = CoerceDouble(GetOr(s, "WiseTheFox Lead Time", 0.15)),
                ["Shalloe Lead Multiplier"] = CoerceDouble(GetOr(s, "Shalloe Lead Multiplier", 4.0)),
                ["Auto Trigger Delay"] = CoerceDouble(GetOr(s, "Auto Trigger Delay", 0.1)),
                ["AI Minimum Confidence"] = CoerceDouble(GetOr(s, "AI Minimum Confidence", 30)),
                ["AI Confidence Font Size"] = CoerceDouble(GetOr(s, "AI Confidence Font Size", 20)),
                ["Corner Radius"] = CoerceDouble(GetOr(s, "Corner Radius", 0)),
                ["Border Thickness"] = CoerceDouble(GetOr(s, "Border Thickness", 1)),
                ["Opacity"] = CoerceDouble(GetOr(s, "Opacity", 1)),
                ["Y Recoil (Up/Down)"] = CoerceDouble(GetOr(ar, "Y Recoil (Up/Down)", 0.0)),
                ["X Recoil (Left/Right)"] = CoerceDouble(GetOr(ar, "X Recoil (Left/Right)", 0.0)),
                ["Drift Compensation X (Left/Right)"] = CoerceDouble(GetOr(ar, "Drift Compensation X (Left/Right)", 0.0)),
                ["Drift Compensation Y (Up/Down)"] = CoerceDouble(GetOr(ar, "Drift Compensation Y (Up/Down)", 0.0))
            };
        }

        public static Dictionary<string, dynamic> BuildAntiRecoilConfigForSave()
        {
            Dictionary.AntiRecoilSettings["Adaptive Recoil"] = Dictionary.toggleState["Adaptive Recoil"];

            var ar = Dictionary.AntiRecoilSettings;
            var ordered = new Dictionary<string, dynamic>();
            foreach (var key in AntiRecoilSaveKeyOrder)
            {
                if (!ar.TryGetValue(key, out var v))
                    continue;
                ordered[key] = v;
            }

            foreach (var kvp in ar)
            {
                if (!ordered.ContainsKey(kvp.Key))
                    ordered[kvp.Key] = kvp.Value;
            }

            return ordered;
        }

        public static void LoadMainSliderConfig(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            if (!File.Exists(path))
            {
                SaveDictionary.WriteJSON(ConfigPersistence.BuildMainSliderConfigForSave("", ""), path);
                return;
            }

            var raw = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(File.ReadAllText(path));
            if (raw == null)
                return;

            foreach (var k in KeysToStripFromSliderSettings)
            {
                if (Dictionary.sliderSettings.ContainsKey(k))
                    Dictionary.sliderSettings.Remove(k);
            }

            foreach (var key in MainSliderOnlyKeys)
            {
                if (!raw.TryGetValue(key, out var value))
                    continue;
                if (!Dictionary.sliderSettings.ContainsKey(key))
                    Dictionary.sliderSettings.Add(key, value);
                else
                    Dictionary.sliderSettings[key] = value;
            }

            if (raw.TryGetValue("Sticky Aim", out var sticky))
                Dictionary.toggleState["Sticky Aim"] = CoerceBool(sticky);

            foreach (var key in MainRecoilMirrorKeys)
            {
                if (!raw.TryGetValue(key, out var value))
                    continue;
                if (!Dictionary.AntiRecoilSettings.ContainsKey(key))
                    Dictionary.AntiRecoilSettings.Add(key, CoerceDouble(value));
                else
                    Dictionary.AntiRecoilSettings[key] = CoerceDouble(value);
            }
        }

        private static dynamic GetOr(Dictionary<string, dynamic> d, string key, dynamic fallback) =>
            d.TryGetValue(key, out var v) ? v : fallback;

        private static double CoerceDouble(dynamic v) => Convert.ToDouble(v, CultureInfo.InvariantCulture);

        private static bool CoerceBool(dynamic v) => Convert.ToBoolean(v);

        private static string CoerceString(dynamic v) => v?.ToString() ?? "";
    }
}
