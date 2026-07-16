using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Visuality
{
    /// <summary>
    /// Debug preview that mirrors exactly what the AI captures (the model's input region),
    /// driven by the frames grabbed inside AIManager. Toggled by "Show Screen Capture".
    /// Uses Aimmy's custom title bar.
    /// </summary>
    public partial class ScreenCaptureWindow : Window
    {
        private static ScreenCaptureWindow? _instance;
        private static Action? _onClosedByUser;

        private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();
        private int _frameCount;

        public ScreenCaptureWindow()
        {
            InitializeComponent();
        }

        /// <summary>Opens (or re-shows) the preview. <paramref name="onClosedByUser"/> fires if the user closes it via the X.</summary>
        public static void ShowWindow(Window? owner, Action? onClosedByUser = null)
        {
            _onClosedByUser = onClosedByUser;

            if (_instance == null)
            {
                _instance = new ScreenCaptureWindow();
                if (owner != null)
                    _instance.Owner = owner;

                // Closing hides the window and notifies the caller so the toggle can sync off.
                _instance.Closing += (s, e) =>
                {
                    e.Cancel = true;
                    _instance.Hide();
                    _onClosedByUser?.Invoke();
                };
            }

            _instance.Show();
            _instance.Activate();
        }

        public static void HideWindow() => _instance?.Hide();

        /// <summary>Pushes a captured frame. Safe to call from the AI loop thread.</summary>
        public static void PushFrame(Bitmap bmp)
        {
            var inst = _instance;
            if (inst == null) return;

            BitmapSource src;
            try
            {
                src = ToBitmapSource(bmp);
            }
            catch
            {
                return;
            }

            inst.Dispatcher.BeginInvoke(new Action(() => inst.ApplyFrame(src)));
        }

        private void ApplyFrame(BitmapSource src)
        {
            if (!IsVisible) return;

            CaptureImage.Source = src;
            EmptyHint.Visibility = Visibility.Collapsed;

            _frameCount++;
            if (_fpsStopwatch.ElapsedMilliseconds >= 500)
            {
                double fps = _frameCount / (_fpsStopwatch.ElapsedMilliseconds / 1000.0);
                FpsText.Text = $"FPS: {fps:F0}";
                _frameCount = 0;
                _fpsStopwatch.Restart();
            }
        }

        private static BitmapSource ToBitmapSource(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Bmp);
            ms.Position = 0;

            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze(); // frozen => safe to hand to the UI thread
            return img;
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    }
}
