using Aimmy2.AILogic;
using Aimmy2.Class;
using Aimmy2.UILibrary;
using Other;
using System.Windows;
using System.Windows.Controls;
using UILibrary;
using Visuality;
using LogLevel = Other.LogManager.LogLevel;

namespace Aimmy2.Controls
{
    public partial class SettingsMenuControl : UserControl
    {
        private MainWindow? _mainWindow;
        private bool _isInitialized;

        // Local minimize state management
        private readonly Dictionary<string, bool> _localMinimizeState = new()
        {
            { "Model Settings", false },
            { "Settings Menu", false },
            { "Theme Settings", false },
            { "Screen Settings", false }
        };

        // Public properties for MainWindow access
        public StackPanel ModelSettingsPanel => ModelSettings;
        public StackPanel SettingsConfigPanel => SettingsConfig;
        public StackPanel ThemeMenuPanel => ThemeMenu;
        public StackPanel DisplaySelectMenuPanel => DisplaySelectMenu;
        public ScrollViewer SettingsMenuScrollViewer => SettingsMenu;

        public SettingsMenuControl()
        {
            InitializeComponent();
        }

        public void Initialize(MainWindow mainWindow)
        {
            if (_isInitialized) return;

            _mainWindow = mainWindow;
            _isInitialized = true;

            // Load minimize states from global dictionary if they exist
            LoadMinimizeStatesFromGlobal();

            SafeLoadSection(LoadModelSettings);
            SafeLoadSection(LoadSettingsConfig);
            SafeLoadSection(LoadThemeMenu);
            SafeLoadSection(LoadDisplaySelectMenu);

            // Apply minimize states after loading
            ApplyMinimizeStates();

            // Subscribe to display changes
            DisplayManager.DisplayChanged += OnDisplayChanged;

        }

        #region Minimize State Management

        private void LoadMinimizeStatesFromGlobal()
        {
            foreach (var key in _localMinimizeState.Keys.ToList())
            {
                if (Dictionary.minimizeState.ContainsKey(key))
                {
                    _localMinimizeState[key] = Dictionary.minimizeState[key];
                }
            }
        }

        private void SaveMinimizeStatesToGlobal()
        {
            foreach (var kvp in _localMinimizeState)
            {
                Dictionary.minimizeState[kvp.Key] = kvp.Value;
            }
        }

        private void ApplyMinimizeStates()
        {
            ApplyPanelState("Model Settings", ModelSettingsPanel);
            ApplyPanelState("Settings Menu", SettingsConfigPanel);
            ApplyPanelState("Theme Settings", ThemeMenuPanel);
            ApplyPanelState("Screen Settings", DisplaySelectMenuPanel);
        }

        private void ApplyPanelState(string stateName, StackPanel panel)
        {
            if (_localMinimizeState.TryGetValue(stateName, out bool isMinimized))
            {
                SetPanelVisibility(panel, !isMinimized);
            }
        }

        private void SetPanelVisibility(StackPanel panel, bool isVisible)
        {
            foreach (UIElement child in panel.Children)
            {
                // Keep titles, spacers, and bottom rectangles always visible
                bool shouldStayVisible = child is ATitle || child is ASpacer || child is ARectangleBottom;

                child.Visibility = shouldStayVisible
                    ? Visibility.Visible
                    : (isVisible ? Visibility.Visible : Visibility.Collapsed);
            }
        }

        private void TogglePanel(string stateName, StackPanel panel)
        {
            if (!_localMinimizeState.ContainsKey(stateName)) return;

            // Toggle the state
            _localMinimizeState[stateName] = !_localMinimizeState[stateName];

            // Apply the new visibility
            SetPanelVisibility(panel, !_localMinimizeState[stateName]);

            // Save to global dictionary
            SaveMinimizeStatesToGlobal();
        }

        #endregion

        #region Menu Section Loaders

        private void LoadModelSettings()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, ModelSettings);

