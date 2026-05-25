using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aimmy2.Other
{
    public static class StreamGuardManager
    {
        #region Constants
        const uint WDA_NONE = 0;
        const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_APPWINDOW = 0x00040000;
        const uint GA_ROOT = 2;
        #endregion

        #region Private Fields
        private static bool _isEnabled = false;
        private static HashSet<nint> _protectedWindows = new();
        private static bool _eventsAttached = false;
        private static System.Windows.Threading.DispatcherTimer _popupMonitorTimer;
        #endregion

        #region P/Invoke Declarations
        private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(nint hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern nint GetParent(nint hWnd);

        [DllImport("user32.dll")]
        private static extern nint GetAncestor(nint hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern int GetClassName(nint hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(nint hWnd);
        #endregion

        #region Window Protection
        private static void ApplyToWindow(Window window, bool enable)
        {
            if (window == null) return;

            var hWnd = new WindowInteropHelper(window).Handle;
            if (hWnd == nint.Zero)
            {
                if (enable && _isEnabled)
                {
                    window.SourceInitialized += (s, e) => ApplyToWindow(window, true);
                }
                return;
            }

            if (enable)
            {
                if (_protectedWindows.Contains(hWnd)) return;
                _protectedWindows.Add(hWnd);
            }
            else
            {
                _protectedWindows.Remove(hWnd);
            }

            SetWindowDisplayAffinity(hWnd, enable ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
            window.ShowInTaskbar = !enable;

            var extendedStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            if (enable)
                SetWindowLong(hWnd, GWL_EXSTYLE, (extendedStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
            else
                SetWindowLong(hWnd, GWL_EXSTYLE, (extendedStyle | WS_EX_APPWINDOW) & ~WS_EX_TOOLWINDOW);
        }

        private static Window FindParentWindow(UserControl userControl)
        {
            Window parentWindow = Window.GetWindow(userControl);
            if (parentWindow != null)
            {
                return parentWindow;
            }

            DependencyObject parent = userControl;
            while (parent != null && !(parent is Window))
            {
                parent = VisualTreeHelper.GetParent(parent) ?? LogicalTreeHelper.GetParent(parent);
            }

            if (parent is Window window)
            {
                return window;
            }

            DependencyObject current = userControl;
            while (current != null)
            {
                if (current is System.Windows.Controls.Primitives.Popup popup && popup.Child != null)
                {
                    var popupRoot = popup.PlacementTarget;
                    if (popupRoot != null)
                    {
                        return Window.GetWindow(popupRoot);
                    }
                }
                current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static void ApplyToUserControl(UserControl userControl, bool enable)
        {
            if (userControl == null) return;

            Window parentWindow = FindParentWindow(userControl);

            if (parentWindow != null)
            {
                ApplyToWindow(parentWindow, enable);
            }
            else if (enable)
            {
                userControl.Loaded += (s, e) =>
                {
                    Window delayedWindow = FindParentWindow(userControl);
                    if (delayedWindow != null)
                    {
                        ApplyToWindow(delayedWindow, enable);
                    }
                };
            }
        }
        #endregion

        #region Popup Window Protection
        private static void ProtectAllProcessWindows()
        {
            uint currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (IsWindowVisible(hWnd))
                    {
                        GetWindowThreadProcessId(hWnd, out uint windowProcessId);

                        if (windowProcessId == currentProcessId)
                        {
                            var className = new System.Text.StringBuilder(256);
                            GetClassName(hWnd, className, className.Capacity);
                            string classNameStr = className.ToString();

                            if (classNameStr.Contains("ComboBox") ||
                                classNameStr.Contains("Popup") ||
                                classNameStr.Equals("HwndWrapper[DefaultDomain") ||
                                classNameStr.Contains("HwndWrapper") ||
                                classNameStr.Contains("DropDown") ||
                                classNameStr.Contains("MenuDropAlignment"))
                            {
                                if (_isEnabled && !_protectedWindows.Contains(hWnd))
                                {
                                    SetWindowDisplayAffinity(hWnd, WDA_EXCLUDEFROMCAPTURE);
                                    _protectedWindows.Add(hWnd);

                                    var extendedStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                                    SetWindowLong(hWnd, GWL_EXSTYLE, (extendedStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
                                }
                                else if (!_isEnabled && _protectedWindows.Contains(hWnd))
                                {
                                    SetWindowDisplayAffinity(hWnd, WDA_NONE);
                                    _protectedWindows.Remove(hWnd);

                                    var extendedStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                                    SetWindowLong(hWnd, GWL_EXSTYLE, (extendedStyle | WS_EX_APPWINDOW) & ~WS_EX_TOOLWINDOW);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Continue enumeration on error
                }
                return true;
            }, nint.Zero);
        }
        #endregion

        #region Event Monitoring
        private static void AttachEvents()
        {
            if (_eventsAttached) return;

            Application.Current.Activated -= OnAppActivated;
            Application.Current.Activated += OnAppActivated;

            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
            EventManager.RegisterClassHandler(typeof(UserControl), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnUserControlLoaded));

            StartPopupMonitoring();
            _eventsAttached = true;
        }

        private static void DetachEvents()
        {
            if (!_eventsAttached) return;

            Application.Current.Activated -= OnAppActivated;
            StopPopupMonitoring();
            _eventsAttached = false;
        }

        private static void StartPopupMonitoring()
        {
            if (_popupMonitorTimer != null) return;

            _popupMonitorTimer = new System.Windows.Threading.DispatcherTimer();
            _popupMonitorTimer.Interval = TimeSpan.FromMilliseconds(100);
            _popupMonitorTimer.Tick += (s, e) => ProtectAllProcessWindows();
            _popupMonitorTimer.Start();
        }

        private static void StopPopupMonitoring()
        {
            if (_popupMonitorTimer != null)
            {
                _popupMonitorTimer.Stop();
                _popupMonitorTimer = null;
            }
        }

        private static void OnAppActivated(object? sender, EventArgs e)
        {
            if (!_isEnabled) return;
            CheckAndProtectNewWindows();
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (!_isEnabled) return;
            if (sender is Window window)
            {
                ApplyToWindow(window, true);
            }
        }

        private static void OnUserControlLoaded(object sender, RoutedEventArgs e)
        {
            if (!_isEnabled) return;
            if (sender is UserControl userControl)
            {
                ApplyToUserControl(userControl, true);
            }
        }
        #endregion

        #region Helper Methods
        private static void CheckAllUserControls()
        {
            foreach (Window window in Application.Current.Windows)
            {
                CheckUserControlsInWindow(window);
            }
        }

        private static void CheckUserControlsInWindow(DependencyObject parent)
        {
            if (parent == null) return;

            if (parent is UserControl userControl)
            {
                ApplyToUserControl(userControl, true);
            }

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                CheckUserControlsInWindow(child);
            }
        }

        private static void CheckAndProtectNewWindows()
        {
            foreach (Window window in Application.Current.Windows)
            {
                var hWnd = new WindowInteropHelper(window).Handle;
                if (hWnd != nint.Zero && !_protectedWindows.Contains(hWnd))
                {
                    ApplyToWindow(window, true);
                }
            }

            CheckAllUserControls();
            ProtectAllProcessWindows();
        }
        #endregion

        #region Public API
        public static void ApplyStreamGuardToAllWindows(bool enable)
        {
            _isEnabled = enable;

            foreach (Window window in Application.Current.Windows)
                ApplyToWindow(window, enable);

            if (enable)
            {
                CheckAllUserControls();
                ProtectAllProcessWindows();
                AttachEvents();
            }
            else
            {
                // Unprotect all popup windows when disabling
                ProtectAllProcessWindows();
                DetachEvents();
                _protectedWindows.Clear();
            }
        }

        public static void ForceProtectAllContent()
        {
            if (!_isEnabled) return;

            foreach (Window window in Application.Current.Windows)
            {
                ApplyToWindow(window, true);
                CheckUserControlsInWindow(window);
            }

            ProtectAllProcessWindows();
        }

        public static void ProtectComboBoxPopups()
        {
            if (!_isEnabled) return;
            ProtectAllProcessWindows();
        }

        public static void ProtectWindow(Window window)
        {
            if (_isEnabled)
            {
                ApplyToWindow(window, true);
            }
        }

        public static void ProtectUserControl(UserControl userControl)
        {
            if (_isEnabled)
            {
                ApplyToUserControl(userControl, true);
            }
        }
        #endregion
    }
}