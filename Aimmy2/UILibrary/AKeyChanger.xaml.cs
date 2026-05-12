using Other;

namespace Aimmy2.UILibrary
{
    /// <summary>
    /// Interaction logic for AKeyChanger.xaml
    /// </summary>
    public partial class AKeyChanger : System.Windows.Controls.UserControl
    {
        public AKeyChanger(string Text, string Keybind, string? tooltip)
        {
            InitializeComponent();
            KeyChangerTitle.Content = Text;

            KeyNotifier.Content = KeybindNameManager.ConvertToRegularKey(Keybind);
            if (!string.IsNullOrEmpty(tooltip))
            {
                var tt = new System.Windows.Controls.ToolTip { Content = tooltip };
                if (TryFindResource("Tooltip") is System.Windows.Style style)
                    tt.Style = style;
                ToolTip = tt;
            }
        }
    }
}