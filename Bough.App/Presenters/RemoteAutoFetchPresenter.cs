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
        private bool _started;
        private bool _paused;
        private bool _stopped;

        public RemoteAutoFetchPresenter(MainWindowViewModel model, GitSettingsService settings, GitRemoteOperationService service, GitOperationQueue queue, StringHelper strings)
        {
            _model = model;
            _settings = settings;
            _service = service;
            _queue = queue;
            _strings = strings;
        }

        public void Start()
        {
            if (_started == true || _stopped == true)
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
            if (_started == false || _paused == true || _stopped == true)
            {
                return;
            }
            if (_consumer.IsCompleted == true)
            {
                _consumer = ConsumeAsync();
            }
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
            if (_stopped == true || _paused == true || _settings.AutoFetchEnabled == false)
            {
                return false;
            }
            if (generation != _generation)
            {
                return false;
            }
            return _model.IsCurrentRepositoryRequest(repository, request);
        }

        private async Task ConsumeAsync()
        {
            while (_stopped == false && _paused == false && _settings.AutoFetchEnabled == true)
            {
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
                    while (_model.IsRemoteLoading == true || _model.IsReferencesLoading == true || _model.IsLocalChangesLoading == true)
                    {
                        await Task.Delay(200, cancellation.Token);
                    }
                    using CancellationTokenSource checkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                    checkCancellation.CancelAfter(TimeSpan.FromSeconds(60));
                    try
                    {
                        await CheckAsync(repository, request, generation, checkCancellation.Token);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested == true)
                    {
                        throw;
                    }
                    catch (OperationCanceledException exception) when (checkCancellation.IsCancellationRequested == true)
                    {
                        if (IsCurrent(repository, request, generation) == true)
                        {
                            _model.RemoteOperations.AutoFetchError = new GitException("RemoteAutoFetchTimedOut", exception, Array.Empty<object>());
                        }
                    }
                    catch (Exception exception)
                    {
                        if (IsCurrent(repository, request, generation) == true)
                        {
                            _model.RemoteOperations.AutoFetchError = exception;
                        }
                    }
                    await Task.Delay(TimeSpan.FromMinutes(_settings.AutoFetchIntervalMinutes), cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested == true)
                {
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

        private async Task CheckAsync(GitRepository repository, int request, int generation, CancellationToken cancellationToken)
        {
            if (IsCurrent(repository, request, generation) == false)
            {
                return;
            }
            if (_model.IsBusy == true || _model.RemoteOperations.IsBusy == true || _model.RemoteOperations.IsLoading == true)
            {
                return;
            }
            if (_model.References.IsBusy == true || _model.LocalChanges.IsBusy == true || _model.LocalChanges.Stashes.IsBusy == true)
            {
                return;
            }
            if (_model.GitSettings.IsBusy == true || _model.GitSettings.IsRemoteOperationBusy == true || _model.History.IsLoading == true)
            {
                return;
            }
            GitRemoteState knownState = _model.RemoteOperations.RemoteState;
            if (knownState == null)
            {
                return;
            }
            if (knownState.HasUpstream == false || knownState.IsDetached == true)
            {
                return;
            }
            GitOperationQueueState queueState = _queue.GetState(repository.RootPath);
            if (queueState.IsRunning == true || queueState.PendingCount > 0)
            {
                return;
            }
            await _queue.EnqueueAsync(repository.RootPath, _strings.GetString("AutoFetchOperationName"), token => UiQueuedOperation.RunAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                if (IsCurrent(repository, request, generation) == false)
                {
                    return;
                }
                int readVersion = _model.RemoteOperations.ReadVersion;
                GitRemoteState state = await Task.Run(() => _service.GetStateAsync(repository, token), token);
                if (IsCurrent(repository, request, generation) == false)
                {
                    return;
                }
                if (state.HasUpstream == false || state.IsDetached == true)
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
                    // A cancelled or failed final read can follow a successful ref update.
                    _model.MarkHistoryReferencesDirty(repository);
                    throw;
                }
                if (result.UpdatedReferences.Count > 0 || result.RemovedReferences.Count > 0)
                {
                    _model.MarkHistoryReferencesDirty(repository);
                }
                if (result.FailedRemotes.Count > 0)
                {
                    GitFetchFailure failure = result.FailedRemotes[0];
                    throw new GitRemoteOperationException("RemoteAutoFetchRequestFailed", failure.Error, failure.ExitCode);
                }
                GitRemoteState refreshed = await Task.Run(() => _service.GetStateAsync(repository, token), token);
                if (IsCurrent(repository, request, generation) == false)
                {
                    return;
                }
                if (_model.RemoteOperations.ApplyAutoFetchState(repository, readVersion, refreshed) == false)
                {
                    return;
                }
                if (result.UpdatedReferences.Count == 0 && result.RemovedReferences.Count == 0)
                {
                    return;
                }
                await _model.References.RefreshAsync();
                if (IsCurrent(repository, request, generation) == false)
                {
                    return;
                }
                if (_model.IsHistoryView == true)
                {
                    await _model.LoadHistoryAsync(repository, request);
                }
            }), cancellationToken);
        }
    }
}
