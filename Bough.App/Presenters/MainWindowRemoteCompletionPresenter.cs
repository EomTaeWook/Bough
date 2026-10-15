using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using Bough.App.Localization;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class MainWindowRemoteCompletionPresenter
    {
        private readonly MainWindowViewModel _model;
        private readonly StringComparison _pathComparison;
        private CancellationTokenSource _tagPushCandidatesCancellation;
        private Task _tagPushCandidatesRead = Task.CompletedTask;
        private int _tagPushCandidatesVersion;
        private bool _tagPushCandidatesStarted;
        private bool _tagPushCandidatesStopped;
        private bool _tagPushCandidatesPaused;
        private bool _tagPushCandidatesRequested;
        private bool _tagPushCandidatesRunning;

        public MainWindowRemoteCompletionPresenter(MainWindowViewModel model)
        {
            _model = model;
            _pathComparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows() == true)
            {
                _pathComparison = StringComparison.OrdinalIgnoreCase;
            }
        }

        internal void StartTagPushCandidates()
        {
            if (_tagPushCandidatesStarted == true)
            {
                return;
            }
            if (_tagPushCandidatesStopped == true)
            {
                return;
            }
            _tagPushCandidatesStarted = true;
            _model.PropertyChanged += OnTagPushContextChanged;
            _model.RemoteOperations.PropertyChanged += OnTagPushRemoteStateChanged;
            _model.References.PropertyChanged += OnTagPushReferencesChanged;
            _model.References.TagPushCompleted += OnTagPushCompleted;
            RefreshTagPushCandidates();
        }

        internal void StopTagPushCandidates()
        {
            _tagPushCandidatesStopped = true;
            _model.PropertyChanged -= OnTagPushContextChanged;
            _model.RemoteOperations.PropertyChanged -= OnTagPushRemoteStateChanged;
            _model.References.PropertyChanged -= OnTagPushReferencesChanged;
            _model.References.TagPushCompleted -= OnTagPushCompleted;
            _tagPushCandidatesVersion++;
            _tagPushCandidatesRequested = false;
            _tagPushCandidatesCancellation?.Cancel();
        }

        internal async Task PauseTagPushCandidatesAsync()
        {
            _tagPushCandidatesPaused = true;
            _tagPushCandidatesVersion++;
            _tagPushCandidatesRequested = false;
            _tagPushCandidatesCancellation?.Cancel();
            _model.RemoteOperations.SetTagPushCandidates(null, null);
            await _tagPushCandidatesRead;
        }

        internal void ResumeTagPushCandidates()
        {
            if (_tagPushCandidatesStopped == true)
            {
                return;
            }
            _tagPushCandidatesPaused = false;
            RefreshTagPushCandidates();
        }

        private void OnTagPushContextChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            switch (eventArgs.PropertyName)
            {
                case nameof(MainWindowViewModel.HasRepository):
                case nameof(MainWindowViewModel.IsBusy):
                case nameof(MainWindowViewModel.IsReferencesLoading):
                case nameof(MainWindowViewModel.IsRemoteLoading):
                    RefreshTagPushCandidates();
                    break;
            }
        }

        private void OnTagPushRemoteStateChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            switch (eventArgs.PropertyName)
            {
                case nameof(RemoteOperationsViewModel.RemoteState):
                case nameof(RemoteOperationsViewModel.IsLoading):
                    RefreshTagPushCandidates();
                    break;
            }
        }

        private void OnTagPushReferencesChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName != nameof(ReferenceExplorerViewModel.IsBusy))
            {
                return;
            }
            RefreshTagPushCandidates();
        }

        private void OnTagPushCompleted(GitRepository repository, string remoteName)
        {
            if (Dispatcher.UIThread.CheckAccess() == false)
            {
                Dispatcher.UIThread.Post(() => OnTagPushCompleted(repository, remoteName));
                return;
            }
            if (_tagPushCandidatesStopped == true)
            {
                return;
            }
            if (repository == null)
            {
                return;
            }
            GitRepository currentRepository = _model.CurrentRepository;
            if (currentRepository == null)
            {
                return;
            }
            if (string.Equals(currentRepository.RootPath, repository.RootPath, _pathComparison) == false)
            {
                return;
            }
            int requestVersion = _model.RepositoryRequestVersion;
            if (_model.IsCurrentRepositoryRequest(currentRepository, requestVersion) == false)
            {
                return;
            }
            if (ReferenceEquals(_model.RemoteOperations.CurrentRepository, currentRepository) == false)
            {
                return;
            }
            if (_model.RemoteOperations.HasUpstream == false)
            {
                return;
            }
            if (_model.RemoteOperations.UpstreamRemote != remoteName)
            {
                return;
            }
            if (_model.RemoteOperations.Remotes.Contains(remoteName) == false)
            {
                return;
            }
            RefreshTagPushCandidates();
        }

        internal void RefreshTagPushCandidates()
        {
            if (Dispatcher.UIThread.CheckAccess() == false)
            {
                Dispatcher.UIThread.Post(RefreshTagPushCandidates);
                return;
            }
            if (_tagPushCandidatesStopped == true)
            {
                return;
            }
            _tagPushCandidatesVersion++;
            _tagPushCandidatesCancellation?.Cancel();
            _model.RemoteOperations.SetTagPushCandidates(null, null);
            _tagPushCandidatesRequested = true;
            if (_tagPushCandidatesPaused == true)
            {
                return;
            }
            if (_tagPushCandidatesRunning == true)
            {
                return;
            }
            _tagPushCandidatesRunning = true;
            _tagPushCandidatesRead = ReadTagPushCandidatesAsync();
        }

        private async Task ReadTagPushCandidatesAsync()
        {
            try
            {
                await Task.Yield();
                while (_tagPushCandidatesRequested == true)
                {
                    if (_tagPushCandidatesStopped == true)
                    {
                        return;
                    }
                    if (_tagPushCandidatesPaused == true)
                    {
                        return;
                    }
                    _tagPushCandidatesRequested = false;
                    GitRepository repository = _model.CurrentRepository;
                    if (repository == null)
                    {
                        continue;
                    }
                    if (_model.IsBusy == true)
                    {
                        continue;
                    }
                    if (_model.IsReferencesLoading == true)
                    {
                        continue;
                    }
                    if (_model.IsRemoteLoading == true)
                    {
                        continue;
                    }
                    if (_model.References.IsBusy == true)
                    {
                        continue;
                    }
                    if (_model.RemoteOperations.IsLoading == true)
                    {
                        continue;
                    }
                    if (_model.RemoteOperations.CanPushTo == false)
                    {
                        continue;
                    }
                    if (_model.RemoteOperations.HasUpstream == false)
                    {
                        continue;
                    }
                    string remoteName = _model.RemoteOperations.UpstreamRemote;
                    if (_model.RemoteOperations.Remotes.Contains(remoteName) == false)
                    {
                        continue;
                    }
                    int requestVersion = _model.RepositoryRequestVersion;
                    int readVersion = _model.RemoteOperations.ReadVersion;
                    int bindingVersion = _model.References.ReferenceBindingVersion;
                    int candidatesVersion = _tagPushCandidatesVersion;
                    if (IsCurrentTagPushCandidates(repository, remoteName, requestVersion, readVersion, bindingVersion, candidatesVersion) == false)
                    {
                        continue;
                    }
                    using CancellationTokenSource cancellation = new();
                    _tagPushCandidatesCancellation = cancellation;
                    try
                    {
                        IReadOnlyList<GitTag> candidates = await _model.References.GetTagPushCandidatesAsync(repository, remoteName, cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (IsCurrentTagPushCandidates(repository, remoteName, requestVersion, readVersion, bindingVersion, candidatesVersion) == false)
                        {
                            continue;
                        }
                        if (candidates == null)
                        {
                            continue;
                        }
                        _model.RemoteOperations.SetTagPushCandidates(candidates, null);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested == true)
                    {
                        // The next request waits for this read and its Git process to finish.
                    }
                    catch (Exception exception)
                    {
                        if (IsCurrentTagPushCandidates(repository, remoteName, requestVersion, readVersion, bindingVersion, candidatesVersion) == true)
                        {
                            _model.RemoteOperations.SetTagPushCandidates(null, new LocalizedText("RemoteTagPushCandidatesCheckFailed", new LocalizedText(exception)));
                        }
                    }
                    finally
                    {
                        if (ReferenceEquals(_tagPushCandidatesCancellation, cancellation) == true)
                        {
                            _tagPushCandidatesCancellation = null;
                        }
                    }
                }
            }
            finally
            {
                _tagPushCandidatesRunning = false;
            }
        }

        private bool IsCurrentTagPushCandidates(GitRepository repository, string remoteName, int requestVersion, int readVersion, int bindingVersion, int candidatesVersion)
        {
            if (_tagPushCandidatesStopped == true)
            {
                return false;
            }
            if (_tagPushCandidatesPaused == true)
            {
                return false;
            }
            if (candidatesVersion != _tagPushCandidatesVersion)
            {
                return false;
            }
            if (_model.IsCurrentRepositoryRequest(repository, requestVersion) == false)
            {
                return false;
            }
            if (ReferenceEquals(_model.RemoteOperations.CurrentRepository, repository) == false)
            {
                return false;
            }
            if (readVersion != _model.RemoteOperations.ReadVersion)
            {
                return false;
            }
            if (bindingVersion != _model.References.ReferenceBindingVersion)
            {
                return false;
            }
            if (_model.References.CurrentRepository == null)
            {
                return false;
            }
            if (string.Equals(_model.References.CurrentRepository.RootPath, repository.RootPath, _pathComparison) == false)
            {
                return false;
            }
            if (_model.RemoteOperations.HasUpstream == false)
            {
                return false;
            }
            if (_model.RemoteOperations.UpstreamRemote != remoteName)
            {
                return false;
            }
            return true;
        }

        public async Task CompleteAsync(GitRepository updated, RemoteOperationStateSnapshot snapshot,
            bool worktreeMayChange, int repositoryRequestVersion)
        {
            if (_model.TryAdoptRepository(updated, repositoryRequestVersion) == false)
            {
                return;
            }
            int request = repositoryRequestVersion;
            try
            {
                if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                {
                    return;
                }
                if (snapshot != null)
                {
                    _model.RemoteOperations.ApplyOperationStateSnapshot(snapshot);
                }
                _model.MarkHistoryReferencesDirty(updated);
                Task references = _model.References.SetRepositoryAsync(updated, propagateReadError: true);
                Task history = Task.CompletedTask;
                if (_model.IsHistoryView)
                {
                    if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                    {
                        return;
                    }
                    history = _model.LoadHistoryAsync(updated, request);
                }
                Task rebase = Task.CompletedTask;
                Task revert = Task.CompletedTask;
                if (worktreeMayChange)
                {
                    if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                    {
                        return;
                    }
                    rebase = _model.RefreshRebaseStateAsync(updated, request);
                    revert = _model.RefreshRevertStateAsync(updated, request);
                }
                Exception readError = null;
                try
                {
                    await Task.WhenAll(references, history, rebase, revert);
                }
                catch (Exception exception)
                {
                    readError = exception;
                }
                if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                {
                    return;
                }
                if (worktreeMayChange)
                {
                    try
                    {
                        await _model.RefreshLocalChangesAndConflictsAsync(updated, request, propagateReadError: true);
                    }
                    catch (Exception exception)
                    {
                        if (readError == null)
                        {
                            readError = exception;
                        }
                    }
                    if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                    {
                        return;
                    }
                }
                if (readError != null)
                {
                    ExceptionDispatchInfo.Capture(readError).Throw();
                }
            }
            catch (Exception exception)
            {
                if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                {
                    return;
                }
                _model.ReportRemoteCompletionFailure(exception, updated, request);
                throw;
            }
        }
    }
}
