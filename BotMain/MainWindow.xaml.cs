using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BotMain
{
    internal static class TextBoxExtensions
    {
        public static int GetFirstVisibleLineIndex(this TextBox textBox)
        {
            var charIndex = textBox.GetCharacterIndexFromPoint(new Point(0, 0), true);
            return charIndex >= 0 ? textBox.GetLineIndexFromCharacterIndex(charIndex) : 0;
        }
    }

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

                            if (pinnedLine >= 0 && LogBox.LineCount > 0)
                                LogBox.ScrollToLine(Math.Min(pinnedLine, LogBox.LineCount - 1));

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

            if (DataContext is MainViewModel vmInit && vmInit.WindowLeft.HasValue && vmInit.WindowTop.HasValue)
            {
                Left = vmInit.WindowLeft.Value;
                Top = vmInit.WindowTop.Value;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (DataContext is MainViewModel vmSize)
            {
                if (vmSize.WindowWidth.HasValue && vmSize.WindowWidth.Value > 0)
                    Width = vmSize.WindowWidth.Value;
                if (vmSize.WindowHeight.HasValue && vmSize.WindowHeight.Value > 0)
                    Height = vmSize.WindowHeight.Value;
            }

            Loaded += OnLoaded;
            Closing += (_, _) =>
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.WindowLeft = Left;
                    viewModel.WindowTop = Top;
                    viewModel.WindowWidth = Width;
                    viewModel.WindowHeight = Height;
                    viewModel.SaveSettings();
                }
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
