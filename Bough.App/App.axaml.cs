using System;
using System.Reflection;
using System.Threading.Tasks;
using Bough.App.Interfaces;
using Bough.Core.Updates;
using Bough.Core.Updates.Models;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Bough.App.Appearance;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.App.Persistence;
using Bough.App.ViewModels;
using Bough.Core.Conflicts;
using Bough.Core.Git;
using Bough.Core.Interfaces;
using Dignus.DependencyInjection;
using Dignus.DependencyInjection.Extensions;

namespace Bough.App
{
    public partial class App : Application
    {
        private ServiceContainer _serviceContainer;
        private ApplicationUpdateRestartPresenter _updateRestart;

        internal StringHelper Strings { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            IClassicDesktopStyleApplicationLifetime desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null)
            {
                LanguageSettingsStore languageSettings = new();
                _serviceContainer = new ServiceContainer();
                _serviceContainer.RegisterType(languageSettings);
                _serviceContainer.RegisterType(new StringLanguageSelection(languageSettings.SelectedLanguage));
                AppearanceThemeService appearanceTheme = new(this);
                _serviceContainer.RegisterType(appearanceTheme);
                _serviceContainer.RegisterType(GitExecutableSettings.Default);
                _serviceContainer.RegisterType(CreateUpdateEnvironment());
                _serviceContainer.RegisterType<ApplicationUpdateService, ApplicationUpdateService>(LifeScope.Singleton);
                _serviceContainer.RegisterType<ApplicationUpdateInstaller, ApplicationUpdateInstaller>(LifeScope.Singleton);
                _serviceContainer.RegisterType<ApplicationUpdateRestartPresenter, ApplicationUpdateRestartPresenter>(LifeScope.Singleton);
                _serviceContainer.RegisterType<IApplicationUpdateRestart>(
                    provider => (ApplicationUpdateRestartPresenter)provider.GetService(typeof(ApplicationUpdateRestartPresenter)),
                    LifeScope.Singleton);
                _serviceContainer.RegisterType<GitOperationQueue, GitOperationQueue>(LifeScope.Singleton);
                _serviceContainer.RegisterType<GitHistoryService, GitHistoryService>(LifeScope.Singleton);
                _serviceContainer.RegisterType<GitReferenceService, GitReferenceService>(LifeScope.Singleton);
                _serviceContainer.RegisterType<GitCommitActionService, GitCommitActionService>(LifeScope.Singleton);
                _serviceContainer.RegisterType<GitWorkingTreeService, GitWorkingTreeService>(LifeScope.Singleton);
                _serviceContainer.RegisterType<GitStashService, GitStashService>(LifeScope.Singleton);
                _serviceContainer.RegisterType<GitRemoteOperationService, GitRemoteOperationService>(LifeScope.Singleton);
                _serviceContainer.RegisterType<RemoteOperationsViewModel, RemoteOperationsViewModel>(LifeScope.Transient);
                _serviceContainer.RegisterType<GitSettingsService>(
                    provider => new GitSettingsService(
                        (GitCommandRunner)provider.GetService(typeof(GitCommandRunner)),
                        (GitExecutableSettings)provider.GetService(typeof(GitExecutableSettings))),
                    LifeScope.Singleton);
                _serviceContainer.RegisterType<GitHubAccountService, GitHubAccountService>(LifeScope.Singleton);
                _serviceContainer.RegisterType<GitSettingsViewModel, GitSettingsViewModel>(LifeScope.Singleton);
                _serviceContainer.RegisterType<GitCommitInspectionService, GitCommitInspectionService>(LifeScope.Singleton);
                _serviceContainer.RegisterType<GitCommitMessageService, GitCommitMessageService>(LifeScope.Singleton);
                _serviceContainer.RegisterType<GitCommitFileActionService, GitCommitFileActionService>(LifeScope.Singleton);
                RegisterTerminalLauncher();
                _serviceContainer.RegisterType<RepositoryFolderLauncher, RepositoryFolderLauncher>(LifeScope.Singleton);
                _serviceContainer.RegisterType<PullRequestLauncher, PullRequestLauncher>(LifeScope.Singleton);
                _serviceContainer.RegisterType<ConflictParser, ConflictParser>(LifeScope.Singleton);
                _serviceContainer.RegisterType<RepositoryListStore, RepositoryListStore>(LifeScope.Singleton);
                _serviceContainer.RegisterDependencies(typeof(GitCommandRunner).Assembly);
                _serviceContainer.RegisterDependencies(typeof(StringHelper).Assembly);

                IServiceProvider serviceProvider = _serviceContainer.Build();

                StringHelper stringHelper = serviceProvider.GetService<StringHelper>();
                Strings = stringHelper;
                GitErrorLocalizer errorLocalizer = serviceProvider.GetService<GitErrorLocalizer>();
                GitOperationQueue operationQueue = serviceProvider.GetService<GitOperationQueue>();
                CloneRepositoryPresenter clonePresenter = serviceProvider.GetService<CloneRepositoryPresenter>();
                GitSettingsService settingsService = serviceProvider.GetService<GitSettingsService>();
                MainWindowViewModel viewModel = serviceProvider.GetService<MainWindowViewModel>();
                Func<RemoteOperationsViewModel> createRemoteOperationSession = () => serviceProvider.GetService<RemoteOperationsViewModel>();
                MainWindow mainWindow = new(viewModel, stringHelper, errorLocalizer, operationQueue, clonePresenter, settingsService,
                    createRemoteOperationSession);
                _updateRestart = serviceProvider.GetService<ApplicationUpdateRestartPresenter>();
                _updateRestart.BindWindow(mainWindow);
                if (Program.UpdateStartupErrorCode != null)
                {
                    viewModel.ReportApplicationUpdateStartupFailure(Program.UpdateStartupErrorCode);
                }
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }

        internal async Task CompleteUpdateShutdownAsync()
        {
            if (_updateRestart == null)
            {
                return;
            }
            try
            {
                await _updateRestart.CompleteShutdownAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The helper will not replace without authorization. Keep the original executable.
                // Do not route updater network/path data to the global exception logger.
            }
        }

        private static ApplicationUpdateEnvironment CreateUpdateEnvironment()
        {
            Assembly assembly = typeof(App).Assembly;
            AssemblyInformationalVersionAttribute version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string current = string.Empty;
            if (version != null)
            {
                current = version.InformationalVersion;
            }
            bool published = false;
            foreach (AssemblyMetadataAttribute metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (metadata.Key == "BoughPublishedSingleFile")
                {
                    published = metadata.Value == "true";
                }
            }
            string executable = Environment.ProcessPath ?? string.Empty;
            return new ApplicationUpdateEnvironment(current, executable, published);
        }

        private void RegisterTerminalLauncher()
        {
            if (OperatingSystem.IsWindows() == true)
            {
                _serviceContainer.RegisterType<ITerminalLauncher, WindowsTerminalLauncher>(LifeScope.Singleton);
                return;
            }
            if (OperatingSystem.IsMacOS() == true)
            {
                _serviceContainer.RegisterType<ITerminalLauncher, MacOsTerminalLauncher>(LifeScope.Singleton);
                return;
            }
            if (OperatingSystem.IsLinux() == true)
            {
                _serviceContainer.RegisterType<ITerminalLauncher, LinuxTerminalLauncher>(LifeScope.Singleton);
                return;
            }

            _serviceContainer.RegisterType<ITerminalLauncher, UnsupportedTerminalLauncher>(LifeScope.Singleton);
        }
    }
}
