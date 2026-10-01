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

        public string ValidateDestination(string remote, string destinationPath)
        {
            return _cloneService.ValidateDestination(remote, destinationPath);
        }

        public Task<string> CloneAsync(string remote, string destinationPath,
            string operationName, IProgress<int> progress, Action started, CancellationToken cancellationToken)
        {
            string destination = _cloneService.ValidateDestination(remote, destinationPath);
            return _operationQueue.EnqueueAsync(destination, operationName,
                token => _cloneService.CloneAsync(remote, destination, progress, token, started),
                cancellationToken);
        }
    }
}
