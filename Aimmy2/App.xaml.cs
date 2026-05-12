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
            try
            {
                // Keep startup theme stable and predictable.
                ThemeManager.SetThemeColor("#FF722ED1");
            }
            catch (Exception ex)
            {
                // Log error and use default color
                ThemeManager.SetThemeColor("#FF722ED1");
            }
        }
    }
}