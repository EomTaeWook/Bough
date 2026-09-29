using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Internals;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class RemoteOperationPresenter
    {
        private readonly GitOperationQueue _queue;
        private readonly GitRepository _repository;
        private readonly RemoteOperationsViewModel _session;
        private readonly Func<GitRepository, RemoteOperationStateSnapshot, bool, int, Task> _operationFinished;
        private readonly int _repositoryRequestVersion;
        private readonly string _branch;
        private readonly string _remote;
        private readonly bool _prune;
        private readonly StringComparison _pathComparison;

        public RemoteOperationPresenter(GitOperationQueue queue, GitRepository repository, RemoteOperationsViewModel session,
            int repositoryRequestVersion, string branch, string remote, bool prune,
            Func<GitRepository, RemoteOperationStateSnapshot, bool, int, Task> operationFinished)
        {
            _queue = queue;
            _repository = repository;
            _session = session;
            _repositoryRequestVersion = repositoryRequestVersion;
            _branch = branch;
            _remote = remote;
            _prune = prune;
            _operationFinished = operationFinished;
            _pathComparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                _pathComparison = StringComparison.OrdinalIgnoreCase;
            }

            _session.BindRepository(_repository);
            _session.SelectedRemote = _remote;
            _session.Prune = _prune;
        }

        public Task<bool> ExecuteAsync(RemoteOperationKind kind, bool fetchAll, string operationName, string target,
            Func<RemoteOperationsViewModel, Task<bool>> operation, bool worktreeMayChange, CancellationToken cancellationToken)
        {
            return _queue.EnqueueAsync(_repository.RootPath, $"{operationName} · {target}", async token =>
            {
                token.ThrowIfCancellationRequested();
                await _session.SetRepositoryAsync(_repository);
                token.ThrowIfCancellationRequested();
                _session.SelectedRemote = _remote;
                _session.Prune = _prune;
                ValidateSession(kind, fetchAll);
                try
                {
                    return await operation(_session);
                }
                finally
                {
                    GitRepository completedRepository = _session.CurrentRepository;
                    if (completedRepository != null)
                    {
                        if (string.Equals(completedRepository.RootPath, _repository.RootPath, _pathComparison))
                        {
                            if (_operationFinished != null)
                            {
                                await _operationFinished(completedRepository, _session.LatestOperationStateSnapshot,
                                    worktreeMayChange, _repositoryRequestVersion);
                            }
                        }
                    }
                }
            }, cancellationToken: cancellationToken);
        }

        private void ValidateSession(RemoteOperationKind kind, bool fetchAll)
        {
            GitRepository sessionRepository = _session.CurrentRepository;
            if (sessionRepository == null)
            {
                throw new GitException("RemoteQueueRepositoryUnavailable", null, Array.Empty<object>());
            }
            if (string.Equals(sessionRepository.RootPath, _repository.RootPath, _pathComparison) == false)
            {
                throw new GitException("RemoteQueueRepositoryChanged", null, Array.Empty<object>());
            }
            if (kind == RemoteOperationKind.Fetch)
            {
                if (fetchAll == false)
                {
                    if (_session.SelectedRemote != _remote)
                    {
                        throw new GitException("RemoteQueueFetchRemoteChanged", null, Array.Empty<object>());
                    }
                    if (_session.CanFetch == false)
                    {
                        throw new GitException("RemoteQueueFetchRemoteUnavailable", null, Array.Empty<object>());
                    }
                }
                if (fetchAll)
                {
                    if (_session.CanFetchAll == false)
                    {
                        throw new GitException("RemoteQueueFetchAllUnavailable", null, Array.Empty<object>());
                    }
                }
            }
            if (kind == RemoteOperationKind.Pull || kind == RemoteOperationKind.Push)
            {
                if (_session.CurrentBranchText != _branch)
                {
                    throw new GitException("RemoteQueueBranchChanged", null, Array.Empty<object>());
                }
            }
        }
    }
}
