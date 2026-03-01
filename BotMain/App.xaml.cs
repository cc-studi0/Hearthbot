using System;
using System.Windows;
using System.Windows.Threading;

namespace BotMain
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        }

        private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.ToString(), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.ExceptionObject.ToString(), "致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
