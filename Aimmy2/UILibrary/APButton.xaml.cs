namespace Aimmy2.UILibrary
{
    /// <summary>
    /// Interaction logic for APButton.xaml
    /// </summary>
    public partial class APButton : System.Windows.Controls.UserControl
    {
        private string v;

        public APButton(string v)
        {
            this.v = v;
        }

        public APButton(string Text, string? tooltip)
        {
            InitializeComponent();
            ButtonTitle.Content = Text;
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