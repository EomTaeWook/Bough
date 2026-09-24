using Avalonia;
using Dignus.Log;
using System;

namespace Bough.App
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Environment.CurrentDirectory = AppContext.BaseDirectory;
            LogBuilder.Configuration(LogConfigXmlReader.Load("DignusLog.config")).Build();
            TemplateDataLoader templateDataLoader = new();
            templateDataLoader.Load();

            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
        {
            Exception exception = eventArgs.ExceptionObject as Exception;
            if (exception == null)
            {
                LogHelper.Error($"Unhandled non-exception object. value:{eventArgs.ExceptionObject}");
                return;
            }

            LogHelper.Error(exception);
        }
    }
}