            builder
                .AddTitle("Model Settings", true, t =>
                {
                    uiManager.AT_ModelSettings = t;
                    t.Minimize.Click += (s, e) => TogglePanel("Model Settings", ModelSettingsPanel);
                })
                .AddSlider("AI Minimum Confidence", "% Confidence", 1, 1, 1, 100, s =>
                {
                    uiManager.S_AIMinimumConfidence = s;
                    // Whole percentages only (e.g. 54.00 — no 54.65).
                    s.Slider.TickFrequency = 1.0;
                    s.Slider.IsSnapToTickEnabled = true;
                    var rounded = Math.Round(s.Slider.Value);
                    if (Math.Abs(s.Slider.Value - rounded) > double.Epsilon)
                        s.Slider.Value = rounded;

                    s.Slider.PreviewMouseLeftButtonUp += (sender, e) =>
                    {
                        var value = s.Slider.Value;
                        if (value >= 95)
                            LogManager.Log(LogLevel.Warning, "The minimum confidence you have set for Aimmy to be too high and may be unable to detect players.", true);
                        else if (value <= 35)
                            LogManager.Log(LogLevel.Warning, "The minimum confidence you have set for Aimmy may be too low can cause false positives.", true);
                    };
                }, tooltip: "How sure the AI must be before targeting. Higher = fewer false detections but may miss targets.")
                .AddToggle("Enable Model Switch Keybind", t => uiManager.T_EnableModelSwitchKeybind = t,
                    tooltip: "Allow switching between AI models using a hotkey.")
                .AddKeyChanger("Model Switch Keybind", k => uiManager.C_ModelSwitchKeybind = k,
                    tooltip: "Press this key to cycle through available AI models.")
                .AddKeyChanger("Emergency Stop Keybind", k => uiManager.C_EmergencyKeybind = k,
                    tooltip: "Press this key to immediately stop all aim assist functions.")
                .AddSeparator();
        }

        private void LoadSettingsConfig()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, SettingsConfig);

            builder
                .AddTitle("Settings Menu", true, t =>
                {
                    uiManager.AT_SettingsMenu = t;
                    t.Minimize.Click += (s, e) => TogglePanel("Settings Menu", SettingsConfigPanel);
                })
                .AddToggle("Collect Data While Playing", t => uiManager.T_CollectDataWhilePlaying = t,
                    tooltip: "Save screenshots of detections for training new AI models.")
                .AddToggle("Auto Label Data", t => uiManager.T_AutoLabelData = t,
                    tooltip: "Automatically label collected screenshots with detection data.")
                .AddToggle("Mouse Background Effect", t => uiManager.T_MouseBackgroundEffect = t,
                    tooltip: "Show a visual effect on the UI when moving your mouse.")
                .AddToggle("UI TopMost", t => uiManager.T_UITopMost = t,
                    tooltip: "Keep this window above all other windows.")
                .AddToggle("Debug Mode", t => uiManager.T_DebugMode = t,
                    tooltip: "Show extra information useful for troubleshooting problems.")
                .AddButton("Save Config", b =>
                {
                    uiManager.B_SaveConfig = b;
                    b.Reader.Click += (s, e) => new ConfigSaver().ShowDialog();
                }, tooltip: "Save your current settings to a file you can load later.")
                .AddSeparator();
        }

        private void LoadDisplaySelectMenu()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, DisplaySelectMenu);

            builder
                .AddTitle("Screen Settings", true, t =>
                {
                    uiManager.AT_DisplaySelector = t;
                    t.Minimize.Click += (s, e) =>
                        TogglePanel("Screen Settings", DisplaySelectMenuPanel);
                })
                .AddDropdown("Screen Capture Method", d =>
                {
                    d.DropdownBox.SelectedIndex = -1;  // Prevent auto-selection that overwrites saved state
                    uiManager.D_ScreenCaptureMethod = d;
                    _mainWindow.AddDropdownItem(d, "DirectX");
                    _mainWindow.AddDropdownItem(d, "GDI+");
                }, tooltip: "How the screen is captured. DirectX is faster, GDI+ works on more systems.")
                .AddToggle("StreamGuard", t => uiManager.T_StreamGuard = t,
                    tooltip: "Hide the overlay from screen recordings and streams.")
                .AddSeparator();

            try
            {
                // Handle DisplaySelector separately as it's a custom control
                uiManager.DisplaySelector = new ADisplaySelector();
                uiManager.DisplaySelector.RefreshDisplays();

                // Insert after title but before separator
                var insertIndex = DisplaySelectMenu.Children.Count - 2;
                DisplaySelectMenu.Children.Insert(insertIndex, uiManager.DisplaySelector);

                // Add refresh button after DisplaySelector
                var refreshButton = new APButton("Refresh Displays", "Update the list of available monitors.");
                refreshButton.Reader.Click += (s, e) =>
                {
                    try
                    {
                        DisplayManager.RefreshDisplays();
                        uiManager.DisplaySelector?.RefreshDisplays();
                        LogManager.Log(LogLevel.Info, "Display list refreshed successfully.", true);
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogLevel.Error, $"Error refreshing displays: {ex.Message}", true);
                    }
                };
                DisplaySelectMenu.Children.Insert(insertIndex + 1, refreshButton);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Warning, $"Display selector failed to initialize: {ex.Message}", true);
            }
        }

        private void LoadThemeMenu()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, ThemeMenu);

            builder
                .AddTitle("Theme Settings", true, t =>
                {
                    uiManager.AT_ThemeColorWheel = t;
                    t.Minimize.Click += (s, e) =>
                        TogglePanel("Theme Settings", ThemeMenuPanel);
                })
                .AddSeparator();

            try
            {
                // Handle ColorWheel separately as it's a custom control
                uiManager.ThemeColorWheel = new AColorWheel();

                var arrowButton = uiManager.ThemeColorWheel.FindName("ArrowButton") as Button;
                if (arrowButton != null)
                {
                    arrowButton.Visibility = Visibility.Visible;
                }

                // Insert before separator
                var insertIndex = ThemeMenu.Children.Count - 2;
                ThemeMenu.Children.Insert(insertIndex, uiManager.ThemeColorWheel);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Warning, $"Theme wheel failed to initialize: {ex.Message}", true);
            }
        }

        #endregion

        #region Helper Methods

        private void OnDisplayChanged(object? sender, DisplayChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    LogManager.Log(LogLevel.Info, $"AI focus switched to Display {e.DisplayIndex + 1} ({e.Bounds.Width}x{e.Bounds.Height})", true);
                    UpdateDisplayRelatedSettings(e);
                }
                catch (Exception ex)
                {
                }
            });
        }

        private void UpdateDisplayRelatedSettings(DisplayChangedEventArgs e)
        {
            Dictionary.sliderSettings["SelectedDisplay"] = e.DisplayIndex;
        }

        private async Task ResetToMouseEvent()
        {
            await Task.Delay(500);
            _mainWindow!.uiManager.D_MouseMovementMethod!.DropdownBox.SelectedIndex = 0;
        }


        public void Dispose()
        {
            DisplayManager.DisplayChanged -= OnDisplayChanged;
            _mainWindow?.uiManager.DisplaySelector?.Dispose();

            // Save minimize states before disposing
            SaveMinimizeStatesToGlobal();
        }

        private void SafeLoadSection(Action loadAction)
        {
            try
            {
                loadAction();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Warning, $"Settings section failed to load: {ex.Message}", true);
            }
        }

        #endregion

        #region Control Creation Methods

        private AToggle CreateToggle(string title, string? tooltip = null)
        {
            var toggle = new AToggle(title, tooltip);
            _mainWindow!.toggleInstances[title] = toggle;

            // Set initial state
            if (!Dictionary.toggleState.ContainsKey(title))
            {
                Dictionary.toggleState[title] = false;
            }

            if (Dictionary.toggleState[title])
                toggle.EnableSwitch();
            else
                toggle.DisableSwitch();

            // Handle click
            toggle.Reader.Click += (sender, e) =>
            {
                Dictionary.toggleState[title] = !Dictionary.toggleState[title];
                _mainWindow.UpdateToggleUI(toggle, Dictionary.toggleState[title]);
                _mainWindow.Toggle_Action(title);
            };

            return toggle;
        }

        //copied & Pasted from other class
        private AKeyChanger CreateKeyChanger(string title, string keybind, string? tooltip = null)
        {
            var keyChanger = new AKeyChanger(title, keybind, tooltip);

            keyChanger.Reader.Click += (sender, e) =>
            {
                keyChanger.KeyNotifier.Content = "...";
                _mainWindow!.bindingManager.StartListeningForBinding(title);

                Action<string, string>? bindingSetHandler = null;
                bindingSetHandler = (bindingId, key) =>
                {
                    if (bindingId == title)
                    {
                        keyChanger.KeyNotifier.Content = KeybindNameManager.ConvertToRegularKey(key);
                        Dictionary.bindingSettings[bindingId] = key;
                        _mainWindow.bindingManager.OnBindingSet -= bindingSetHandler;
                    }
                };

                _mainWindow.bindingManager.OnBindingSet += bindingSetHandler;
            };

            return keyChanger;
        }

        private ASlider CreateSlider(string title, string label, double frequency, double buttonSteps,
            double min, double max, string? tooltip = null)
        {
            var slider = new ASlider(title, label, buttonSteps, tooltip)
            {
                Slider = { Minimum = min, Maximum = max, TickFrequency = frequency }
            };

            slider.Slider.Value = Dictionary.sliderSettings.TryGetValue(title, out var value) ? value : min;
            slider.Slider.ValueChanged += (s, e) => Dictionary.sliderSettings[title] = slider.Slider.Value;

            return slider;
        }

        private ADropdown CreateDropdown(string title, string? tooltip = null) => new(title, title, tooltip);

        #endregion

        #region Section Builder

        private class SectionBuilder
        {
            private readonly SettingsMenuControl _parent;
            private readonly StackPanel _panel;

            public SectionBuilder(SettingsMenuControl parent, StackPanel panel)
            {
                _parent = parent;
                _panel = panel;
            }

            public SectionBuilder AddTitle(string title, bool canMinimize, Action<ATitle>? configure = null)
            {
                var titleControl = new ATitle(title, canMinimize);
                configure?.Invoke(titleControl);
                _panel.Children.Add(titleControl);
                return this;
            }

            public SectionBuilder AddToggle(string title, Action<AToggle>? configure = null, string? tooltip = null)
            {
                var toggle = _parent.CreateToggle(title, tooltip);
                configure?.Invoke(toggle);
                _panel.Children.Add(toggle);
                return this;
            }

            public SectionBuilder AddKeyChanger(string title, Action<AKeyChanger>? configure = null, string? defaultKey = null, string? tooltip = null)
            {
                var key = defaultKey;
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = Dictionary.bindingSettings.TryGetValue(title, out var savedKey) && !string.IsNullOrWhiteSpace(savedKey)
                        ? savedKey
                        : "None";
                }
                var keyChanger = _parent.CreateKeyChanger(title, key, tooltip);
                configure?.Invoke(keyChanger);
                _panel.Children.Add(keyChanger);
                return this;
            }

            public SectionBuilder AddSlider(string title, string label, double frequency, double buttonSteps,
                double min, double max, Action<ASlider>? configure = null, string? tooltip = null)
            {
                var slider = _parent.CreateSlider(title, label, frequency, buttonSteps, min, max, tooltip);
                configure?.Invoke(slider);
                _panel.Children.Add(slider);
                return this;
            }

            public SectionBuilder AddDropdown(string title, Action<ADropdown>? configure = null, string? tooltip = null)
            {
                var dropdown = _parent.CreateDropdown(title, tooltip);
                configure?.Invoke(dropdown);
                _panel.Children.Add(dropdown);
                return this;
            }

            public SectionBuilder AddButton(string title, Action<APButton>? configure = null, string? tooltip = null)
            {
                var button = new APButton(title, tooltip);
                configure?.Invoke(button);
                _panel.Children.Add(button);
                return this;
            }

            public SectionBuilder AddSeparator()
            {
                _panel.Children.Add(new ARectangleBottom());
                _panel.Children.Add(new ASpacer());
                return this;
            }
        }

        #endregion
    }
}

