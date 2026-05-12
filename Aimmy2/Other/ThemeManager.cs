using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Visuality;

namespace Aimmy2.Theme
{
    public static class ThemeManager
    {
        // Theme changed event
        public static event EventHandler<Color> ThemeChanged;

        // Cached theme colors
        private static Color _themeColor = Color.FromRgb(114, 46, 209);
        private static Color _themeColorDark;
        private static Color _themeColorLight;
        private static Color _themeGradientDark;
        private static Color _themeColorTransparent;
        private static Color _themeColorSemiTransparent;

        // Cache of themed elements for performance
        private static readonly Dictionary<WeakReference, List<ThemeElementInfo>> _themedElements = new Dictionary<WeakReference, List<ThemeElementInfo>>();
        private static readonly DispatcherTimer _cleanupTimer;

        // Theme element tags
        public const string THEME_TAG = "Theme";
        public const string THEME_DARK_TAG = "ThemeDark";
        public const string THEME_LIGHT_TAG = "ThemeLight";
        public const string THEME_GRADIENT_TAG = "ThemeGradient";
        public const string THEME_TRANSPARENT_TAG = "ThemeTransparent";
        public const string THEME_SEMI_TRANSPARENT_TAG = "ThemeSemiTransparent";

        static ThemeManager()
        {
            // Initialize colors
            CalculateThemeColors(_themeColor);

            // Setup cleanup timer for weak references
            _cleanupTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _cleanupTimer.Tick += CleanupDeadReferences;
            _cleanupTimer.Start();

            // Initialize dynamic resources when application starts
            if (Application.Current != null)
            {
                Application.Current.Activated += (s, e) =>
                {
                    // Update resources on first activation
                    UpdateDynamicResources();
                    UpdateMainWindowGradients();
                };
            }
        }

        public static Color ThemeColor => _themeColor;
        public static Color ThemeColorDark => _themeColorDark;
        public static Color ThemeColorLight => _themeColorLight;
        public static Color ThemeGradientDark => _themeGradientDark;

        /// <summary>
        /// Sets the base theme color and updates all registered elements
        /// </summary>
        public static void SetThemeColor(Color baseColor)
        {
            if (_themeColor == baseColor) return;

            _themeColor = baseColor;
            CalculateThemeColors(baseColor);
            UpdateAllThemedElements();

            // Update MainWindow gradients
            UpdateMainWindowGradients();

            // Update dynamic resources
            UpdateDynamicResources();

            // Raise theme changed event
            ThemeChanged?.Invoke(null, baseColor);
        }

