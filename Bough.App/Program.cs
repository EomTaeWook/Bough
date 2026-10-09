using Avalonia;
using Dignus.Log;
using Dignus.Log.Model;
using System;
using System.IO;
using System.Globalization;
using Bough.Core.Updates;

namespace Bough.App
{
    internal static class Program
    {
        internal static string UpdateStartupErrorCode { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
            // Updater helper runs from a temporary copy of the same published single executable.
            // It does not initialize Avalonia, user settings, Git services, or the logger.
            if (args.Length > 0)
            {
                if (args[0] == "--bough-update-helper")
                {
                    if (args.Length != 2)
                    {
                        Environment.ExitCode = 1;
                        return;
                    }
                    Environment.ExitCode = ApplicationUpdateInstaller.RunHelperAsync(args[1]).GetAwaiter().GetResult();
                    return;
                }
                if (args[0] == "--bough-update-cleanup")
                {
                    if (args.Length >= 3)
                    {
                        if (int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int helperPid))
                        {
                            _ = ApplicationUpdateInstaller.CleanupAfterHelperAsync(args[1], helperPid);
                        }
                    }
                    if (args.Length == 4)
                    {
                        // Only locally generated stable failure codes are accepted as startup input.
                        if (args[3] == "AppUpdateCoreReplaceFailed" || args[3] == "AppUpdateLaunchFailed")
                        {
                            UpdateStartupErrorCode = args[3];
                        }
                    }
                    args = Array.Empty<string>();
                }
            }
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            if (Application.Current is App app)
            {
                // UI lifetime has finished; disk authorization work does not block UI input.
                app.CompleteUpdateShutdownAsync().GetAwaiter().GetResult();
            }
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
