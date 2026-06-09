using Aimmy2.Theme;
using System.Windows.Controls;
using System.Windows.Input;

namespace Aimmy2.UILibrary
{
    /// <summary>
    /// Interaction logic for ASlider.xaml
    /// </summary>
    public partial class ASlider : UserControl
    {
        private readonly string _notifierText;
        private int _decimalPlaces = 2;

        /// <summary>
        /// Number of decimal places shown in the value notifier (defaults to 2, e.g. "0.00").
        /// Set to 1 for "0.0", 0 for whole numbers. Updating it re-renders the current value.
        /// </summary>
        public int DecimalPlaces
        {
            get => _decimalPlaces;
            set
            {
                _decimalPlaces = value;
                UpdateNotifier();
            }
        }

        public ASlider(string Text, string NotifierText, double ButtonSteps, string? tooltip = null)
        {
            InitializeComponent();

            SliderTitle.Content = Text;
            _notifierText = NotifierText;

            if (!string.IsNullOrEmpty(tooltip))
            {
                var tt = new System.Windows.Controls.ToolTip { Content = tooltip };
                if (TryFindResource("Tooltip") is System.Windows.Style style)
                    tt.Style = style;
                ToolTip = tt;
            }

            Slider.ValueChanged += (s, e) => UpdateNotifier();

            SubtractOne.Click += (s, e) => UpdateSliderValue(-ButtonSteps);
            AddOne.Click += (s, e) => UpdateSliderValue(ButtonSteps);

            // Register buttons for theme updates when loaded
            Loaded += (s, e) =>
            {
                ThemeManager.RegisterElement(SubtractOne);
                ThemeManager.RegisterElement(AddOne);
            };
        }

        private void UpdateNotifier()
        {
            AdjustNotifier.Content = $"{Slider.Value.ToString("F" + _decimalPlaces)} {_notifierText}";
        }

        private void UpdateSliderValue(double change)
        {
            Slider.Value = Math.Round(Slider.Value + change, 2);
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