        /// <summary>
        /// Sets the theme color from hex string
        /// </summary>
        public static void SetThemeColor(string hexColor)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                SetThemeColor(color);
            }
            catch (Exception ex)
            {
                // Log error or use default color
            }
        }

        /// <summary>
        /// Registers an element to be themed
        /// </summary>
        public static void RegisterElement(FrameworkElement element)
        {
            if (element == null) return;

            var weakRef = new WeakReference(element);
            var elementInfoList = new List<ThemeElementInfo>();

            // Check for Theme attached property
            string themeValue = ThemeBehavior.GetTheme(element);
            if (!string.IsNullOrEmpty(themeValue))
            {
                elementInfoList.Add(new ThemeElementInfo
                {
                    PropertyPath = GetPropertyPathFromTag(themeValue),
                    ThemeType = GetThemeTypeFromTag(themeValue)
                });
            }
            // Check element's tag
            else if (element.Tag is string tag && tag.StartsWith("Theme"))
            {
                elementInfoList.Add(new ThemeElementInfo
                {
                    PropertyPath = GetPropertyPathFromTag(tag),
                    ThemeType = GetThemeTypeFromTag(tag)
                });
            }

            // Check for themed children
            FindThemedChildren(element, elementInfoList);

            if (elementInfoList.Count > 0)
            {
                _themedElements[weakRef] = elementInfoList;

                // Apply theme immediately
                ApplyThemeToElement(element, elementInfoList);
            }

            // If this is the MainWindow, update gradients and resources
            if (element == Application.Current?.MainWindow)
            {
                UpdateDynamicResources();
                UpdateMainWindowGradients();
            }
        }

        /// <summary>
        /// Unregisters an element from theming
        /// </summary>
        public static void UnregisterElement(FrameworkElement element)
        {
            var toRemove = _themedElements.Keys
                .Where(wr => wr.IsAlive && wr.Target == element)
                .ToList();

            foreach (var wr in toRemove)
            {
                _themedElements.Remove(wr);
            }
        }

        /// <summary>
        /// Manually update all themed elements
        /// </summary>
        public static void UpdateAllThemedElements()
        {
            var deadRefs = new List<WeakReference>();

            foreach (var kvp in _themedElements)
            {
                if (kvp.Key.IsAlive && kvp.Key.Target is FrameworkElement element)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ApplyThemeToElement(element, kvp.Value);
                    }), DispatcherPriority.Render);
                }
                else
                {
                    deadRefs.Add(kvp.Key);
                }
            }

            // Clean up dead references
            foreach (var deadRef in deadRefs)
            {
                _themedElements.Remove(deadRef);
            }
        }

        /// <summary>
        /// Updates specific MainWindow gradient elements
        /// </summary>
        public static void UpdateMainWindowGradients()
        {
            if (Application.Current?.MainWindow is FrameworkElement mainWindow)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Update main border gradient theme stop (dark gradient color)
                    if (mainWindow.FindName("GradientThemeStop") is GradientStop gradientStop)
                    {
                        gradientStop.Color = _themeGradientDark;
                    }

                    // Update menu highlighter gradient (base theme color)
                    if (mainWindow.FindName("HighlighterGradient1") is GradientStop highlighter1)
                    {
                        highlighter1.Color = _themeColor;
                    }

                    // Update menu highlighter gradient (transparent theme color)
                    if (mainWindow.FindName("HighlighterGradient2") is GradientStop highlighter2)
                    {
                        highlighter2.Color = _themeColorSemiTransparent;
                    }
                }), DispatcherPriority.Render);
            }
        }

        /// <summary>
        /// Updates dynamic resources in MainWindow
        /// </summary>
        private static void UpdateDynamicResources()
        {
            if (Application.Current?.MainWindow != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var resources = Application.Current.MainWindow.Resources;

                    // Update theme color resources
                    resources["ThemeColor"] = new SolidColorBrush(_themeColor);
                    resources["ThemeColorDark"] = new SolidColorBrush(_themeColorDark);
                    resources["ThemeColorLight"] = new SolidColorBrush(_themeColorLight);
                    resources["ThemeGradientDark"] = new SolidColorBrush(_themeGradientDark);
                    resources["ThemeColorTransparent"] = new SolidColorBrush(_themeColorTransparent);
                    resources["ThemeColorSemiTransparent"] = new SolidColorBrush(_themeColorSemiTransparent);
                }), DispatcherPriority.Render);
            }
        }

        /// <summary>
        /// Loads theme color from settings
        /// </summary>
        public static void LoadThemeFromSettings(string hexColor)
        {
            SetThemeColor(hexColor);
        }

        /// <summary>
        /// Gets current theme color as hex string
        /// </summary>
        public static string GetThemeColorHex()
        {
            return $"#{_themeColor.R:X2}{_themeColor.G:X2}{_themeColor.B:X2}";
        }

        #region Private Methods

        private static void CalculateThemeColors(Color baseColor)
        {
            // Dark variant - 25% darker
            _themeColorDark = DarkenColor(baseColor, 0.25);

            // Light variant - 20% lighter
            _themeColorLight = LightenColor(baseColor, 0.2);

            // Gradient dark - 70% darker (matches the original #FF120338 darkness level)
            _themeGradientDark = DarkenColor(baseColor, 0.7);

            // Transparent variants
            _themeColorTransparent = Color.FromArgb(51, baseColor.R, baseColor.G, baseColor.B); // 20% opacity
            _themeColorSemiTransparent = Color.FromArgb(102, baseColor.R, baseColor.G, baseColor.B); // 40% opacity
        }

        private static void FindThemedChildren(DependencyObject parent, List<ThemeElementInfo> elementInfoList)
        {
            if (parent == null) return;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement fe)
                {
                    // Check for Theme attached property first
                    string themeValue = ThemeBehavior.GetTheme(fe);
                    if (!string.IsNullOrEmpty(themeValue))
                    {
                        elementInfoList.Add(new ThemeElementInfo
                        {
                            Target = fe,
                            PropertyPath = GetPropertyPathFromTag(themeValue),
                            ThemeType = GetThemeTypeFromTag(themeValue)
                        });
                    }
                    // Then check regular Tag property
                    else if (fe.Tag is string tag && tag.StartsWith("Theme"))
                    {
                        elementInfoList.Add(new ThemeElementInfo
                        {
                            Target = fe,
                            PropertyPath = GetPropertyPathFromTag(tag),
                            ThemeType = GetThemeTypeFromTag(tag)
                        });
                    }
                }

                // Recurse
                FindThemedChildren(child, elementInfoList);
            }
        }

        private static void ApplyThemeToElement(FrameworkElement element, List<ThemeElementInfo> elementInfos)
        {
            foreach (var info in elementInfos)
            {
                var targetElement = info.Target ?? element;
                var color = GetColorForThemeType(info.ThemeType);
                var brush = new SolidColorBrush(color);

                try
                {
                    switch (info.PropertyPath)
                    {
                        case "Background":
                            if (targetElement is Control control)
                                control.Background = brush;
                            else if (targetElement is Border border)
                                border.Background = brush;
                            break;

                        case "BorderBrush":
                            if (targetElement is Control control2)
                                control2.BorderBrush = brush;
                            else if (targetElement is Border border2)
                                border2.BorderBrush = brush;
                            break;

                        case "Foreground":
                            if (targetElement is Control control3)
                                control3.Foreground = brush;
                            else if (targetElement is TextBlock textBlock)
                                textBlock.Foreground = brush;
                            break;

                        case "Fill":
                            if (targetElement is System.Windows.Shapes.Shape shape)
                                shape.Fill = brush;
                            break;

                        case "Stroke":
                            if (targetElement is System.Windows.Shapes.Shape shape2)
                                shape2.Stroke = brush;
                            break;

                        case "EffectBrush":
                            // For AntWpf buttons
                            var effectBrushProperty = targetElement.GetType().GetProperty("EffectBrush");
                            if (effectBrushProperty != null)
                            {
                                effectBrushProperty.SetValue(targetElement, brush);
                            }
                            break;

                        case "GradientStop":
                            // This case is now handled by UpdateMainWindowGradients for named gradient stops
                            break;
                    }
                }
                catch (Exception ex)
                {
                }
            }
        }

        private static string GetPropertyPathFromTag(string tag)
        {
            // Extract property from tag format: "Theme:Background" or "ThemeDark:BorderBrush"
            var parts = tag.Split(':');
            return parts.Length > 1 ? parts[1] : "Background";
        }

        private static ThemeType GetThemeTypeFromTag(string tag)
        {
            var parts = tag.Split(':');
            var themeTag = parts[0];

            return themeTag switch
            {
                THEME_TAG => ThemeType.Base,
                THEME_DARK_TAG => ThemeType.Dark,
                THEME_LIGHT_TAG => ThemeType.Light,
                THEME_GRADIENT_TAG => ThemeType.Gradient,
                THEME_TRANSPARENT_TAG => ThemeType.Transparent,
                THEME_SEMI_TRANSPARENT_TAG => ThemeType.SemiTransparent,
                _ => ThemeType.Base
            };
        }

        private static Color GetColorForThemeType(ThemeType type)
        {
            return type switch
            {
                ThemeType.Base => _themeColor,
                ThemeType.Dark => _themeColorDark,
                ThemeType.Light => _themeColorLight,
                ThemeType.Gradient => _themeGradientDark,
                ThemeType.Transparent => _themeColorTransparent,
                ThemeType.SemiTransparent => _themeColorSemiTransparent,
                _ => _themeColor
            };
        }

        private static Color DarkenColor(Color color, double factor)
        {
            return Color.FromRgb(
                (byte)(color.R * (1 - factor)),
                (byte)(color.G * (1 - factor)),
                (byte)(color.B * (1 - factor))
            );
        }

        private static Color LightenColor(Color color, double factor)
        {
            return Color.FromRgb(
                (byte)Math.Min(255, color.R + (255 - color.R) * factor),
                (byte)Math.Min(255, color.G + (255 - color.G) * factor),
                (byte)Math.Min(255, color.B + (255 - color.B) * factor)
            );
        }

        private static void CleanupDeadReferences(object sender, EventArgs e)
        {
            var deadRefs = _themedElements.Keys
                .Where(wr => !wr.IsAlive)
                .ToList();

            foreach (var deadRef in deadRefs)
            {
                _themedElements.Remove(deadRef);
            }
        }

        internal static void ExcludeWindowFromBackground(FOV fOV)
        {
            // Keep compatibility with callers in this branch.
            // Background exclusion pipeline is not implemented here, so no-op.
            _ = fOV;
        }

        #endregion

        #region Helper Classes

        private class ThemeElementInfo
        {
            public FrameworkElement Target { get; set; }
            public string PropertyPath { get; set; }
            public ThemeType ThemeType { get; set; }
        }

        private enum ThemeType
        {
            Base,
            Dark,
            Light,
            Gradient,
            Transparent,
            SemiTransparent
        }

        #endregion
    }

    /// <summary>
    /// Attached behavior for automatic theme registration
    /// </summary>
    public static class ThemeBehavior
    {
        public static readonly DependencyProperty AutoRegisterProperty =
            DependencyProperty.RegisterAttached(
                "AutoRegister",
                typeof(bool),
                typeof(ThemeBehavior),
                new PropertyMetadata(false, OnAutoRegisterChanged));

        public static readonly DependencyProperty ThemeProperty =
            DependencyProperty.RegisterAttached(
                "Theme",
                typeof(string),
                typeof(ThemeBehavior),
                new PropertyMetadata(null, OnThemeChanged));

        public static bool GetAutoRegister(DependencyObject obj)
        {
            return (bool)obj.GetValue(AutoRegisterProperty);
        }

        public static void SetAutoRegister(DependencyObject obj, bool value)
        {
            obj.SetValue(AutoRegisterProperty, value);
        }

        public static string GetTheme(DependencyObject obj)
        {
            return (string)obj.GetValue(ThemeProperty);
        }

        public static void SetTheme(DependencyObject obj, string value)
        {
            obj.SetValue(ThemeProperty, value);
        }

        private static void OnAutoRegisterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && (bool)e.NewValue)
            {
                if (element.IsLoaded)
                {
                    ThemeManager.RegisterElement(element);
                }
                else
                {
                    element.Loaded += (s, args) => ThemeManager.RegisterElement(element);
                }

                element.Unloaded += (s, args) => ThemeManager.UnregisterElement(element);
            }
        }

        private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && e.NewValue is string themeValue && !string.IsNullOrEmpty(themeValue))
            {
                // Auto-register elements that have a Theme attached property
                if (element.IsLoaded)
                {
                    ThemeManager.RegisterElement(element);
                }
                else
                {
                    element.Loaded += (s, args) => ThemeManager.RegisterElement(element);
                }

                element.Unloaded += (s, args) => ThemeManager.UnregisterElement(element);
            }
        }
    }
}