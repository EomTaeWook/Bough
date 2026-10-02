using Avalonia;
using Dignus.Log;
using Dignus.Log.Model;
using System;
using System.IO;

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
            LogBuilder.Configuration(LoadLogConfiguration()).Build();
            TemplateDataLoader templateDataLoader = new();
            templateDataLoader.Load();

            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
        }

        private static LogConfiguration LoadLogConfiguration()
        {
#if DEBUG
            string developmentConfig = Path.Combine(AppContext.BaseDirectory, "DignusLog.config");
            if (File.Exists(developmentConfig))
            {
                return LogConfigXmlReader.Load(developmentConfig);
            }
#endif
            // Dignus accepts a file path; this temporary file is only packaged input.
            string temporaryConfig = Path.GetTempFileName();
            try
            {
                using (Stream resource = PackagedResources.Open("Bough.DignusLog.config"))
                using (FileStream output = File.OpenWrite(temporaryConfig))
                {
                    resource.CopyTo(output);
                }
                return LogConfigXmlReader.Load(temporaryConfig);
            }
            finally
            {
                File.Delete(temporaryConfig);
            }
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
