using System;
using System.Threading.Tasks;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.Presenters
{
    public class HistoryActionPresenter
    {
        private readonly GitCommitActionService _actionService;
        private readonly GitOperationQueue _operationQueue;

        public HistoryActionPresenter(GitCommitActionService actionService, GitOperationQueue operationQueue)
        {
            _actionService = actionService ?? throw new ArgumentNullException(nameof(actionService));
            _operationQueue = operationQueue ?? throw new ArgumentNullException(nameof(operationQueue));
        }

        public Task<GitResetPreview> GetResetPreviewAsync(GitRepository repository, string commitHash)
        {
            return _actionService.GetResetPreviewAsync(repository, commitHash);
        }

        public Task<bool> ResetAsync(GitRepository repository, GitResetPreview preview, GitResetMode mode, bool hardConfirmed,
            string operationName, Func<GitRepository, Task<bool>> applyResult)
        {
            return RunRepositoryActionAsync(repository, operationName,
                () => _actionService.ResetAsync(repository, preview, mode, hardConfirmed), applyResult);
        }

        public Task<bool> SwitchDetachedAsync(GitRepository repository, string commitHash, string operationName,
            Func<GitRepository, Task<bool>> applyResult)
        {
            return RunRepositoryActionAsync(repository, operationName,
                () => _actionService.SwitchDetachedAsync(repository, commitHash), applyResult);
        }

        public Task<bool> CreateBranchAsync(GitRepository repository, string commitHash, string branchName, bool switchToBranch,
            string operationName, Func<GitRepository, Task<bool>> applyResult)
        {
            return RunRepositoryActionAsync(repository, operationName,
                () => _actionService.CreateBranchAsync(repository, branchName, commitHash, switchToBranch), applyResult);
        }

        public Task<bool> CreateTagAsync(GitRepository repository, string commitHash, string tagName, string operationName,
            Func<Task<bool>> applyResult)
        {
            return _operationQueue.EnqueueAsync(repository.RootPath, operationName, async token =>
            {
                token.ThrowIfCancellationRequested();
                await _actionService.CreateLightweightTagAsync(repository, tagName, commitHash);
                return await applyResult();
            });
        }

        private Task<bool> RunRepositoryActionAsync(GitRepository repository, string operationName,
            Func<Task<GitRepository>> action, Func<GitRepository, Task<bool>> applyResult)
        {
            return _operationQueue.EnqueueAsync(repository.RootPath, operationName, async token =>
            {
                token.ThrowIfCancellationRequested();
                GitRepository updated = await action();
                return await applyResult(updated);
            });
        }
    }
}
