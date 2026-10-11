using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Bough.App.Localization;
using Bough.App.Threading;
using Bough.App.ViewModels;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class RemoteAutoFetchPresenter
    {
        private readonly MainWindowViewModel _model;
        private readonly GitSettingsService _settings;
        private readonly GitRemoteOperationService _service;
        private readonly GitOperationQueue _queue;
        private readonly StringHelper _strings;
        private Task _consumer = Task.CompletedTask;
        private CancellationTokenSource _cancellation;
        private int _generation;
        private bool _consumerRunning;
        private bool _started;
        private bool _paused;
        private bool _stopped;

        public RemoteAutoFetchPresenter(MainWindowViewModel model, GitSettingsService settings,
            GitRemoteOperationService service, GitOperationQueue queue, StringHelper strings)
        {
            _model = model;
            _settings = settings;
            _service = service;
            _queue = queue;
            _strings = strings;
        }

        public void Start()
        {
            if (_started == true)
            {
                return;
            }
            if (_stopped == true)
            {
                return;
            }
            _started = true;
            _settings.AutoFetchSettingsChanged += OnSettingsChanged;
            ContextChanged();
        }

        public void ContextChanged()
        {
            _generation++;
            _cancellation?.Cancel();
            _model.RemoteOperations.AutoFetchError = null;
            if (_started == false)
            {
                return;
            }
            if (_paused == true)
            {
                return;
            }
            if (_stopped == true)
            {
                return;
            }
            if (_consumerRunning == true)
            {
                return;
            }
            _consumerRunning = true;
            _consumer = ConsumeAsync();
        }

        public async Task PauseAsync()
        {
            _paused = true;
            _generation++;
            _cancellation?.Cancel();
            await _consumer;
        }

        public void Resume()
        {
            if (_stopped == true)
            {
                return;
            }
            _paused = false;
            ContextChanged();
        }

        public void Stop()
        {
            _stopped = true;
            _settings.AutoFetchSettingsChanged -= OnSettingsChanged;
            _generation++;
            _cancellation?.Cancel();
        }

        private void OnSettingsChanged()
        {
            if (Dispatcher.UIThread.CheckAccess() == true)
            {
                ContextChanged();
                return;
            }
            Dispatcher.UIThread.Post(ContextChanged);
        }

        private bool IsCurrent(GitRepository repository, int request, int generation)
        {
            if (_stopped == true)
            {
                return false;
            }
            if (_paused == true)
            {
                return false;
            }
            if (_settings.AutoFetchEnabled == false)
            {
                return false;
            }
            if (generation != _generation)
            {
                return false;
            }
            return _model.IsCurrentRepositoryRequest(repository, request);
        }

        private bool CanApply(GitRepository repository, int request, int generation, int readVersion)
        {
            if (IsCurrent(repository, request, generation) == false)
            {
                return false;
            }
            if (readVersion != _model.RemoteOperations.ReadVersion)
            {
                return false;
            }
            if (ReferenceEquals(_model.RemoteOperations.CurrentRepository, repository) == false)
            {
                return false;
            }
            return true;
        }

        private async Task ConsumeAsync()
        {
            int scheduledGeneration = -1;
            try
            {
                while (true)
                {
                    if (_stopped == true)
                    {
                        return;
                    }
                    if (_paused == true)
                    {
                        return;
                    }
                    if (_settings.AutoFetchEnabled == false)
                    {
                        return;
                    }
                    GitRepository repository = _model.CurrentRepository;
                    if (repository == null)
                    {
                        return;
                    }
                    int request = _model.RepositoryRequestVersion;
                    int generation = _generation;
                    using CancellationTokenSource cancellation = new();
                    _cancellation = cancellation;
                    try
                    {
                        if (scheduledGeneration != generation)
                        {
                            while (HasInitialReads() == true)
                            {
                                await Task.Delay(200, cancellation.Token);
                            }
                            scheduledGeneration = generation;
                        }
                        int readVersion = _model.RemoteOperations.ReadVersion;
                        using CancellationTokenSource checkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                        checkCancellation.CancelAfter(TimeSpan.FromSeconds(60));
                        try
                        {
                            await CheckAsync(repository, request, generation, readVersion, checkCancellation.Token);
                        }
                        catch (OperationCanceledException) when (cancellation.IsCancellationRequested == true)
                        {
                            throw;
                        }
                        catch (OperationCanceledException exception) when (checkCancellation.IsCancellationRequested == true)
                        {
                            if (CanApply(repository, request, generation, readVersion) == true)
                            {
                                _model.RemoteOperations.AutoFetchError = new GitException("RemoteAutoFetchTimedOut", exception, Array.Empty<object>());
                            }
                        }
                        catch (Exception exception)
                        {
                            if (CanApply(repository, request, generation, readVersion) == true)
                            {
                                _model.RemoteOperations.AutoFetchError = exception;
                            }
                        }
                        await Task.Delay(TimeSpan.FromMinutes(_settings.AutoFetchIntervalMinutes), cancellation.Token);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested == true)
                    {
                        // Only this automatic lifetime is cancelled. User FIFO requests retain their tokens.
                    }
                    finally
                    {
                        if (ReferenceEquals(_cancellation, cancellation) == true)
                        {
                            _cancellation = null;
                        }
                    }
                }
            }
            finally
            {
                _consumerRunning = false;
            }
        }

        private bool HasInitialReads()
        {
            return _model.IsRemoteLoading == true || _model.IsReferencesLoading == true || _model.IsLocalChangesLoading == true;
        }

        private bool IsBusy()
        {
            if (HasInitialReads() == true)
            {
                return true;
            }
            if (_model.IsBusy == true)
            {
                return true;
            }
            if (_model.RemoteOperations.IsBusy == true)
            {
                return true;
            }
            if (_model.RemoteOperations.IsLoading == true)
            {
                return true;
            }
            if (_model.References.IsBusy == true)
            {
                return true;
            }
            if (_model.LocalChanges.IsBusy == true)
            {
                return true;
            }
            if (_model.LocalChanges.Stashes.IsBusy == true)
            {
                return true;
            }
            if (_model.GitSettings.IsBusy == true)
            {
                return true;
            }
            if (_model.GitSettings.IsRemoteOperationBusy == true)
            {
                return true;
            }
            if (_model.History.IsLoading == true)
            {
                return true;
            }
            if (_model.Conflicts.IsBusy == true)
            {
                return true;
            }
            if (_model.Conflicts.IsBatchStaging == true)
            {
                return true;
            }
            return false;
        }

        private async Task CheckAsync(GitRepository repository, int request, int generation, int readVersion, CancellationToken cancellationToken)
        {
            if (CanApply(repository, request, generation, readVersion) == false)
            {
                return;
            }
            if (IsBusy() == true)
            {
                return;
            }
            GitRemoteState knownState = _model.RemoteOperations.RemoteState;
            if (knownState == null)
            {
                return;
            }
            if (knownState.HasUpstream == false)
            {
                return;
            }
            if (knownState.IsDetached == true)
            {
                return;
            }
            GitOperationQueueState queueState = _queue.GetState(repository.RootPath);
            if (queueState.IsRunning == true)
            {
                return;
            }
            if (queueState.PendingCount > 0)
            {
                return;
            }
            await _queue.EnqueueAsync(repository.RootPath, _strings.GetString("AutoFetchOperationName"), token => UiQueuedOperation.RunAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                if (CanApply(repository, request, generation, readVersion) == false)
                {
                    return;
                }
                if (IsBusy() == true)
                {
                    return;
                }
                // A user request accepted between admission and execution takes precedence over this tick.
                if (_queue.GetState(repository.RootPath).PendingCount > 0)
                {
                    return;
                }
                GitRemoteState state = await Task.Run(() => _service.GetStateAsync(repository, token), token);
                token.ThrowIfCancellationRequested();
                if (CanApply(repository, request, generation, readVersion) == false)
                {
                    return;
                }
                if (IsBusy() == true)
                {
                    return;
                }
                if (state.HasUpstream == false)
                {
                    _model.RemoteOperations.ApplyAutoFetchState(repository, readVersion, state);
                    return;
                }
                if (state.IsDetached == true)
                {
                    _model.RemoteOperations.ApplyAutoFetchState(repository, readVersion, state);
                    return;
                }
                GitFetchResult result;
                try
                {
                    result = await Task.Run(() => _service.FetchAutomaticallyAsync(repository, state, token), token);
                }
                catch
                {
                    // A ref update may have succeeded before cancellation or a final read failure.
                    _model.MarkHistoryReferencesDirty(repository);
                    throw;
                }
                bool referencesChanged = result.UpdatedReferences.Count > 0 || result.RemovedReferences.Count > 0;
                if (referencesChanged == true)
                {
                    _model.MarkHistoryReferencesDirty(repository);
                }
                if (result.FailedRemotes.Count > 0)
                {
                    GitFetchFailure failure = result.FailedRemotes[0];
                    throw new GitRemoteOperationException("RemoteAutoFetchRequestFailed", failure.Error, failure.ExitCode);
                }
                token.ThrowIfCancellationRequested();
                GitRemoteState refreshed = await Task.Run(() => _service.GetStateAsync(repository, token), token);
                token.ThrowIfCancellationRequested();
                if (CanApply(repository, request, generation, readVersion) == false)
                {
                    return;
                }
                if (_model.RemoteOperations.ApplyAutoFetchState(repository, readVersion, refreshed) == false)
                {
                    return;
                }
                if (referencesChanged == false)
                {
                    return;
                }
                await _model.References.RefreshAsync(updateStatus: false);
                token.ThrowIfCancellationRequested();
                if (CanApply(repository, request, generation, readVersion) == false)
                {
                    return;
                }
                if (_model.IsHistoryView == true)
                {
                    await _model.LoadHistoryAsync(repository, request);
                    if (CanApply(repository, request, generation, readVersion) == false)
                    {
                        return;
                    }
                }
            }), cancellationToken);
        }
    }
}
