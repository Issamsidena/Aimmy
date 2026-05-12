using Aimmy2.Class;
using Aimmy2.Theme;
using AimmyWPF.Class;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Visuality
{
    /// <summary>
    /// Optimized notice bar that uses a single window for all notices.
    /// Maintains backward compatibility with the original constructor-based API.
    /// </summary>
    public partial class NoticeBar : Window
    {
        private static NoticeBar containerInstance;
        private static readonly object instanceLock = new object();
        private static ObservableCollection<NoticeItem> notices;
        private static DispatcherTimer cleanupTimer;
        private readonly bool isContainerInstance;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ClickThroughOverlay.MakeClickThrough(new WindowInteropHelper(this).Handle);
        }

        // Constructor for backward compatibility - creates individual notice windows
        public NoticeBar(string text, int waitingTime = 4000, NoticeType type = NoticeType.Info) : this(true)
        {
            // This creates a standalone notice for backward compatibility
            isContainerInstance = false;
            CreateStandaloneNotice(text, waitingTime, type);
        }

        // Private constructor for the container instance
        private NoticeBar(bool isContainer)
        {
            InitializeComponent();
            isContainerInstance = isContainer;

            if (isContainer)
            {
                // Initialize as container
                if (notices == null)
                {
                    notices = new ObservableCollection<NoticeItem>();
                }
                NoticesContainer.ItemsSource = notices;

                // Setup cleanup timer to remove expired notices
                if (cleanupTimer == null)
                {
                    cleanupTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(250) // Check 4 times per second
                    };
                    cleanupTimer.Tick += CleanupExpiredNotices;
                    cleanupTimer.Start();
                }

                // Register with ThemeManager
                ThemeManager.RegisterElement(this);
                ThemeManager.ThemeChanged += OnThemeChanged;
            }
        }

        private async void CreateStandaloneNotice(string text, int waitingTime, NoticeType type)
        {
            // For backward compatibility - show using the optimized container
            Application.Current.Dispatcher.Invoke(() =>
            {
                Show(text, waitingTime, type);
            });

            // Hide this standalone instance immediately
            Hide();

            // Close after a delay to clean up
            await Task.Delay(100);
            Close();
        }

        private void OnThemeChanged(object sender, Color newColor)
        {
            if (isContainerInstance)
            {
                Dispatcher.Invoke(() =>
                {
                    foreach (var notice in notices)
                    {
                        notice.UpdateThemeColors();
                    }
                });
            }
        }

        private static void CleanupExpiredNotices(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            var toRemove = notices.Where(n => n.IsExpired(now)).ToList();

            foreach (var notice in toRemove)
            {
                if (containerInstance != null)
                {
                    containerInstance.AnimateRemoval(notice);
                }
            }
        }

        private async void AnimateRemoval(NoticeItem notice)
        {
            // Find the container for this notice
            var container = NoticesContainer?.ItemContainerGenerator.ContainerFromItem(notice) as ListBoxItem;
            if (container != null)
            {
                // Create storyboard for smooth removal animation
                var storyboard = new Storyboard();

                // Fade out
                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                Storyboard.SetTarget(fadeOut, container);
                Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
                storyboard.Children.Add(fadeOut);

                // Get the transform group
                var transform = container.RenderTransform as TransformGroup;
                if (transform != null && transform.Children.Count >= 2)
                {
                    var scale = transform.Children[0] as ScaleTransform;
                    var translate = transform.Children[1] as TranslateTransform;

                    // Scale down
                    var scaleX = new DoubleAnimation
                    {
                        From = 1,
                        To = 0.9,
                        Duration = TimeSpan.FromMilliseconds(250),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    Storyboard.SetTarget(scaleX, scale);
                    Storyboard.SetTargetProperty(scaleX, new PropertyPath(ScaleTransform.ScaleXProperty));
                    storyboard.Children.Add(scaleX);

                    var scaleY = new DoubleAnimation
                    {
                        From = 1,
                        To = 0.9,
                        Duration = TimeSpan.FromMilliseconds(250),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    Storyboard.SetTarget(scaleY, scale);
                    Storyboard.SetTargetProperty(scaleY, new PropertyPath(ScaleTransform.ScaleYProperty));
                    storyboard.Children.Add(scaleY);

                    // Slide out
                    var slideOut = new DoubleAnimation
                    {
                        From = 0,
                        To = 50,
                        Duration = TimeSpan.FromMilliseconds(250),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    Storyboard.SetTarget(slideOut, translate);
                    Storyboard.SetTargetProperty(slideOut, new PropertyPath(TranslateTransform.XProperty));
                    storyboard.Children.Add(slideOut);
                }

                // Start the storyboard
                storyboard.Begin();

                // Wait for animation to complete
                await Task.Delay(250);
            }

            notices?.Remove(notice);

            // Close window if no more notices
            if (notices?.Count == 0)
            {
                Hide();
            }
        }

        public static void Show(string message, int duration = 4000, NoticeType type = NoticeType.Info)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                lock (instanceLock)
                {
                    if (containerInstance == null)
                    {
                        containerInstance = new NoticeBar(true);
                    }

                    if (!containerInstance.IsVisible)
                    {
                        containerInstance.Show();
                    }

                    var notice = new NoticeItem(message, duration, type);
                    notices.Add(notice);

                    // Limit number of visible notices
                    while (notices.Count > 10)
                    {
                        notices.RemoveAt(0);
                    }

                    // Start the notice animations after it's added
                    containerInstance.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        containerInstance.StartNoticeAnimations(notice);
                    }), DispatcherPriority.Loaded);
                }
            });
        }

        private void StartNoticeAnimations(NoticeItem notice)
        {
            var container = NoticesContainer.ItemContainerGenerator.ContainerFromItem(notice) as ListBoxItem;
            if (container == null)
            {
                // If container not ready, wait a bit and try again
                Dispatcher.BeginInvoke(new Action(() => StartNoticeAnimations(notice)),
                    DispatcherPriority.Loaded);
                return;
            }

            // Find the progress bar in the visual tree
            var progressBar = FindVisualChild<Border>(container, "ProgressBar");
            if (progressBar != null)
            {
                // Animate progress bar width
                var progressAnimation = new DoubleAnimation
                {
                    From = 50,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(notice.Duration),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };

                progressBar.BeginAnimation(WidthProperty, progressAnimation);
            }
        }

        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild && typedChild.Name == name)
                    return typedChild;

                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (isContainerInstance)
            {
                cleanupTimer?.Stop();
                ThemeManager.ThemeChanged -= OnThemeChanged;
                ThemeManager.UnregisterElement(this);

                lock (instanceLock)
                {
                    containerInstance = null;
                    notices = null;
                    cleanupTimer = null;
                }
            }

            base.OnClosed(e);
        }
    }

    /// <summary>
    /// Notice item view model
    /// </summary>
    public class NoticeItem : INotifyPropertyChanged
    {
        private readonly DateTime createdTime;
        private readonly DateTime expiryTime;
        private readonly int duration;
        private double progressWidth = 50;
        private bool isRemoving = false;

        public string Message { get; }
        public NoticeType Type { get; }
        public string IconData { get; }
        public Brush IconColor { get; private set; }
        public Brush BorderBrush { get; private set; }
        public Color ThemeColorMid { get; private set; }
        public Color ThemeColorEnd { get; private set; }
        public Color ThemeColorLight { get; private set; }
        public int Duration => duration;

        public bool IsRemoving
        {
            get => isRemoving;
            set
            {
                isRemoving = value;
                OnPropertyChanged();
            }
        }

        public double ProgressWidth
        {
            get => progressWidth;
            set
            {
                progressWidth = value;
                OnPropertyChanged();
            }
        }

        public NoticeItem(string message, int duration, NoticeType type)
        {
            Message = message;
            Type = type;
            this.duration = duration;
            createdTime = DateTime.Now;
            expiryTime = createdTime.AddMilliseconds(duration);

            // Set icon data based on type
            IconData = type switch
            {
                NoticeType.Success => "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4M17.59,7L12,12.59L8.41,9L7,10.41L12,15.41L19,8.41L17.59,7Z",
                NoticeType.Warning => "M12,2L1,21H23M12,6L19.53,19H4.47M11,10V14H13V10M11,16V18H13V16",
                NoticeType.Error => "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4M9.88,9.88L7.76,7.76L6.34,9.17L8.46,11.29L6.34,13.41L7.76,14.83L9.88,12.71L12,14.83L13.41,13.41L11.29,11.29L13.41,9.17L12,7.76L9.88,9.88Z",
                _ => "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4M11,7V13H13V7H11M11,15V17H13V15H11Z"
            };

            UpdateColors();
        }

        private void UpdateColors()
        {
            // Set icon color based on type
            Color iconColorValue = Type switch
            {
                NoticeType.Success => Color.FromRgb(76, 175, 80),
                NoticeType.Warning => Color.FromRgb(255, 152, 0),
                NoticeType.Error => Color.FromRgb(244, 67, 54),
                _ => ThemeManager.ThemeColor
            };

            IconColor = new SolidColorBrush(iconColorValue);

            // Set theme colors
            ThemeColorMid = Color.FromArgb(204, ThemeManager.ThemeColor.R,
                                          ThemeManager.ThemeColor.G,
                                          ThemeManager.ThemeColor.B);
            ThemeColorEnd = Color.FromArgb(204, ThemeManager.ThemeGradientDark.R,
                                          ThemeManager.ThemeGradientDark.G,
                                          ThemeManager.ThemeGradientDark.B);
            ThemeColorLight = Color.FromArgb(255,
                                            (byte)Math.Min(255, ThemeManager.ThemeColor.R + 30),
                                            (byte)Math.Min(255, ThemeManager.ThemeColor.G + 30),
                                            (byte)Math.Min(255, ThemeManager.ThemeColor.B + 30));

            // Set border brush
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(ThemeManager.ThemeColor, 0),
                    new GradientStop(ThemeManager.ThemeColorDark, 0.5),
                    new GradientStop(ThemeManager.ThemeColor, 1)
                },
                Opacity = 0.8
            };
        }

        public void UpdateThemeColors()
        {
            UpdateColors();
            OnPropertyChanged(nameof(IconColor));
            OnPropertyChanged(nameof(ThemeColorMid));
            OnPropertyChanged(nameof(ThemeColorEnd));
            OnPropertyChanged(nameof(ThemeColorLight));
            OnPropertyChanged(nameof(BorderBrush));
        }

        public bool IsExpired(DateTime now) => now >= expiryTime && !isRemoving;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum NoticeType
    {
        Info,
        Success,
        Warning,
        Error
    }
}