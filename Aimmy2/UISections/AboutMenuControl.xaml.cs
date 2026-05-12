using System;
using Aimmy2.Theme;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Newtonsoft.Json.Linq;

namespace Aimmy2.Controls
{
    public partial class AboutMenuControl : UserControl
    {
        private MainWindow? _mainWindow;
        private bool _isInitialized;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

        static AboutMenuControl()
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Aimmy2");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        }

        private Brush? _themeColor;
        private FontFamily? _fontFamily;

        private static readonly (string name, string role, string? github)[] CoreTeam =
        {
            ("Babyhamsta", "AI Logic", "Babyhamsta"),
            ("MarsQQ", "Design", "MarsInsanity"),
            ("Taylor", "Optimization", "TaylorIsBlue")
        };

        private static readonly (string name, string? github, bool highlighted)[] Contributors =
        {
            ("Whoswhip", "whoswhip", true),
            ("Camilia2o7", "Camilia2o7", true),
            ("Shall0e", null, false),
            ("Wisethef0x", null, false),
            ("HakaCat", null, false),
            ("Themida", null, false),
            ("Issamsidena", null, false),
            ("Ninja", null, false)
        };

        public Label AboutSpecsControl => AboutSpecs;
        public ScrollViewer AboutMenuScrollViewer => AboutMenu;

        public AboutMenuControl()
        {
            InitializeComponent();
        }

        public void Initialize(MainWindow mainWindow)
        {
            if (_isInitialized) return;
            _mainWindow = mainWindow;
            _isInitialized = true;

            _themeColor = new SolidColorBrush(ThemeManager.ThemeColor);
            _fontFamily = Application.Current.TryFindResource("Atkinson Hyperlegible") as FontFamily
                ?? new FontFamily("Segoe UI");

            LoadCoreTeam();
            LoadContributors();
        }

        private void LoadCoreTeam()
        {
            CoreTeamPanel.Children.Clear();
            foreach (var (name, role, github) in CoreTeam)
            {
                CoreTeamPanel.Children.Add(CreateCoreTeamMember(name, role, github));
            }
        }

        private StackPanel CreateCoreTeamMember(string name, string role, string? github)
        {
            var panel = new StackPanel { Margin = new Thickness(8, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Center };
            var avatarBorder = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(24),
                Background = _themeColor,
                Margin = new Thickness(0, 0, 0, 8),
                ClipToBounds = true
            };
            var fallbackText = new TextBlock
            {
                Text = name[0].ToString().ToUpper(),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            avatarBorder.Child = fallbackText;

            if (!string.IsNullOrEmpty(github))
            {
                LoadGitHubAvatar(github, avatarBorder);
            }

            panel.Children.Add(avatarBorder);

            var nameText = new TextBlock
            {
                Text = name,
                FontFamily = _fontFamily,
                FontSize = 12,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            if (!string.IsNullOrEmpty(github))
            {
                nameText.Cursor = Cursors.Hand;
                nameText.MouseEnter += (s, e) => nameText.TextDecorations = TextDecorations.Underline;
                nameText.MouseLeave += (s, e) => nameText.TextDecorations = null;
                nameText.MouseLeftButtonUp += (s, e) => OpenGitHubProfile(github);
                avatarBorder.Cursor = Cursors.Hand;
                avatarBorder.MouseLeftButtonUp += (s, e) => OpenGitHubProfile(github);
            }

            panel.Children.Add(nameText);
            panel.Children.Add(new TextBlock
            {
                Text = role,
                FontFamily = _fontFamily,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            return panel;
        }

        private async void LoadGitHubAvatar(string username, Border avatarBorder)
        {
            try
            {
                var userUrl = $"https://api.github.com/users/{Uri.EscapeDataString(username)}";
                using var userResponse = await _httpClient.GetAsync(userUrl);
                if (!userResponse.IsSuccessStatusCode) return;

                var userJson = await userResponse.Content.ReadAsStringAsync();
                var avatarUrl = JObject.Parse(userJson)["avatar_url"]?.Value<string>();
                if (string.IsNullOrEmpty(avatarUrl)) return;

                avatarUrl += (avatarUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "s=96";

                using var imageResponse = await _httpClient.GetAsync(avatarUrl);
                if (!imageResponse.IsSuccessStatusCode) return;

                var imageData = await imageResponse.Content.ReadAsByteArrayAsync();
                await Dispatcher.InvokeAsync(() =>
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new System.IO.MemoryStream(imageData);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    avatarBorder.Background = Brushes.Transparent;
                    avatarBorder.Child = new Ellipse
                    {
                        Width = 48,
                        Height = 48,
                        Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill }
                    };
                });
            }
            catch { }
        }

        private void LoadContributors()
        {
            HighlightedContributorsPanel.Children.Clear();
            ContributorsPanel.Children.Clear();

            foreach (var (name, github, highlighted) in Contributors)
            {
                var chip = CreateContributorChip(name, github, highlighted);
                if (highlighted) HighlightedContributorsPanel.Children.Add(chip);
                else ContributorsPanel.Children.Add(chip);
            }
        }

        private Border CreateContributorChip(string name, string? github, bool highlighted)
        {
            var border = new Border
            {
                Background = highlighted ? _themeColor : new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(highlighted ? 12 : 10),
                Padding = new Thickness(highlighted ? 12 : 10, highlighted ? 6 : 5, highlighted ? 12 : 10, highlighted ? 6 : 5),
                Margin = new Thickness(highlighted ? 4 : 3),
                Child = new TextBlock
                {
                    Text = name,
                    FontFamily = _fontFamily,
                    FontSize = highlighted ? 11 : 10,
                    Foreground = highlighted ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF))
                }
            };

            if (!string.IsNullOrEmpty(github))
            {
                var themeColor = _themeColor;
                border.Cursor = Cursors.Hand;
                border.MouseEnter += (s, e) => border.Background = highlighted
                    ? new SolidColorBrush(Color.FromArgb(0xFF, 0x90, 0x60, 0xE0))
                    : new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
                border.MouseLeave += (s, e) => border.Background = highlighted
                    ? themeColor
                    : new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
                border.MouseLeftButtonUp += (s, e) => OpenGitHubProfile(github);
            }
            return border;
        }

        private static void OpenGitHubProfile(string username)
        {
            try { Process.Start(new ProcessStartInfo { FileName = $"https://github.com/{username}", UseShellExecute = true }); } catch { }
        }

        private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/Issamsidena/Aimmy/releases",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://github.com/Issamsidena/Aimmy", UseShellExecute = true }); } catch { }
        }

        private void DiscordButton_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://discord.gg/aimmy", UseShellExecute = true }); } catch { }
        }

        private void VersionBorder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/Issamsidena/Aimmy/releases",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
