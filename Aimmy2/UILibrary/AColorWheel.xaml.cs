using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Aimmy2.Theme;

namespace Aimmy2.UILibrary
{
    public partial class AColorWheel : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register("Title", typeof(string), typeof(AColorWheel),
            new PropertyMetadata("Theme Color", OnTitleChanged));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AColorWheel control && e.NewValue is string newTitle)
            {
                control.ColorWheelTitle.Content = newTitle;
            }
        }

        private bool _isMouseDown = false;
        private WriteableBitmap _colorWheelBitmap;
        private Color _selectedColor = Color.FromRgb(114, 46, 209); // Default purple
        private Color _previewColor = Color.FromRgb(114, 46, 209); // For live preview
        private double _brightness = 1.0;
        private double _currentHue = 0;
        private double _currentSaturation = 0;
        private bool _isUpdatingFromCode = false;
        private Color _initialColor = Colors.Transparent;
        private bool _hasInitialColor = false;
        public bool SuppressThemeApply { get; set; } = false;

        public AColorWheel()
        {
            InitializeComponent();
            Loaded += AColorWheel_Loaded;
        }

        private void AColorWheel_Loaded(object sender, RoutedEventArgs e)
        {
            // Create the color wheel bitmap
            CreateColorWheel();

            if (_hasInitialColor)
            {
                _selectedColor = _initialColor;
            }
            else
            {
                _selectedColor = ThemeManager.ThemeColor;
            }
            _previewColor = _selectedColor;
            UpdateColorPreview(_previewColor);

            // Position selector based on current color
            PositionSelectorForColor(_selectedColor);

            // Update brightness gradient
            UpdateBrightnessGradient();
        }

        private void CreateColorWheel()
        {
            int size = 200;
            _colorWheelBitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);

            byte[] pixels = new byte[size * size * 4];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Calculate distance from center
                    double dx = x - size / 2.0;
                    double dy = y - size / 2.0;
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    // Only draw within the circle
                    if (distance <= size / 2.0)
                    {
                        // Calculate angle for hue (0-360)
                        double angle = Math.Atan2(dy, dx);
                        double hue = (angle + Math.PI) / (2 * Math.PI) * 360;

                        // Calculate saturation based on distance from center
                        double saturation = distance / (size / 2.0);

                        // Convert HSV to RGB with full brightness for the wheel
                        Color color = HsvToRgb(hue, saturation, 1.0);

                        int pixelOffset = (y * size + x) * 4;
                        pixels[pixelOffset] = color.B;
                        pixels[pixelOffset + 1] = color.G;
                        pixels[pixelOffset + 2] = color.R;
                        pixels[pixelOffset + 3] = 255;
                    }
                    else
                    {
                        // Transparent outside the circle
                        int pixelOffset = (y * size + x) * 4;
                        pixels[pixelOffset + 3] = 0;
                    }
                }
            }

            _colorWheelBitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            ColorWheelEllipse.Fill = new ImageBrush(_colorWheelBitmap);
        }

        private void ColorWheel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isMouseDown = true;
            UpdateColorFromPosition(e.GetPosition(ColorWheelCanvas));
            ColorWheelCanvas.CaptureMouse();
        }

        private void ColorWheel_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isMouseDown)
            {
                UpdateColorFromPosition(e.GetPosition(ColorWheelCanvas));
            }
        }

        private void ColorWheel_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isMouseDown = false;
            ColorWheelCanvas.ReleaseMouseCapture();

            if (!SuppressThemeApply)
            {
                SaveThemeColor(_previewColor);
            }
        }

        private void UpdateColorFromPosition(Point position)
        {
            double centerX = ColorWheelCanvas.Width / 2;
            double centerY = ColorWheelCanvas.Height / 2;

            double dx = position.X - centerX;
            double dy = position.Y - centerY;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            // Constrain to circle
            if (distance > centerX)
            {
                double angle = Math.Atan2(dy, dx);
                dx = Math.Cos(angle) * centerX;
                dy = Math.Sin(angle) * centerX;
                distance = centerX;
            }

            // Update selector position
            Canvas.SetLeft(ColorSelector, centerX + dx - 10); // Adjusted for new selector size
            Canvas.SetTop(ColorSelector, centerY + dy - 10);

            // Calculate color
            _currentHue = (Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI) * 360;
            _currentSaturation = distance / centerX;

            _previewColor = HsvToRgb(_currentHue, _currentSaturation, _brightness);

            // Update preview
            UpdateColorPreview(_previewColor);

            // Update brightness gradient
            UpdateBrightnessGradient();

            // Update the selector dot color
            if (ColorDot != null)
            {
                ColorDot.Fill = new SolidColorBrush(_previewColor);
            }
        }

        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BrightnessSlider != null && !_isUpdatingFromCode)
            {
                _brightness = BrightnessSlider.Value;

                // Recalculate color with new brightness
                _previewColor = HsvToRgb(_currentHue, _currentSaturation, _brightness);

                // Update preview
                UpdateColorPreview(_previewColor);

                // Update the selector dot color
                if (ColorDot != null)
                {
                    ColorDot.Fill = new SolidColorBrush(_previewColor);
                }
            }
        }

        private void BrightnessSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            // Save the color when brightness slider is released
            if (!SuppressThemeApply)
            {
                SaveThemeColor(_previewColor);
            }
        }

        private void UpdateColorPreview(Color color)
        {
            // Update the preview circle
            if (ColorPreview != null)
            {
                ColorPreview.Fill = new SolidColorBrush(color);
            }

            // Update hex value
            if (HexValue != null)
            {
                HexValue.Content = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
        }

        private void UpdateBrightnessGradient()
        {
            // Update the brightness slider gradient to show the color range
            if (BrightnessGradientStart != null && BrightnessGradientEnd != null)
            {
                // Start with black
                BrightnessGradientStart.Color = Color.FromRgb(0, 0, 0);

                // End with the full brightness color
                BrightnessGradientEnd.Color = HsvToRgb(_currentHue, _currentSaturation, 1.0);
            }
        }

        private void SaveThemeColor(Color color)
        {
            // Update selected color
            _selectedColor = color;

            // Use ThemeManager to set and save the color
            ThemeManager.SetThemeColor(color);

            // Save to settings (implement your settings save logic here)
            string hexColor = ThemeManager.GetThemeColorHex();
            // Settings.SaveThemeColor(hexColor);
        }

        private void PositionSelectorForColor(Color color)
        {
            _isUpdatingFromCode = true;

            // Convert RGB to HSV to find position
            double h, s, v;
            RgbToHsv(color, out h, out s, out v);

            // Store current values
            _currentHue = h;
            _currentSaturation = s;
            _brightness = v;

            // Calculate position from HSV
            double angle = h * Math.PI / 180.0 - Math.PI;
            double radius = s * (ColorWheelCanvas.Width / 2);

            double x = ColorWheelCanvas.Width / 2 + Math.Cos(angle) * radius;
            double y = ColorWheelCanvas.Height / 2 + Math.Sin(angle) * radius;

            Canvas.SetLeft(ColorSelector, x - 10);
            Canvas.SetTop(ColorSelector, y - 10);

            // Set brightness slider
            BrightnessSlider.Value = v;

            // Update selector dot color
            if (ColorDot != null)
            {
                ColorDot.Fill = new SolidColorBrush(color);
            }

            // Update brightness gradient
            UpdateBrightnessGradient();

            _isUpdatingFromCode = false;
        }

        public void LoadSavedThemeColor(string hexColor)
        {
            try
            {
                Color color = (Color)ColorConverter.ConvertFromString(hexColor);
                _selectedColor = color;
                _previewColor = color;

                // Update UI
                UpdateColorPreview(color);
                PositionSelectorForColor(color);

                // Apply theme
                ThemeManager.SetThemeColor(color);
            }
            catch
            {
                // If parsing fails, keep default color
            }
        }

        public Color GetCurrentPreviewColor()
        {
            if (ColorPreview.Fill is SolidColorBrush brush)
            {
                return brush.Color;
            }
            return Colors.Transparent;
        }

        public void SetInitialColor(Color color)
        {
            _initialColor = color;
            _hasInitialColor = true;
        }

        #region Color Conversion Methods

        private Color HsvToRgb(double hue, double saturation, double value)
        {
            int hi = (int)(hue / 60) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            byte v = (byte)value;
            byte p = (byte)(value * (1 - saturation));
            byte q = (byte)(value * (1 - f * saturation));
            byte t = (byte)(value * (1 - (1 - f) * saturation));

            switch (hi)
            {
                case 0: return Color.FromRgb(v, t, p);
                case 1: return Color.FromRgb(q, v, p);
                case 2: return Color.FromRgb(p, v, t);
                case 3: return Color.FromRgb(p, q, v);
                case 4: return Color.FromRgb(t, p, v);
                default: return Color.FromRgb(v, p, q);
            }
        }

        private void RgbToHsv(Color color, out double hue, out double saturation, out double value)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            // Value
            value = max;

            // Saturation
            if (max == 0)
                saturation = 0;
            else
                saturation = delta / max;

            // Hue
            if (delta == 0)
            {
                hue = 0;
            }
            else if (max == r)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }

            if (hue < 0)
                hue += 360;
        }

        #endregion
    }
}