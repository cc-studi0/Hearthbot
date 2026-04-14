using System;
using System.ComponentModel;
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
        private MainViewModel _logViewModel;
        private bool _logAutoFollow = true;
        private bool _restoringLogView;
        private int _logPinnedFirstVisibleLine = -1;
        private const double LogBottomTolerance = 2.0;

        public MainWindow()
        {
            GuiBridge.Install();
            InitializeComponent();

            LogBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnLogScrollChanged));
            DataContextChanged += OnDataContextChanged;
            AttachLogViewModel(DataContext as MainViewModel);

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

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachLogViewModel(e.OldValue as MainViewModel);
            AttachLogViewModel(e.NewValue as MainViewModel);
        }

        private void AttachLogViewModel(MainViewModel viewModel)
        {
            if (viewModel == null)
                return;

            _logViewModel = viewModel;
            _logViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyLogText(_logViewModel.LogText);
        }

        private void DetachLogViewModel(MainViewModel viewModel)
        {
            if (viewModel == null)
                return;

            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            if (ReferenceEquals(_logViewModel, viewModel))
                _logViewModel = null;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.LogText) || sender is not MainViewModel viewModel)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ApplyLogText(viewModel.LogText)), DispatcherPriority.Background);
                return;
            }

            ApplyLogText(viewModel.LogText);
        }

        private void ApplyLogText(string nextText)
        {
            var plan = LogTextSyncPlanner.Build(LogBox.Text, nextText);
            if (plan.Mode == LogTextSyncMode.None)
                return;

            var pinnedLine = _logAutoFollow
                ? -1
                : (_logPinnedFirstVisibleLine >= 0 ? _logPinnedFirstVisibleLine : LogBox.GetFirstVisibleLineIndex());
            var selectionStart = LogBox.SelectionStart;
            var selectionLength = LogBox.SelectionLength;
            var hasSelection = selectionLength > 0;

            _restoringLogView = true;
            try
            {
                switch (plan.Mode)
                {
                    case LogTextSyncMode.Append:
                        LogBox.AppendText(plan.Text);
                        break;

                    case LogTextSyncMode.Replace:
                        LogBox.Text = plan.Text;
                        break;
                }

                if (_logAutoFollow)
                {
                    _logPinnedFirstVisibleLine = -1;
                    LogBox.ScrollToEnd();
                    return;
                }

                if (hasSelection)
                {
                    var textLength = LogBox.Text?.Length ?? 0;
                    var safeStart = Math.Max(0, Math.Min(selectionStart, textLength));
                    var safeLength = Math.Max(0, Math.Min(selectionLength, textLength - safeStart));
                    if (safeLength > 0)
                        LogBox.Select(safeStart, safeLength);
                }

                if (pinnedLine >= 0 && LogBox.LineCount > 0)
                    LogBox.ScrollToLine(Math.Min(pinnedLine, LogBox.LineCount - 1));

                _logPinnedFirstVisibleLine = pinnedLine;
            }
            finally
            {
                _restoringLogView = false;
            }
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
