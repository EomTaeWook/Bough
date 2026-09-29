using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public delegate Task<bool> ReferenceBranchRunner(Func<Task<GitRepository>> action);

    public class ReferenceMutationPresenter
    {
        private readonly GitReferenceService _referenceService;
        private readonly GitCommitActionService _actionService;
        private readonly GitOperationQueue _operationQueue;

        public ReferenceMutationPresenter(GitReferenceService referenceService, GitCommitActionService actionService,
            GitOperationQueue operationQueue)
        {
            _referenceService = referenceService ?? throw new ArgumentNullException(nameof(referenceService));
            _actionService = actionService ?? throw new ArgumentNullException(nameof(actionService));
            _operationQueue = operationQueue ?? throw new ArgumentNullException(nameof(operationQueue));
        }

        public Task<bool> DeleteLocalBranchAsync(GitRepository repository, GitLocalBranch branch, string operationName,
            Func<Task<bool>> applyResult)
        {
            return RunDeletionAsync(repository, operationName,
                token => _referenceService.DeleteLocalBranchAsync(repository, branch, token), applyResult);
        }

        public Task<bool> DeleteRemoteBranchAsync(GitRepository repository, GitRemoteBranch branch, string operationName,
            Func<Task<bool>> applyResult)
        {
            return RunDeletionAsync(repository, operationName,
                token => _referenceService.DeleteRemoteBranchAsync(repository, branch, token), applyResult);
        }

        public Task<bool> CreateTagAsync(GitRepository repository, string name, string operationName,
            Func<Task<bool>> applyResult)
        {
            return _operationQueue.EnqueueAsync(repository.RootPath, operationName, async token =>
            {
                token.ThrowIfCancellationRequested();
                await _referenceService.CreateLightweightTagAsync(repository, name, "HEAD");
                return await applyResult();
            });
        }

        public Task<bool> CreateFromBranchAsync(GitRepository repository, GitLocalBranch branch, string newName,
            string operationName, ReferenceBranchRunner runAndApply)
        {
            return RunBranchChangeAsync(repository, operationName,
                () => _actionService.CreateFromBranchAsync(repository, branch.Name, branch.CommitHash, newName, true), runAndApply);
        }

        public Task<bool> TrackRemoteAsync(GitRepository repository, GitRemoteBranch branch, string localName,
            string operationName, ReferenceBranchRunner runAndApply)
        {
            return RunBranchChangeAsync(repository, operationName,
                () => _actionService.TrackRemoteAsync(repository, branch, localName), runAndApply);
        }

        public Task<bool> CreateBranchAsync(GitRepository repository, string name, string operationName,
            ReferenceBranchRunner runAndApply)
        {
            return RunBranchChangeAsync(repository, operationName,
                () => _referenceService.CreateBranchAsync(repository, name, "HEAD"), runAndApply);
        }

        private Task<bool> RunDeletionAsync(GitRepository repository, string operationName,
            Func<CancellationToken, Task> action, Func<Task<bool>> applyResult)
        {
            return _operationQueue.EnqueueAsync(repository.RootPath, operationName, async token =>
            {
                token.ThrowIfCancellationRequested();
                await action(token);
                return await applyResult();
            });
        }

        private Task<bool> RunBranchChangeAsync(GitRepository repository, string operationName,
            Func<Task<GitRepository>> action, ReferenceBranchRunner runAndApply)
        {
            return _operationQueue.EnqueueAsync(repository.RootPath, operationName, async token =>
            {
                token.ThrowIfCancellationRequested();
                return await runAndApply(action);
            });
        }
    }
}
