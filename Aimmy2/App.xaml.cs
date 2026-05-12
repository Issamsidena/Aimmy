using Aimmy2.Class;
using Aimmy2.Theme;
using Class;
using System;
using System.Collections.Generic;
using System.Windows;

namespace Aimmy2
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Initialize the application theme from saved settings
            InitializeTheme();

            // Set shutdown mode to prevent app from closing when startup window closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                // Create and show startup window
                var startupWindow = new StartupWindow();
                startupWindow.Show();

                // Reset shutdown mode after startup window is shown
                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
            catch (Exception ex)
            {
                // If startup window fails, launch main window directly
                MessageBox.Show($"Startup animation failed: {ex.Message}\nLaunching main application...",
                              "Aimmy AI", MessageBoxButton.OK, MessageBoxImage.Information);

                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();

                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
        }

        private void InitializeTheme()
        {
            const string fallbackHex = "#FF722ED1";
            try
            {
                SaveDictionary.EnsureDirectoriesExist();
                SaveDictionary.LoadJSON(Dictionary.colorState, "bin\\colors.cfg");

                if (Dictionary.colorState.TryGetValue("Theme Color", out var saved) &&
                    saved != null &&
                    !string.IsNullOrWhiteSpace(saved.ToString()))
                {
                    ThemeManager.SetThemeColor(saved.ToString()!.Trim());
                    return;
                }
            }
            catch
            {
                // Fall through to default
            }

            ThemeManager.SetThemeColor(fallbackHex);
        }
    }
}