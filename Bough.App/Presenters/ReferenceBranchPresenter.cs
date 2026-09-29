using System;
using System.Threading.Tasks;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class ReferenceBranchPresenter
    {
        private readonly GitReferenceService _referenceService;
        private readonly GitOperationQueue _operationQueue;

        public ReferenceBranchPresenter(GitReferenceService referenceService, GitOperationQueue operationQueue)
        {
            _referenceService = referenceService ?? throw new ArgumentNullException(nameof(referenceService));
            _operationQueue = operationQueue ?? throw new ArgumentNullException(nameof(operationQueue));
        }

        public GitOperationQueueState GetQueueState(string repositoryRoot)
        {
            return _operationQueue.GetState(repositoryRoot);
        }

        public Task<bool> SwitchAsync(GitRepository repository, string branchName, string operationName,
            Action onStarted, Func<GitRepository, Task<bool>> applyResult, Action onFinished)
        {
            return _operationQueue.EnqueueAsync(repository.RootPath, operationName, async token =>
            {
                onStarted();
                try
                {
                    GitRepository updated = await _referenceService.SwitchBranchAsync(repository, branchName, token);
                    return await applyResult(updated);
                }
                finally
                {
                    onFinished();
                }
            });
        }
    }
}
