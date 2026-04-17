using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace BotMain
{
    public partial class MainWindow : Window
    {
        private GuiRenderer _guiRenderer;
        private MainViewModel _logViewModel;
        private bool _logAutoFollow = true;
        private const double LogBottomTolerance = 20.0;
        private const int MaxParagraphs = 500;

        // ── Light theme brushes (smartbot style) ──
        private static readonly Brush TimestampBrush  = Freeze(153, 153, 153);
        private static readonly Brush DefaultBrush    = Freeze(34, 34, 34);
        private static readonly Brush ErrorBrush      = Freeze(204, 0, 0);
        private static readonly Brush WinBrush        = Freeze(26, 122, 58);
        private static readonly Brush LossBrush       = Freeze(204, 85, 0);
        private static readonly Brush TurnBrush       = Freeze(0, 102, 204);
        private static readonly Brush PluginBrush     = Freeze(119, 51, 187);
        private static readonly Brush ReloggerBrush   = Freeze(0, 136, 136);
        private static readonly Brush ActionBrush     = Freeze(153, 119, 0);
        private static readonly Brush WarningBrush    = Freeze(204, 119, 0);
        private static readonly Brush HsAngBrush      = Freeze(14, 122, 122);
        private static readonly Brush StatusBrush     = Freeze(14, 138, 78);
        private static readonly Brush CompileBrush    = Freeze(42, 110, 142);
        private static readonly Brush DebugBrush      = Freeze(170, 170, 170);
        private static readonly Brush ProgressBrush   = Freeze(119, 119, 119);

        // ── Filter state ──
        private string _currentFilter = "all";
        private readonly List<int> _gameStartIndices = new();
        private int _totalParagraphs;

        public MainWindow()
        {
            GuiBridge.Install();
            InitializeComponent();

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

            // Wire up filter buttons
            LogFilterAll.Click         += (_, _) => ApplyFilter("all");
            LogFilterErrors.Click      += (_, _) => ApplyFilter("errors");
            LogFilterActions.Click      += (_, _) => ApplyFilter("actions");
            LogFilterCurrentGame.Click += (_, _) => ApplyFilter("currentgame");
            LogFilterLastGame.Click    += (_, _) => ApplyFilter("lastgame");
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _guiRenderer = new GuiRenderer(PluginCanvas);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) => _guiRenderer.Sync();
            timer.Start();
        }

        // ── ViewModel wiring ──

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachLogViewModel(e.OldValue as MainViewModel);
            AttachLogViewModel(e.NewValue as MainViewModel);
        }

        private void AttachLogViewModel(MainViewModel viewModel)
        {
            if (viewModel == null) return;
            _logViewModel = viewModel;
            _logViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void DetachLogViewModel(MainViewModel viewModel)
        {
            if (viewModel == null) return;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            if (ReferenceEquals(_logViewModel, viewModel))
                _logViewModel = null;
        }

        private string _lastProcessedLog = "";

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.LogText) || sender is not MainViewModel viewModel)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ProcessNewLogLines(viewModel.LogText)), DispatcherPriority.Background);
                return;
            }

            ProcessNewLogLines(viewModel.LogText);
        }

        // ── Core: incremental colored log rendering ──

        private void ProcessNewLogLines(string fullText)
        {
            if (string.IsNullOrEmpty(fullText)) return;

            // Find new portion
            string newPortion;
            if (fullText.Length > _lastProcessedLog.Length && fullText.StartsWith(_lastProcessedLog))
            {
                newPortion = fullText.Substring(_lastProcessedLog.Length);
            }
            else
            {
                // Full reset (truncation happened)
                LogBox.Document.Blocks.Clear();
                _gameStartIndices.Clear();
                _totalParagraphs = 0;
                newPortion = fullText;
            }
            _lastProcessedLog = fullText;

            if (string.IsNullOrEmpty(newPortion)) return;

            var lines = newPortion.Split('\n');
            var doc = LogBox.Document;
            bool wasAtBottom = IsLogAtBottom();

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrEmpty(line)) continue;

                var para = CreateLogParagraph(line);
                doc.Blocks.Add(para);
            }

            // Trim old paragraphs
            while (doc.Blocks.Count > MaxParagraphs)
                doc.Blocks.Remove(doc.Blocks.FirstBlock);

            if (_logAutoFollow || wasAtBottom)
            {
                _logAutoFollow = true;
                LogBox.ScrollToEnd();
            }
        }

        // ── Paragraph creation (smartbot style) ──

        private Paragraph CreateLogParagraph(string str)
        {
            var category = GetLogCategory(str);
            var brush = GetBrushForCategory(category);
            bool bold = category is "turn" or "win" or "loss" or "status";

            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 1) };

            // [HH:mm:ss] prefix gets timestamp color, rest gets category color
            if (str.StartsWith("[") && str.Length > 10 && str[9] == ']')
            {
                var timestamp = str.Substring(0, 10);
                var body = str.Substring(10).TrimEnd();

                paragraph.Inlines.Add(new Run(timestamp) { Foreground = TimestampBrush });

                if (bold)
                    paragraph.Inlines.Add(new Bold(new Run(body) { Foreground = brush }));
                else
                    paragraph.Inlines.Add(new Run(body) { Foreground = brush });
            }
            else if (string.IsNullOrWhiteSpace(str))
            {
                paragraph.Inlines.Add(new Run(" "));
                paragraph.FontSize = 4;
            }
            else if (bold)
            {
                paragraph.Inlines.Add(new Bold(new Run(str.TrimEnd()) { Foreground = brush }));
            }
            else
            {
                paragraph.Inlines.Add(new Run(str.TrimEnd()) { Foreground = brush });
            }

            // Track game boundaries
            if (str.Contains("[Game] Victory") || str.Contains("[Game] Defeat") ||
                str.Contains("<-------Won !-------->") || str.Contains("<-------Lost-------->"))
                _gameStartIndices.Add(_totalParagraphs);

            _totalParagraphs++;

            // Apply filter visibility
            if (!ShouldShowEntry(str, category, _totalParagraphs - 1))
                paragraph.Tag = "hidden";

            if (paragraph.Tag is "hidden")
            {
                paragraph.FontSize = 0.01;
                paragraph.Margin = new Thickness(0);
                paragraph.Padding = new Thickness(0);
            }

            return paragraph;
        }

        // ── Category detection (adapted for Hearthbot log format) ──

        private static string GetLogCategory(string str)
        {
            // ── Error ──
            if (str.Contains("[ERROR]") || str.Contains("Error compiling") || str.Contains("error:") ||
                str.Contains("failed:") || str.Contains("失败"))
                return "error";
            if (str.TrimStart().StartsWith("at ") || str.TrimStart().StartsWith("System.") ||
                str.Contains("Exception:") || str.Contains("NullReferenceException"))
                return "error";

            // ── Win / Loss ──
            if (str.Contains("[Game] Victory") || str.Contains("<-------Won !-------->"))
                return "win";
            if (str.Contains("[Game] Defeat") || str.Contains("<-------Lost-------->"))
                return "loss";
            if (str.Contains("[GameResult]"))
                return "status";

            // ── Turn ──
            if (str.Contains("New Turn") || str.Contains("End Turn"))
                return "turn";

            // ── AI / Lethal (action color) ──
            if (str.Contains("[AI]"))
                return "action";
            if (str.Contains("[Lethal]"))
                return "action";

            // ── HSBox integration ──
            if (str.Contains("[HSBox] Action") || str.Contains("[HSBox] >>") ||
                str.Contains("[HSAng] Action"))
                return "action";
            if (str.Contains("[HSBox-WARN]") || str.Contains("[HSAng-WARN]") ||
                str.Contains("警告") || str.Contains("⚠"))
                return "warning";
            if (str.Contains("[HSBox-DBG]") || str.Contains("[HSAng-DBG]") ||
                str.Contains("[HSAng] HSBox turn="))
                return "hsang";

            // ── Plugin ──
            if (str.Contains("[Plugin]"))
                return "plugin";

            // ── Restart / BG queue (relogger color) ──
            if (str.Contains("[Restart]") || str.Contains("[BG.AutoQueue]") || str.Contains("[BG]"))
                return "relogger";

            // ── Status ──
            if (str.Contains("Payload connected") || str.Contains("initialized") ||
                str.Contains("loaded") || str.Contains("Stopped.") ||
                str.Contains("Run config:") ||
                str.Contains("[Settings]") || str.Contains("[Deploy]") ||
                str.Contains("[自动更新]") || str.Contains("[更新]"))
                return "status";

            // ── Auto-concede ──
            if (str.Contains("[AutoConcedeAlt]") || str.Contains("[Limit]"))
                return "warning";

            // ── Notify ──
            if (str.Contains("[Notify]"))
                return "plugin";

            // ── Misc concede / action markers ──
            if (str.Contains("[CloseHs]"))
                return "relogger";
            if (str.Contains("--> "))
                return "action";

            // ── Debug ──
            if (str.Contains("[DEBUG]") || str.Contains("[BG-DBG]"))
                return "debug";

            // ── Progress ──
            if (str.Contains("loading :") && str.Contains("%"))
                return "progress";

            return "default";
        }

        // ── Brush selection (smartbot light theme colors) ──

        private static Brush GetBrushForCategory(string category) => category switch
        {
            "win"      => WinBrush,
            "turn"     => TurnBrush,
            "loss"     => LossBrush,
            "debug"    => DebugBrush,
            "error"    => ErrorBrush,
            "hsang"    => HsAngBrush,
            "status"   => StatusBrush,
            "plugin"   => PluginBrush,
            "action"   => ActionBrush,
            "warning"  => WarningBrush,
            "compile"  => CompileBrush,
            "relogger" => ReloggerBrush,
            "progress" => ProgressBrush,
            _          => DefaultBrush,
        };

        // ── Log filtering ──

        private bool ShouldShowEntry(string str, string category, int paragraphIndex)
        {
            if (_currentFilter == "all") return true;
            if (_currentFilter == "errors") return category == "error";
            if (_currentFilter == "actions")
                return category is "action" or "turn" or "win" or "loss";
            if (_currentFilter == "currentgame")
            {
                if (_gameStartIndices.Count == 0) return true;
                return paragraphIndex >= _gameStartIndices[^1];
            }
            if (_currentFilter == "lastgame")
            {
                if (_gameStartIndices.Count < 2) return false;
                var start = _gameStartIndices[^2];
                var end = _gameStartIndices[^1];
                return paragraphIndex >= start && paragraphIndex < end;
            }
            return true;
        }

        private void ApplyFilter(string filter)
        {
            _currentFilter = filter;

            // Bold the active filter button
            LogFilterAll.FontWeight         = filter == "all"         ? FontWeights.Bold : FontWeights.Normal;
            LogFilterErrors.FontWeight      = filter == "errors"      ? FontWeights.Bold : FontWeights.Normal;
            LogFilterActions.FontWeight     = filter == "actions"     ? FontWeights.Bold : FontWeights.Normal;
            LogFilterCurrentGame.FontWeight = filter == "currentgame" ? FontWeights.Bold : FontWeights.Normal;
            LogFilterLastGame.FontWeight    = filter == "lastgame"    ? FontWeights.Bold : FontWeights.Normal;

            // Re-evaluate visibility on all paragraphs
            int idx = 0;
            foreach (var block in LogBox.Document.Blocks)
            {
                if (block is Paragraph para)
                {
                    var text = GetParagraphText(para);
                    var cat = GetLogCategory(text);
                    bool show = ShouldShowEntry(text, cat, idx);

                    if (show)
                    {
                        para.Tag = null;
                        para.FontSize = 11;
                        para.Margin = new Thickness(0, 0, 0, 1);
                        para.Padding = new Thickness(0);
                    }
                    else
                    {
                        para.Tag = "hidden";
                        para.FontSize = 0.01;
                        para.Margin = new Thickness(0);
                        para.Padding = new Thickness(0);
                    }
                }
                idx++;
            }
        }

        private static string GetParagraphText(Paragraph para)
        {
            var text = new System.Text.StringBuilder();
            foreach (var inline in para.Inlines)
            {
                if (inline is Run run)
                    text.Append(run.Text);
                else if (inline is Bold b)
                    foreach (var inner in b.Inlines)
                        if (inner is Run r) text.Append(r.Text);
            }
            return text.ToString();
        }

        // ── Scroll tracking ──

        private bool IsLogAtBottom()
        {
            var sv = FindScrollViewer(LogBox);
            if (sv == null) return true;
            return sv.VerticalOffset + sv.ViewportHeight >= sv.ExtentHeight - LogBottomTolerance;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject obj)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is ScrollViewer sv) return sv;
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
