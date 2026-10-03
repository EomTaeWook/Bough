using System;
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
using Dignus.DependencyInjection;
using Dignus.DependencyInjection.Extensions;

namespace Bough.App
{
    public partial class App : Application
    {
        private ServiceContainer _serviceContainer;

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
                _serviceContainer.RegisterType<TerminalLauncher, TerminalLauncher>(LifeScope.Singleton);
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
                desktop.MainWindow = new MainWindow(viewModel, stringHelper, errorLocalizer, operationQueue, clonePresenter, settingsService,
                    createRemoteOperationSession);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
