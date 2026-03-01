using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BotMain
{
    public partial class MainWindow : Window
    {
        private GuiRenderer _guiRenderer;

        public MainWindow()
        {
            GuiBridge.Install();
            InitializeComponent();

            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.LogText))
                        LogBox.ScrollToEnd();
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
    }
}
