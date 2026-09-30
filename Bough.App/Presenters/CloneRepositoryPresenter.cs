using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Git;
using Dignus.DependencyInjection.Attributes;

namespace Bough.App.Presenters
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Transient)]
    public class CloneRepositoryPresenter
    {
        private readonly GitCloneService _cloneService;
        private readonly GitOperationQueue _operationQueue;

        public CloneRepositoryPresenter(GitCloneService cloneService, GitOperationQueue operationQueue)
        {
            _cloneService = cloneService;
            _operationQueue = operationQueue;
        }

        public string ValidateDestination(string remote, string parentPath, string folderName)
        {
            return _cloneService.ValidateDestination(remote, parentPath, folderName);
        }

        public Task<string> CloneAsync(string remote, string parentPath, string folderName,
            string operationName, IProgress<int> progress, Action started, CancellationToken cancellationToken)
        {
            string destination = _cloneService.ValidateDestination(remote, parentPath, folderName);
            return _operationQueue.EnqueueAsync(destination, operationName, async token =>
            {
                _cloneService.ValidateDestination(remote, parentPath, folderName);
                started?.Invoke();
                return await _cloneService.CloneAsync(remote, parentPath, folderName, progress, token);
            }, cancellationToken);
        }
    }
}
