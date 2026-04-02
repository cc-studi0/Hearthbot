using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace BotMain
{
    public partial class App : Application
    {
        public static bool IsPostUpdate { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            IsPostUpdate = e.Args.Contains("--post-update", StringComparer.OrdinalIgnoreCase);
            DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        }

        private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.ToString(), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            (MainWindow?.DataContext as IDisposable)?.Dispose();
            base.OnExit(e);
        }

        private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.ExceptionObject.ToString(), "致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
