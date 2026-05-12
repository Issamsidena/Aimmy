using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Aimmy2.Theme;

namespace Aimmy2.UILibrary
{
    /// <summary>
    /// Interaction logic for ASlider.xaml
    /// </summary>
    public partial class ASlider : UserControl
    {
        private string title;
        private string label;
        private double buttonSteps;

        public ASlider(string Text, string NotifierText, double ButtonSteps, string? tooltip)
        {
            InitializeComponent();

            SliderTitle.Content = Text;

            Slider.ValueChanged += (s, e) =>
            {
                AdjustNotifier.Content = $"{Slider.Value:F2} {NotifierText}";
            };

            SubtractOne.Click += (s, e) => UpdateSliderValue(-ButtonSteps);
            AddOne.Click += (s, e) => UpdateSliderValue(ButtonSteps);

            // Keep +/- square buttons synced with current theme color.
            ApplyThemeToStepButtons();
            ThemeManager.ThemeChanged += OnThemeChanged;
            Unloaded += (s, e) => ThemeManager.ThemeChanged -= OnThemeChanged;

            if (!string.IsNullOrEmpty(tooltip))
            {
                var tt = new ToolTip { Content = tooltip };
                if (TryFindResource("Tooltip") is Style style)
                    tt.Style = style;
                ToolTip = tt;
            }
        }

        public ASlider(string title, string label, double buttonSteps)
        {
            this.title = title;
            this.label = label;
            this.buttonSteps = buttonSteps;
        }

        private void OnThemeChanged(object? sender, Color e)
        {
            ApplyThemeToStepButtons();
        }

        private void ApplyThemeToStepButtons()
        {
            var brush = new SolidColorBrush(ThemeManager.ThemeColor);
            SubtractOne.Background = brush;
            AddOne.Background = brush;
        }

        private void UpdateSliderValue(double change)
        {
            // Preserve full floating-point precision; do not round/quantize slider values internally.
            Slider.Value = Slider.Value + change;
        }

        private void Slider_MouseUp(object sender, MouseButtonEventArgs e)
        {
        }

        private void Slider_MouseUp_1(object sender, MouseButtonEventArgs e)
        {
            System.Windows.MessageBox.Show($"{Slider.Value:F2}");
        }
    }
}