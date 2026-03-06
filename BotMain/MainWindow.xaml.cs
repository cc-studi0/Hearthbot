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
        private bool _restoringLogView;
        private int _logPinnedFirstVisibleLine = -1;
        private const double LogBottomTolerance = 2.0;

        public MainWindow()
        {
            GuiBridge.Install();
            InitializeComponent();

            LogBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnLogScrollChanged));

            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(MainViewModel.LogText))
                        return;

                    var pinnedLine = _logPinnedFirstVisibleLine;
                    var selectionStart = LogBox.SelectionStart;
                    var selectionLength = LogBox.SelectionLength;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _restoringLogView = true;
                        try
                        {
                            if (_logAutoFollow)
                            {
                                _logPinnedFirstVisibleLine = -1;
                                LogBox.ScrollToEnd();
                                return;
                            }

                            var targetLine = pinnedLine >= 0
                                ? pinnedLine
                                : LogBox.GetFirstVisibleLineIndex();
                            if (targetLine < 0)
                                targetLine = 0;

                            if (LogBox.LineCount > 0)
                                LogBox.ScrollToLine(Math.Min(targetLine, LogBox.LineCount - 1));

                            var textLength = LogBox.Text?.Length ?? 0;
                            var safeStart = Math.Max(0, Math.Min(selectionStart, textLength));
                            var safeLength = Math.Max(0, Math.Min(selectionLength, textLength - safeStart));
                            LogBox.Select(safeStart, safeLength);
                        }
                        finally
                        {
                            _restoringLogView = false;
                        }
                    }), DispatcherPriority.Background);
                };
            }

            Loaded += OnLoaded;
            Closing += (_, _) =>
            {
                if (DataContext is MainViewModel viewModel)
                    viewModel.SaveSettings();
            };
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
            if (_restoringLogView)
                return;

            if (Math.Abs(e.VerticalChange) <= double.Epsilon)
                return;

            _logAutoFollow = IsLogAtBottom();
            _logPinnedFirstVisibleLine = _logAutoFollow
                ? -1
                : LogBox.GetFirstVisibleLineIndex();
        }

        private bool IsLogAtBottom()
        {
            var distance = LogBox.ExtentHeight - LogBox.ViewportHeight - LogBox.VerticalOffset;
            return distance <= LogBottomTolerance;
        }
    }
}
