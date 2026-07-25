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

        // Window.IsVisible is a DependencyProperty and can only be read on the UI thread, so the
        // AI loop needs its own thread-safe view of whether the preview is actually on screen.
        private static volatile bool _isShown;

        // Only the newest frame is kept: the AI loop can outrun the UI thread, and queueing every
        // frame on the dispatcher grows without bound while pinning a frozen bitmap per entry.
        private readonly object _frameLock = new();
        private BitmapSource? _pendingFrame;
        private bool _renderQueued;

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
                    _isShown = false;
                    _instance.Hide();
                    _onClosedByUser?.Invoke();
                };
            }

            _instance.Show();
            _instance.Activate();
            _isShown = true;
        }

        public static void HideWindow()
        {
            _isShown = false;
            _instance?.Hide();
        }

        /// <summary>Pushes a captured frame. Safe to call from the AI loop thread.</summary>
        public static void PushFrame(Bitmap bmp)
        {
            var inst = _instance;

            // Nothing on screen -> do not pay for the pixel copy or the dispatcher hop at all.
            if (inst == null || !_isShown) return;

            BitmapSource src;
            try
            {
                src = ToBitmapSource(bmp);
            }
            catch
            {
                return;
            }

            inst.QueueFrame(src);
        }

        /// <summary>Replaces any frame the UI thread has not drawn yet, keeping at most one queued.</summary>
        private void QueueFrame(BitmapSource src)
        {
            bool needsDispatch;

            lock (_frameLock)
            {
                _pendingFrame = src; // drop the previous frame instead of queueing another one
                needsDispatch = !_renderQueued;
                _renderQueued = true;
            }

            if (needsDispatch)
            {
                Dispatcher.BeginInvoke(new Action(DrainPendingFrame));
            }
        }

        private void DrainPendingFrame()
        {
            BitmapSource? src;

            lock (_frameLock)
            {
                src = _pendingFrame;
                _pendingFrame = null;
                _renderQueued = false;
            }

            if (src != null)
            {
                ApplyFrame(src);
            }
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

        /// <summary>
        /// Copies the bitmap's pixels straight into a BitmapSource. The old implementation encoded the
        /// frame to a BMP MemoryStream and decoded it again, allocating ~3 MiB of LOH buffers per frame.
        /// </summary>
        private static BitmapSource ToBitmapSource(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                // Bgr32 (not Bgra32) keeps the preview opaque, matching how the BMP round-trip
                // used to discard the alpha channel.
                var src = BitmapSource.Create(
                    data.Width,
                    data.Height,
                    96, 96,
                    System.Windows.Media.PixelFormats.Bgr32,
                    null,
                    data.Scan0,
                    data.Stride * data.Height,
                    data.Stride);

                src.Freeze(); // frozen => safe to hand to the UI thread
                return src;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    }
}
