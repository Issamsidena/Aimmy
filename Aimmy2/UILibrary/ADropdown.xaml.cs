using Aimmy2.Class;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace UILibrary
{
    /// <summary>
    /// Interaction logic for ADropdown.xaml
    /// </summary>
    public partial class ADropdown : UserControl
    {
        private string title1;
        private string title2;

        private string main_dictionary_path { get; set; }

        public ADropdown(string title, string dictionary_path, string? tooltip)
        {
            InitializeComponent();
            DropdownTitle.Content = title;
            main_dictionary_path = dictionary_path;
            if (!string.IsNullOrEmpty(tooltip))
            {
                var tt = new ToolTip { Content = tooltip };
                if (TryFindResource("Tooltip") is System.Windows.Style style)
                    tt.Style = style;
                ToolTip = tt;
            }
        }

        public ADropdown(string title1, string title2)
        {
            this.title1 = title1;
            this.title2 = title2;
        }

        private void DropdownBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItemContent = ((ComboBoxItem)DropdownBox.SelectedItem)?.Content?.ToString();
            if (selectedItemContent != null)
            {
                Dictionary.dropdownState[main_dictionary_path] = selectedItemContent;
            }
        }
    }
}