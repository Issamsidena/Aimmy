using Aimmy2.UILibrary;
using Other;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Visuality;

namespace Aimmy2.Controls
{
    public partial class ModelMenuControl : UserControl
    {
        //--
        private readonly HashSet<string> _modelFileHashes = new();
        private readonly HashSet<string> _configFileHashes = new();
        //--
        private MainWindow? _mainWindow;
        private bool _isInitialized = false;
        private bool _storeLoaded = false;
        private readonly object _storeLock = new object();

        public ModelMenuControl()
        {
            InitializeComponent();
        }

        public void Initialize(MainWindow mainWindow)
        {
            if (_isInitialized) return;

            _mainWindow = mainWindow;
            _isInitialized = true;

            // Don't load store immediately - wait for tab to be clicked
        }

        // Expose controls for MainWindow to access
        public ListBox ModelListBoxControl => ModelListBox;
        public Label SelectedModelNotifierControl => SelectedModelNotifier;
        public ListBox ConfigsListBoxControl => ConfigsListBox;
        public Label SelectedConfigNotifierControl => SelectedConfigNotifier;
        public Label LackOfModelsTextControl => LackOfModelsText;
        public Label LackOfConfigsTextControl => LackOfConfigsText;
        public StackPanel ModelStoreScrollerControl => ModelStoreScroller;
        public StackPanel ConfigStoreScrollerControl => ConfigStoreScroller;
        public TextBox SearchBoxControl => SearchBox;
        public TextBox CSearchBoxControl => CSearchBox;
        public ScrollViewer ModelMenuScrollViewer => ModelMenu;

        // Override visibility changed to detect when tab is selected
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.Property == IsVisibleProperty && (bool)e.NewValue && !_storeLoaded && _isInitialized)
            {
                _ = LoadStoreMenuAsync();
            }
        }

        private async Task LoadStoreMenuAsync()
        {
            lock (_storeLock)
            {
                if (_storeLoaded) return;
                _storeLoaded = true;
            }

            try
            {
                // Show loading indicators
                await Dispatcher.InvokeAsync(() =>
                {
                    // You could add a loading spinner here if desired
                    ModelStoreScroller.Children.Clear();
                    ConfigStoreScroller.Children.Clear();

                    var loadingText = new TextBlock
                    {
                        Text = "Loading store...",
                        Foreground = System.Windows.Media.Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 20, 0, 0)
                    };

                    ModelStoreScroller.Children.Add(loadingText);
                    ConfigStoreScroller.Children.Add(new TextBlock
                    {
                        Text = "Loading store...",
                        Foreground = System.Windows.Media.Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 20, 0, 0)
                    });
                }, DispatcherPriority.Normal);

                var availableModels = new HashSet<string>();
                var availableConfigs = new HashSet<string>();

                // Load data in parallel
                var tasks = new[]
                {
                    FileManager.RetrieveAndAddFiles("https://api.github.com/repos/Babyhamsta/Aimmy/contents/models", "bin\\models", availableModels),
                    FileManager.RetrieveAndAddFiles("https://api.github.com/repos/Babyhamsta/Aimmy/contents/configs", "bin\\configs", availableConfigs)
                };

                await Task.WhenAll(tasks);

                // Process results - just collect the data
                var modelNames = availableModels.OrderBy(m => m).ToList();
                var configNames = availableConfigs.OrderBy(c => c).ToList();

                // Update UI on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateStoreDisplay(ModelStoreScroller, modelNames, "models");
                    UpdateStoreDisplay(ConfigStoreScroller, configNames, "configs");
                }, DispatcherPriority.Background);
            }
            catch (Exception e)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    new NoticeBar($"Failed to load store: {e.Message}", 10000).Show();

                    // Show error in UI
                    ModelStoreScroller.Children.Clear();
                    ConfigStoreScroller.Children.Clear();

                    ModelStoreScroller.Children.Add(new TextBlock
                    {
                        Text = "Failed to load store",
                        Foreground = System.Windows.Media.Brushes.Red,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 20, 0, 0)
                    });
                });
            }
        }

        private void UpdateStoreDisplay(StackPanel scroller, List<string> items, string folder)
        {
            scroller.Children.Clear();

            if (items.Count > 0)
            {
                // Create gateways on UI thread
                foreach (var item in items)
                {
                    var gateway = new ADownloadGateway(item, folder);
                    scroller.Children.Add(gateway);
                }
            }
            else
            {
                if (folder == "configs")
                {
                    LackOfConfigsText.Visibility = Visibility.Visible;
                }
                else
                {
                    LackOfModelsText.Visibility = Visibility.Visible;
                }
            }
        }

        private void OpenFolderB_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton && clickedButton.Tag != null)
            {
                try
                {
                    var path = Path.Combine(Directory.GetCurrentDirectory(), "bin", clickedButton.Tag.ToString());
                    if (Directory.Exists(path))
                    {
                        Process.Start("explorer.exe", path);
                    }
                    else
                    {
                        new NoticeBar($"Directory not found: {path}", 5000).Show();
                    }
                }
                catch (Exception ex)
                {
                    new NoticeBar($"Failed to open folder: {ex.Message}", 5000).Show();
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateVisibilityBasedOnSearchText((TextBox)sender, ModelStoreScroller);
        }

        private void CSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateVisibilityBasedOnSearchText((TextBox)sender, ConfigStoreScroller);
        }

        private void LocalModelSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterListBoxBySearch(ModelListBox, (TextBox)sender);
        }

        private void LocalConfigSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterListBoxBySearch(ConfigsListBox, (TextBox)sender);
        }

        private void FilterListBoxBySearch(ListBox listBox, TextBox searchBox)
        {
            string searchText = searchBox.Text?.ToLower() ?? "";

            foreach (var item in listBox.Items)
            {
                if (listBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem listBoxItem)
                {
                    string contentText = item.ToString()?.ToLower() ?? "";
                    listBoxItem.Visibility = contentText.Contains(searchText)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
        }
        private void UpdateVisibilityBasedOnSearchText(TextBox textBox, Panel panel)
        {
            if (panel.Children.Count == 0) return;

            string searchText = textBox.Text?.ToLower() ?? "";
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var child in panel.Children)
                {
                    if (child is ADownloadGateway item)
                    {
                        var title = item.Title.Content?.ToString()?.ToLower() ?? "";
                        item.Visibility = title.Contains(searchText) ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }), DispatcherPriority.Input);
        }

        // Drag and drop for ModelListBox / ConfigsListBox lives in FileManager, it owns the
        // target folders and the file watchers that refresh these lists.
    }
}