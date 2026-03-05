using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BotMain
{
    public partial class MainWindow : Window
    {
        private GuiRenderer _guiRenderer;
        private bool _logAutoFollow = true;
        private const double LogBottomTolerance = 2.0;

        public MainWindow()
        {
            GuiBridge.Install();
            InitializeComponent();

            LogBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnLogScrollChanged));

            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.LogText))
                    {
                        if (_logAutoFollow)
                            LogBox.ScrollToEnd();
                    }
                };

            Loaded += OnLoaded;
            Closing += (_, _) => { if (DataContext is MainViewModel v) v.SaveSettings(); };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _guiRenderer = new GuiRenderer(PluginCanvas);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) => _guiRenderer.Sync();
            timer.Start();
        }

        private void OnLogScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 仅在滚动位置真实变化时更新自动跟随状态。
            // 纯粹由日志追加导致的高度变化（ExtentHeightChange）不改状态，避免误判。
            if (Math.Abs(e.VerticalChange) <= double.Epsilon)
                return;

            _logAutoFollow = IsLogAtBottom();
        }

        private bool IsLogAtBottom()
        {
            var distance = LogBox.ExtentHeight - LogBox.ViewportHeight - LogBox.VerticalOffset;
            return distance <= LogBottomTolerance;
        }
    }
}
