using Aimmy2.Class;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using UserControl = System.Windows.Controls.UserControl;

namespace UILibrary
{
    /// <summary>
    /// Interaction logic for ADropdown.xaml
    /// </summary>
    public partial class ADropdown : UserControl
    {
        private string main_dictionary_path { get; set; }

        public ADropdown(string title, string dictionary_path, string? tooltip = null)
        {
            InitializeComponent();
            DropdownTitle.Content = title;
            main_dictionary_path = dictionary_path;

            if (!string.IsNullOrEmpty(tooltip))
            {
                var tt = new System.Windows.Controls.ToolTip { Content = tooltip };
                if (TryFindResource("Tooltip") is System.Windows.Style style)
                    tt.Style = style;
                ToolTip = tt;
            }
        }

        private void DropdownBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItemContent = ((ComboBoxItem)DropdownBox.SelectedItem)?.Content?.ToString();
            if (selectedItemContent != null)
            {
                Dictionary.dropdownState[main_dictionary_path] = selectedItemContent;
            }
        }

        // Replace the default fade with a spring/overshoot "pop" when the dropdown opens.
        private void DropdownBox_DropDownOpened(object sender, EventArgs e)
        {
            if (DropdownBox.Template?.FindName("PART_Popup", DropdownBox) is not Popup popup
                || popup.Child is not FrameworkElement child)
                return;

            // Kill the built-in fade so our animation is the only effect.
            popup.PopupAnimation = PopupAnimation.None;

            var scale = new ScaleTransform(1, 0);
            child.RenderTransform = scale;
            child.RenderTransformOrigin = new Point(0.5, 0);

            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 }
            };
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
    }
}