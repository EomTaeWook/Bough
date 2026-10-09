using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Git;
using Bough.Core.Git.Models;
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

        public Task<string> ValidateDestinationAsync(string remote, string destinationPath, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = _cloneService.ValidateDestination(remote, destinationPath);
                cancellationToken.ThrowIfCancellationRequested();
                return destination;
            }, cancellationToken);
        }

        public async Task<string> CloneAsync(string remote, string destinationPath,
            string operationName, IProgress<int> progress, Action started, CancellationToken cancellationToken)
        {
            string destination = await ValidateDestinationAsync(remote, destinationPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return await _operationQueue.EnqueueAsync(destination, operationName,
                token => Task.Run(() => _cloneService.CloneAsync(remote, destination, progress, token, started), token),
                cancellationToken);
        }

        public Task<GitCloneDestinationState> GetDestinationStateAsync(string destination)
        {
            return Task.Run(() => _cloneService.GetDestinationState(destination));
        }
    }
}
