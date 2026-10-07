using Bough.App.Internals;
using System;
using System.Threading.Tasks;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;

namespace Bough.App.Presenters
{
    public class GitExecutablePresenter
    {
        private readonly GitSettingsService _settingsService;
        private int _requestVersion;

        public GitExecutablePresenter(GitSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void Invalidate()
        {
            _requestVersion++;
        }

        public Task TestAsync(GitSettingsViewModel screen)
        {
            return ExecuteAsync(screen, GitExecutableActionKind.Verified);
        }

        public Task SaveAsync(GitSettingsViewModel screen)
        {
            return ExecuteAsync(screen, GitExecutableActionKind.Saved);
        }

        private async Task ExecuteAsync(GitSettingsViewModel screen, GitExecutableActionKind kind)
        {
            string candidatePath = screen.GitPathInput;
            int requestVersion = ++_requestVersion;
            screen.BeginGitPathAction();
            try
            {
                try
                {
                    string version;
                    if (kind == GitExecutableActionKind.Saved)
                    {
                        version = await _settingsService.SaveGitPathAsync(candidatePath);
                    }
                    else
                    {
                        version = await _settingsService.TestGitAsync(candidatePath);
                    }

                    if (requestVersion != _requestVersion)
                    {
                        return;
                    }

                    screen.ApplyGitPathAction(new GitExecutableActionResult(kind, version, _settingsService.ConfiguredGitPath, null));
                }
                catch (Exception exception)
                {
                    if (requestVersion != _requestVersion)
                    {
                        return;
                    }

                    screen.ApplyGitPathAction(new GitExecutableActionResult(kind, string.Empty, string.Empty, exception));
                }
            }
            finally
            {
                screen.EndGitPathAction();
            }
        }
    }
}
