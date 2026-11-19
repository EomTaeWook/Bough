using System;
using System.Threading.Tasks;
using Bough.App.Threading;
using Bough.App.ViewModels;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    internal class GitRemoteSettingsPresenter
    {
        private readonly GitHubAccountService _service;
        private readonly GitOperationQueue _queue;

        public GitRemoteSettingsPresenter(GitHubAccountService service, GitOperationQueue queue)
        {
            _service = service;
            _queue = queue;
        }

        public async Task RemovePushUrlAsync(GitSettingsViewModel screen, GitRepository repository, string remoteName, GitRemotePushUrlSettings settings, string operationName)
        {
            try
            {
                await _queue.EnqueueAsync(repository.RootPath, operationName, cancellationToken => UiQueuedOperation.RunAsync(async () =>
                {
                    await Task.Run(() => _service.RemovePushUrlAsync(repository, remoteName, settings, cancellationToken), cancellationToken);
                    await screen.RefreshAfterPushUrlRemovalAsync(repository, remoteName, cancellationToken);
                }));
            }
            catch (Exception exception)
            {
                screen.ShowPushUrlRemovalError(repository, exception);
            }
        }
    }
}
