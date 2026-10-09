using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Internals;
using Bough.App.Localization;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.Presenters
{
    public class RemoteOperationExecutionPresenter
    {
        private readonly RemoteOperationsViewModel _model;
        private readonly GitRemoteOperationService _service;
        private readonly GitRepositoryService _repositoryService;
        private readonly StringHelper _strings;
        private readonly GitErrorLocalizer _errors;
        private CancellationTokenSource _cancellation;
        private CancellationTokenSource _loadCancellation;
        private int _requestVersion;

        public RemoteOperationExecutionPresenter(RemoteOperationsViewModel model, GitRemoteOperationService service,
            GitRepositoryService repositoryService, StringHelper strings, GitErrorLocalizer errors)
        {
            _model = model;
            _service = service;
            _repositoryService = repositoryService;
            _strings = strings;
            _errors = errors;
        }

        public bool CanCancel
        {
            get { return _model.IsBusy && _cancellation != null && _cancellation.IsCancellationRequested == false; }
        }

        public async Task SetRepositoryAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            _model.BindRepository(repository);
            if (repository == null)
            {
                return;
            }
            await RefreshAsync(cancellationToken);
        }

        public async Task<bool> ExecuteRequestAsync(RemoteOperationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            await SetRepositoryAsync(request.Repository);
            if (_model.RemoteState == null)
            {
                _model.LastOperationOutcome = RemoteOperationOutcome.Failed;
                return false;
            }

            if (request.Kind != RemoteOperationKind.Fetch)
            {
                if (_model.RemoteState.BranchName != request.LocalBranch)
                {
                    _model.SetLocalizedStatusText(new LocalizedText("RemoteRequestedBranchChanged", request.LocalBranch, _model.RemoteState.BranchName));
                    _model.LastOperationOutcome = RemoteOperationOutcome.Failed;
                    return false;
                }
            }

            if (request.Kind == RemoteOperationKind.Fetch)
            {
                _model.SelectedRemote = request.Remote;
                _model.Prune = request.Prune;
                return await FetchAsync(request.FetchAll);
            }
            if (request.Kind == RemoteOperationKind.Pull)
            {
                return await PullAsync(request.PullStrategy, request.Remote, request.RemoteBranch);
            }

            if (await PrepareUpstreamPushAsync(request.Remote, request.RemoteBranch) == false)
            {
                if (_model.LastOperationOutcome == RemoteOperationOutcome.None)
                {
                    _model.LastOperationOutcome = RemoteOperationOutcome.Failed;
                }
                return false;
            }
            return await PushAsync(request.Remote, request.RemoteBranch, request.PushTargetConfirmed);
        }

        public void InvalidatePendingRequests()
        {
            _requestVersion++;
            _loadCancellation?.Cancel();
            _cancellation?.Cancel();
            _model.LatestOperationStateSnapshot = null;
            _model.IsBusy = false;
            _model.IsLoading = false;
        }

        public bool ApplyOperationStateSnapshot(RemoteOperationStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }
            if (_model.CurrentRepository == null)
            {
                return false;
            }
            if (_model.IsBusy)
            {
                return false;
            }
            StringComparison comparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                comparison = StringComparison.OrdinalIgnoreCase;
            }
            if (string.Equals(_model.CurrentRepository.RootPath, snapshot.Repository.RootPath, comparison) == false)
            {
                return false;
            }
            if (string.Equals(snapshot.Repository.RootPath, snapshot.State.RepositoryRoot, comparison) == false)
            {
                return false;
            }

            _requestVersion++;
            _loadCancellation?.Cancel();
            _model.IsLoading = false;
            _model.CurrentRepository = snapshot.Repository;
            _model.ApplyState(snapshot.State);
            _model.LatestOperationStateSnapshot = snapshot;
            _model.StatusText = string.Empty;
            return true;
        }

        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GitRepository repository = _model.CurrentRepository;
            if (repository == null)
            {
                return;
            }
            if (_model.IsBusy == true)
            {
                return;
            }
            _loadCancellation?.Cancel();
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loadCancellation = cancellation;
            int request = ++_requestVersion;
            _model.IsLoading = true;
            _model.SetLocalizedStatusText(new LocalizedText("RemoteLoadingState"));
            try
            {
                GitRemoteState state = await _service.GetStateAsync(repository, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (request != _requestVersion)
                {
                    return;
                }
                if (_model.CurrentRepository != repository)
                {
                    return;
                }
                _model.ApplyState(state);
                if (state.Remotes.Count == 0) { _model.SetLocalizedStatusText(new LocalizedText("RemoteNoRemotesConfigured")); }
                else if (state.IsDetached == true) { _model.SetLocalizedStatusText(new LocalizedText("RemoteDetachedHint")); }
                else if (state.HasUpstream == false) { _model.SetLocalizedStatusText(new LocalizedText("RemoteNoUpstreamHint")); }
                else { _model.SetLocalizedStatusText(new LocalizedText("RemoteStateRefreshed")); }
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception exception)
            {
                if (request == _requestVersion)
                {
                    _model.SetLocalizedStatusText(new LocalizedText(exception));
                }
            }
            finally
            {
                if (_loadCancellation == cancellation)
                {
                    _loadCancellation = null;
                }
                if (request == _requestVersion)
                {
                    _model.IsLoading = false;
                }
            }
        }

        public async Task<bool> FetchAsync(bool fetchAll)
        {
            string remote = _model.SelectedRemote;
            bool prune = _model.Prune;
            string progress = _strings.Format("RemoteFetchingFrom", remote);
            if (fetchAll == true)
            {
                progress = _strings.GetString("RemoteFetchingAll");
            }
            return await RunAsync(progress, async (repository, state, token) =>
            {
                GitFetchResult result = await _service.FetchAsync(repository, state, remote, fetchAll, prune, token);
                string succeeded = string.Join(", ", result.SucceededRemotes);
                List<string> changed = new(result.UpdatedReferences);
                foreach (string removed in result.RemovedReferences)
                {
                    changed.Add(_strings.Format("RemoteRemovedReference", removed));
                }
                string updated = _strings.GetString("RemoteNoReferenceChanges");
                if (changed.Count > 0)
                {
                    updated = string.Join(", ", changed);
                }
                string message = _strings.Format("RemoteFetchResult", succeeded, updated);
                if (result.FailedRemotes.Count > 0)
                {
                    List<string> failures = [];
                    foreach (GitFetchFailure failure in result.FailedRemotes)
                    {
                        string reason = failure.Error;
                        if (reason.Length == 0)
                        {
                            reason = _strings.Format("RemoteGitExitWithoutOutput", failure.ExitCode);
                        }
                        failures.Add(_strings.Format("RemoteFetchFailureItem", failure.Remote, reason));
                    }
                    message += _strings.Format("RemoteFetchFailures", string.Join("; ", failures));
                    if (result.SucceededRemotes.Count > 0)
                    {
                        return new OperationExecutionResult(_strings.Format("RemoteFetchFailedSummary", string.Join(", ", result.FailedRemotes.Select(failure => failure.Remote))), RemoteOperationOutcome.PartiallySucceeded, message);
                    }
                    return new OperationExecutionResult(_strings.Format("RemoteFetchFailedSummary", string.Join(", ", result.FailedRemotes.Select(failure => failure.Remote))), RemoteOperationOutcome.Failed, message);
                }
                return new OperationExecutionResult(message, RemoteOperationOutcome.Succeeded);
            });
        }

        public async Task<IReadOnlyList<string>> GetRemoteBranchesAsync(string remote)
        {
            GitRepository repository = _model.CurrentRepository;
            if (repository == null)
            {
                return Array.Empty<string>();
            }
            GitRemoteState state = _model.RemoteState;
            if (state == null)
            {
                return await _service.GetRemoteBranchesAsync(repository, remote);
            }
            return await _service.GetRemoteBranchesAsync(repository, state, remote);
        }

        public async Task ValidatePushTargetAsync(string remote, string branch)
        {
            GitRepository repository = _model.CurrentRepository;
            if (repository == null)
            {
                throw new GitException("RemoteSelectRepository", null, Array.Empty<object>());
            }
            GitRemoteState state = _model.RemoteState;
            if (state == null)
            {
                await _service.ValidatePushTargetAsync(repository, remote, branch);
                return;
            }
            await _service.ValidatePushTargetAsync(repository, state, remote, branch);
        }

        public async Task<bool> PullAsync(GitPullStrategy strategy, string remote, string branch)
        {
            int request = _requestVersion + 1;
            IProgress<GitPullProgress> progress = new Progress<GitPullProgress>(update => ApplyPullProgress(request, update));
            return await RunAsync(_strings.Format("RemotePullingFrom", remote, branch), async (repository, state, token) =>
            {
                await _service.PullWithProgressAsync(repository, state, remote, branch, strategy, progress, token);
                string resultText = _strings.Format("RemotePullCompleted", FormatStrategy(strategy), remote, branch);
                return new OperationExecutionResult(resultText, RemoteOperationOutcome.Succeeded);
            });
        }

        public async Task<bool> PushAsync(string remote, string branch, bool targetConfirmed)
        {
            return await RunAsync(_strings.Format("RemotePushingTo", remote, branch), async (repository, state, token) =>
            {
                bool pushed = await _service.PushAsync(repository, state, remote, branch, targetConfirmed, token);
                if (pushed == false)
                {
                    return new OperationExecutionResult(_strings.GetString("RemotePushNoNewCommits"), RemoteOperationOutcome.NoNewCommits);
                }
                return new OperationExecutionResult(_strings.Format("RemotePushCompleted", remote, branch), RemoteOperationOutcome.Succeeded);
            });
        }

        public async Task<bool> PrepareUpstreamPushAsync(string remote, string branch, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GitRepository repository = _model.CurrentRepository;
            GitRemoteState previous = _model.RemoteState;
            if (repository == null)
            {
                return false;
            }
            if (previous == null)
            {
                return false;
            }
            if (_model.IsBusy)
            {
                return false;
            }
            if (previous.HasUpstream == false)
            {
                return true;
            }
            if (remote != previous.UpstreamRemote)
            {
                return true;
            }
            if (branch != previous.UpstreamBranch)
            {
                return true;
            }

            _loadCancellation?.Cancel();
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loadCancellation = cancellation;
            int request = ++_requestVersion;
            _model.IsLoading = true;
            _model.SetLocalizedStatusText(new LocalizedText("RemoteCheckingPushState"));
            _model.LastOperationOutcome = RemoteOperationOutcome.None;
            try
            {
                GitRemoteState latest = await _service.GetStateAsync(repository, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (request != _requestVersion)
                {
                    return false;
                }
                if (_model.CurrentRepository != repository)
                {
                    return false;
                }

                _model.ApplyState(latest);
                if (latest.BranchName != previous.BranchName)
                {
                    _model.SetLocalizedStatusText(new LocalizedText("RemotePushBranchChanged"));
                    return false;
                }
                if (latest.UpstreamRemote != remote)
                {
                    _model.SetLocalizedStatusText(new LocalizedText("RemotePushUpstreamChanged"));
                    return false;
                }
                if (latest.UpstreamBranch != branch)
                {
                    _model.SetLocalizedStatusText(new LocalizedText("RemotePushUpstreamChanged"));
                    return false;
                }
                if (_model.HasNoOutgoingPushCommits)
                {
                    _model.StatusText = _model.PushAvailabilityText;
                    _model.LastOperationOutcome = RemoteOperationOutcome.NoNewCommits;
                    return false;
                }
                _model.SetLocalizedStatusText(new LocalizedText("RemotePushCommitsConfirmed"));
                return true;
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
            catch (Exception exception)
            {
                if (request == _requestVersion)
                {
                    _model.SetLocalizedStatusText(new LocalizedText(exception));
                }
                return false;
            }
            finally
            {
                if (_loadCancellation == cancellation)
                {
                    _loadCancellation = null;
                }
                if (request == _requestVersion)
                {
                    _model.IsLoading = false;
                }
            }
        }

        public void Cancel()
        {
            if (_loadCancellation != null)
            {
                if (_loadCancellation.IsCancellationRequested == false)
                {
                    _loadCancellation.Cancel();
                    _model.SetLocalizedStatusText(new LocalizedText("RemoteCancelRequested"));
                    _model.NotifyState();
                }
            }
            if (_model.IsBusy == false)
            {
                return;
            }
            if (_cancellation == null)
            {
                return;
            }
            if (_cancellation.IsCancellationRequested == true)
            {
                return;
            }
            _model.SetLocalizedStatusText(new LocalizedText("RemoteCancelRequested"));
            _cancellation.Cancel();
            _model.NotifyState();
        }

        internal void ReportCompletionFailure(Exception exception)
        {
            LocalizedText error = new(exception);
            string completedMessage = _model.StatusText;
            _model.SetLocalizedStatusText(new LocalizedText("RemoteStateRefreshFailed", completedMessage, error));
            if (_model.LastOperationOutcome == RemoteOperationOutcome.Succeeded)
            {
                _model.LastOperationOutcome = RemoteOperationOutcome.RefreshFailed;
            }
            _model.NotifyState();
        }

        private void ApplyPullProgress(int request, GitPullProgress progress)
        {
            if (request != _requestVersion)
            {
                return;
            }
            if (_model.IsBusy == false)
            {
                return;
            }
            if (progress.Stage == GitPullStage.Fetching)
            {
                _model.SetLocalizedOperationStageText(new LocalizedText("RemoteStageFetching"));
            }
            if (progress.Stage == GitPullStage.Inspecting)
            {
                _model.SetLocalizedOperationStageText(new LocalizedText("RemoteStageInspecting"));
            }
            if (progress.Stage == GitPullStage.Applying)
            {
                _model.SetLocalizedOperationStageText(new LocalizedText("RemoteStageApplying"));
            }
            if (progress.TransferStatus != null)
            {
                _model.SetLocalizedTransferStatusText(new LocalizedText(progress.TransferStatus.Key, progress.TransferStatus.Arguments.ToArray()));
            }
        }

        private async Task<bool> RunAsync(string progressText, Func<GitRepository, GitRemoteState, CancellationToken, Task<OperationExecutionResult>> operation)
        {
            GitRepository repository = _model.CurrentRepository;
            GitRemoteState state = _model.RemoteState;
            if (repository == null)
            {
                return false;
            }
            if (state == null)
            {
                return false;
            }
            if (_model.IsBusy == true)
            {
                return false;
            }
            _loadCancellation?.Cancel();
            _model.IsLoading = false;
            int request = ++_requestVersion;
            _model.LatestOperationStateSnapshot = null;
            using CancellationTokenSource cancellation = new();
            _cancellation = cancellation;
            _model.IsBusy = true;
            _model.StatusText = progressText;
            _model.LastOperationOutcome = RemoteOperationOutcome.Running;
            _model.OperationStageText = string.Empty;
            _model.TransferStatusText = string.Empty;
            _model.OperationDetailsText = string.Empty;
            OperationExecutionResult completedResult = null;
            try
            {
                completedResult = await operation(repository, state, cancellation.Token);
                if (request != _requestVersion)
                {
                    return false;
                }
                if (_model.CurrentRepository != repository)
                {
                    return false;
                }
                _model.OperationDetailsText = completedResult.Details;
                if (_model.OperationStageText.Length > 0)
                {
                    _model.SetLocalizedOperationStageText(new LocalizedText("RemoteStageCheckingState"));
                }
                GitRepository updated = await _repositoryService.OpenAsync(repository.RootPath, cancellation.Token);
                if (request != _requestVersion)
                {
                    return false;
                }
                if (ReferenceEquals(_model.CurrentRepository, repository) == false)
                {
                    return false;
                }
                GitRemoteState updatedState = await _service.GetStateAsync(updated, cancellation.Token);
                if (request != _requestVersion)
                {
                    return false;
                }
                if (_model.CurrentRepository != repository)
                {
                    return false;
                }
                _model.CurrentRepository = updated;
                _model.ApplyState(updatedState);
                _model.LatestOperationStateSnapshot = new RemoteOperationStateSnapshot(updated, updatedState);
                _model.StatusText = completedResult.Message;
                _model.LastOperationOutcome = completedResult.Outcome;
                if (completedResult.Outcome == RemoteOperationOutcome.NoNewCommits)
                {
                    _model.NotifyState();
                    return false;
                }
                if (_model.OperationStageText.Length > 0)
                {
                    _model.SetLocalizedOperationStageText(new LocalizedText("RemoteStageComplete"));
                }
                if (completedResult.Outcome == RemoteOperationOutcome.Succeeded || completedResult.Outcome == RemoteOperationOutcome.PartiallySucceeded)
                {
                    _model.NotifyOperationCompleted(updated);
                }
                if (completedResult.Outcome == RemoteOperationOutcome.Succeeded)
                {
                    _model.PullStrategiesRequested = false;
                }
                _model.NotifyState();
                return completedResult.Outcome == RemoteOperationOutcome.Succeeded;
            }
            catch (OperationCanceledException)
            {
                if (request == _requestVersion)
                {
                    if (ReferenceEquals(_model.CurrentRepository, repository) == false)
                    {
                        return false;
                    }
                    string refreshError = await RefreshAfterOutcomeAsync(repository, request);
                    if (request != _requestVersion)
                    {
                        return false;
                    }
                    if (_model.LatestOperationStateSnapshot == null)
                    {
                        if (ReferenceEquals(_model.CurrentRepository, repository) == false)
                        {
                            return false;
                        }
                    }
                    else if (ReferenceEquals(_model.CurrentRepository, _model.LatestOperationStateSnapshot.Repository) == false)
                    {
                        return false;
                    }
                    if (completedResult == null)
                    {
                        _model.SetLocalizedStatusText(new LocalizedText("RemoteOperationCanceled"));
                        _model.LastOperationOutcome = RemoteOperationOutcome.Canceled;
                    }
                    else
                    {
                        _model.SetLocalizedStatusText(new LocalizedText("RemoteStateCheckCanceled", completedResult.Message));
                        _model.LastOperationOutcome = RemoteOperationOutcome.RefreshFailed;
                    }
                    if (refreshError.Length > 0)
                    {
                        _model.StatusText += _strings.Format("RemoteRefreshFailedSuffix", refreshError);
                    }
                }
                return false;
            }
            catch (Exception exception)
            {
                if (request == _requestVersion)
                {
                    if (ReferenceEquals(_model.CurrentRepository, repository) == false)
                    {
                        return false;
                    }
                    string refreshError = await RefreshAfterOutcomeAsync(repository, request);
                    if (request != _requestVersion)
                    {
                        return false;
                    }
                    if (_model.LatestOperationStateSnapshot == null)
                    {
                        if (ReferenceEquals(_model.CurrentRepository, repository) == false)
                        {
                            return false;
                        }
                    }
                    else if (ReferenceEquals(_model.CurrentRepository, _model.LatestOperationStateSnapshot.Repository) == false)
                    {
                        return false;
                    }
                    _model.SetLocalizedStatusText(new LocalizedText(exception));
                    if (exception is GitRemoteOperationException remoteException)
                    {
                        _model.OperationDetailsText = remoteException.Details;
                    }
                    if (completedResult == null)
                    {
                        _model.LastOperationOutcome = RemoteOperationOutcome.Failed;
                    }
                    else
                    {
                        _model.SetLocalizedStatusText(new LocalizedText("RemoteStateRefreshFailed", completedResult.Message, _errors.GetDisplayMessage(exception)));
                        _model.LastOperationOutcome = RemoteOperationOutcome.RefreshFailed;
                    }
                    if (refreshError.Length > 0)
                    {
                        _model.StatusText += _strings.Format("RemoteRefreshFailedSuffix", refreshError);
                    }
                    if (IsPullDivergence(exception))
                    {
                        _model.PullStrategiesRequested = true;
                        _model.NotifyState();
                    }
                }
                return false;
            }
            finally
            {
                if (request == _requestVersion)
                {
                    _model.IsBusy = false;
                }
                if (_cancellation == cancellation)
                {
                    _cancellation = null;
                }
            }
        }

        private async Task<string> RefreshAfterOutcomeAsync(GitRepository repository, int request)
        {
            try
            {
                GitRepository updated = await _repositoryService.OpenAsync(repository.RootPath);
                if (request != _requestVersion)
                {
                    return string.Empty;
                }
                if (ReferenceEquals(_model.CurrentRepository, repository) == false)
                {
                    return string.Empty;
                }
                GitRemoteState state = await _service.GetStateAsync(updated);
                if (request != _requestVersion)
                {
                    return string.Empty;
                }
                if (_model.CurrentRepository != repository)
                {
                    return string.Empty;
                }
                _model.CurrentRepository = updated;
                _model.ApplyState(state);
                _model.LatestOperationStateSnapshot = new RemoteOperationStateSnapshot(updated, state);
                _model.NotifyOperationCompleted(updated);
                return string.Empty;
            }
            catch (Exception exception)
            {
                if (request != _requestVersion)
                {
                    return string.Empty;
                }
                if (ReferenceEquals(_model.CurrentRepository, repository) == false)
                {
                    return string.Empty;
                }
                return _errors.GetDisplayMessage(exception);
            }
        }

        private string FormatStrategy(GitPullStrategy strategy)
        {
            if (strategy == GitPullStrategy.Merge)
            {
                return _strings.GetString("RemoteStrategyMerge");
            }
            if (strategy == GitPullStrategy.Rebase)
            {
                return _strings.GetString("RemoteStrategyRebase");
            }
            return _strings.GetString("RemoteStrategyFastForward");
        }

        private static bool IsPullDivergence(Exception exception)
        {
            if (exception is GitRemoteOperationException remoteException)
            {
                if (remoteException.ErrorCode == "RemotePullFastForwardUnavailable")
                {
                    return true;
                }
            }

            string diagnostic = exception.Message;
            if (exception is GitException gitException)
            {
                foreach (object argument in gitException.Arguments)
                {
                    if (argument is string value)
                    {
                        diagnostic += " " + value;
                    }
                }
            }
            return diagnostic.Contains("fast-forward", StringComparison.OrdinalIgnoreCase)
                || diagnostic.Contains("divergent", StringComparison.OrdinalIgnoreCase);
        }

        private class OperationExecutionResult
        {
            public OperationExecutionResult(string message, RemoteOperationOutcome outcome, string details = "")
            {
                Message = message;
                Outcome = outcome;
                Details = details;
            }

            public string Message { get; }
            public RemoteOperationOutcome Outcome { get; }
            public string Details { get; }
        }
    }
}
