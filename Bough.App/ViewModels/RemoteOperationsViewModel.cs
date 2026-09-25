using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Localization;
using Bough.Core.Git;

namespace Bough.App.ViewModels
{
    public class RemoteOperationsViewModel : ViewModelBase
    {
        private readonly GitRemoteOperationService _service;
        private readonly GitRepositoryService _repositoryService;
        private readonly StringHelper _strings;
        private readonly GitErrorLocalizer _errors;
        private readonly ObservableCollection<string> _remotes;
        private readonly Dictionary<string, string> _selectedRemotesByRepository;
        private CancellationTokenSource _cancellation;
        private CancellationTokenSource _loadCancellation;
        private GitRepository _repository;
        private GitRemoteState _state;
        private string _selectedRemote;
        private string _statusText;
        private string _operationOutcomeText;
        private RemoteOperationOutcome _lastOperationOutcome;
        private string _operationStageText;
        private string _transferStatusText;
        private string _pullSummaryText;
        private bool _isBusy;
        private bool _isLoading;
        private bool _prune;
        private bool _showPullStrategies;
        private int _requestVersion;

        public RemoteOperationsViewModel(GitRemoteOperationService service, GitRepositoryService repositoryService, StringHelper strings, GitErrorLocalizer errors)
        {
            _service = service;
            _repositoryService = repositoryService;
            _strings = strings;
            _errors = errors;
            _remotes = [];
            StringComparer repositoryComparer = StringComparer.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                repositoryComparer = StringComparer.OrdinalIgnoreCase;
            }
            _selectedRemotesByRepository = new Dictionary<string, string>(repositoryComparer);
            Remotes = new ReadOnlyObservableCollection<string>(_remotes);
            _selectedRemote = string.Empty;
            _statusText = _strings.GetString("RemoteSelectRepository");
            _operationOutcomeText = string.Empty;
            _operationStageText = string.Empty;
            _transferStatusText = string.Empty;
            _pullSummaryText = string.Empty;
        }

        public event Action<GitRepository> OperationCompleted;
        public ReadOnlyObservableCollection<string> Remotes { get; }
        public GitRepository CurrentRepository { get { return _repository; } }
        public RemoteOperationStateSnapshot LatestOperationStateSnapshot { get; private set; }
        public string UpstreamRemote { get { return _state?.UpstreamRemote ?? string.Empty; } }
        public string UpstreamBranch { get { return _state?.UpstreamBranch ?? string.Empty; } }
        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value) == true)
                {
                    NotifyState();
                }
            }
        }
        public bool IsLoading { get { return _isLoading; } private set { SetProperty(ref _isLoading, value); } }
        public bool Prune { get { return _prune; } set { SetProperty(ref _prune, value); } }
        public bool HasUpstream { get { return _state != null && _state.HasUpstream; } }
        public bool CanFetch { get { return CanFetchAll && _remotes.Contains(SelectedRemote); } }
        public bool CanFetchAll { get { return _repository != null && _remotes.Count > 0; } }
        public bool CanSelectFetchRemote { get { return CanFetchAll; } }
        public bool CanPull { get { return CanFetchAll && _state != null && _state.IsDetached == false && _state.HeadHash.Length > 0; } }
        public bool CanPush { get { return CanPushTo && HasNoOutgoingPushCommits == false; } }
        public bool CanPushTo { get { return CanPull; } }
        public bool HasNoOutgoingPushCommits { get { return CanPushTo && _state.HasUpstream && _state.Ahead == 0; } }
        public string PushAvailabilityText
        {
            get
            {
                if (HasNoOutgoingPushCommits == false)
                {
                    return string.Empty;
                }
                if (_state.Behind > 0)
                {
                    return _strings.Format("RemotePushNoNewCommitsBehind", _state.Behind);
                }
                return _strings.GetString("RemotePushNoNewCommits");
            }
        }
        public bool CanCancel { get { return IsBusy && _cancellation != null && _cancellation.IsCancellationRequested == false; } }
        public bool ShowPullStrategies { get { return _showPullStrategies || (_state != null && _state.Ahead > 0 && _state.Behind > 0); } }
        public string CurrentBranchText
        {
            get
            {
                if (_state != null && _state.BranchName.Length > 0) { return _state.BranchName; }
                return _strings.GetString("RemoteDetachedOrNoCommits");
            }
        }
        public string UpstreamText
        {
            get
            {
                if (_state != null && _state.UpstreamName.Length > 0) { return _state.UpstreamName; }
                return _strings.GetString("RemoteNoUpstream");
            }
        }
        public string AheadBehindText
        {
            get
            {
                if (_state == null || _state.HasUpstream == false) { return _strings.GetString("RemoteAheadBehindUnavailable"); }
                if (_state.Ahead < 0 || _state.Behind < 0) { return _strings.GetString("RemoteAheadBehindUnavailable"); }
                return $"↑{_state.Ahead} ↓{_state.Behind}";
            }
        }
        public string StatusText { get { return _statusText; } private set { SetProperty(ref _statusText, value); } }
        public string OperationOutcomeText { get { return _operationOutcomeText; } private set { SetProperty(ref _operationOutcomeText, value); } }
        public RemoteOperationOutcome LastOperationOutcome
        {
            get { return _lastOperationOutcome; }
            private set
            {
                if (SetProperty(ref _lastOperationOutcome, value) == false)
                {
                    return;
                }
                switch (value)
                {
                    case RemoteOperationOutcome.Running: OperationOutcomeText = _strings.GetString("RemoteOutcomeRunning"); break;
                    case RemoteOperationOutcome.Succeeded: OperationOutcomeText = _strings.GetString("RemoteOutcomeSucceeded"); break;
                    case RemoteOperationOutcome.Failed: OperationOutcomeText = _strings.GetString("RemoteOutcomeFailed"); break;
                    case RemoteOperationOutcome.Canceled: OperationOutcomeText = _strings.GetString("RemoteOutcomeCanceled"); break;
                    case RemoteOperationOutcome.PartiallySucceeded: OperationOutcomeText = _strings.GetString("RemoteOutcomePartiallySucceeded"); break;
                    case RemoteOperationOutcome.RefreshFailed: OperationOutcomeText = _strings.GetString("RemoteOutcomeRefreshFailed"); break;
                    case RemoteOperationOutcome.NoNewCommits: OperationOutcomeText = _strings.GetString("RemotePushNoNewCommits"); break;
                    default: OperationOutcomeText = string.Empty; break;
                }
            }
        }
        public string OperationStageText { get { return _operationStageText; } private set { SetProperty(ref _operationStageText, value); } }
        public string TransferStatusText { get { return _transferStatusText; } private set { SetProperty(ref _transferStatusText, value); } }
        public string PullSummaryText
        {
            get { return _pullSummaryText; }
            private set
            {
                if (SetProperty(ref _pullSummaryText, value) == true)
                {
                    OnPropertyChanged(nameof(HasPullSummary));
                }
            }
        }
        public bool HasPullSummary { get { return PullSummaryText.Length > 0; } }
        public string SelectedRemote
        {
            get { return _selectedRemote; }
            set
            {
                if (SetProperty(ref _selectedRemote, value) == false)
                {
                    return;
                }
                if (_repository != null)
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        _selectedRemotesByRepository.Remove(_repository.RootPath);
                    }
                    else
                    {
                        _selectedRemotesByRepository[_repository.RootPath] = value;
                    }
                }
                NotifyState();
            }
        }
        public async Task SetRepositoryAsync(GitRepository repository)
        {
            BindRepository(repository);
            if (repository == null)
            {
                return;
            }
            await RefreshAsync();
        }

        public void BindRepository(GitRepository repository)
        {
            string selectedRemote = string.Empty;
            if (repository != null)
            {
                if (_selectedRemotesByRepository.TryGetValue(repository.RootPath, out string rememberedRemote))
                {
                    selectedRemote = rememberedRemote;
                }
            }
            InvalidatePendingRequests();
            _repository = repository;
            LatestOperationStateSnapshot = null;
            _state = null;
            _remotes.Clear();
            SelectedRemote = selectedRemote;
            _showPullStrategies = false;
            LastOperationOutcome = RemoteOperationOutcome.None;
            OperationStageText = string.Empty;
            TransferStatusText = string.Empty;
            PullSummaryText = string.Empty;
            IsBusy = false;
            IsLoading = false;
            NotifyState();
            if (repository == null)
            {
                StatusText = _strings.GetString("RemoteSelectRepository");
                return;
            }
            StatusText = _strings.GetString("RemoteLoadingState");
        }

        public async Task<bool> ExecuteRequestAsync(RemoteOperationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            await SetRepositoryAsync(request.Repository);
            if (_state == null)
            {
                LastOperationOutcome = RemoteOperationOutcome.Failed;
                return false;
            }

            if (request.Kind != RemoteOperationKind.Fetch)
            {
                if (_state.BranchName != request.LocalBranch)
                {
                    StatusText = _strings.Format("RemoteRequestedBranchChanged", request.LocalBranch, _state.BranchName);
                    LastOperationOutcome = RemoteOperationOutcome.Failed;
                    return false;
                }
            }

            if (request.Kind == RemoteOperationKind.Fetch)
            {
                SelectedRemote = request.Remote;
                Prune = request.Prune;
                return await FetchAsync(request.FetchAll);
            }
            if (request.Kind == RemoteOperationKind.Pull)
            {
                return await PullAsync(request.PullStrategy, request.Remote, request.RemoteBranch);
            }

            if (await PrepareUpstreamPushAsync(request.Remote, request.RemoteBranch) == false)
            {
                if (LastOperationOutcome == RemoteOperationOutcome.None)
                {
                    LastOperationOutcome = RemoteOperationOutcome.Failed;
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
            LatestOperationStateSnapshot = null;
            IsBusy = false;
            IsLoading = false;
        }

        public bool ApplyOperationStateSnapshot(RemoteOperationStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }
            if (_repository == null)
            {
                return false;
            }
            if (IsBusy)
            {
                return false;
            }
            StringComparison comparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                comparison = StringComparison.OrdinalIgnoreCase;
            }
            if (string.Equals(_repository.RootPath, snapshot.Repository.RootPath, comparison) == false)
            {
                return false;
            }
            if (string.Equals(snapshot.Repository.RootPath, snapshot.State.RepositoryRoot, comparison) == false)
            {
                return false;
            }

            _requestVersion++;
            _loadCancellation?.Cancel();
            IsLoading = false;
            _repository = snapshot.Repository;
            ApplyState(snapshot.State);
            LatestOperationStateSnapshot = snapshot;
            StatusText = string.Empty;
            return true;
        }

        public async Task RefreshAsync()
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return;
            }
            if (IsBusy == true)
            {
                return;
            }
            _loadCancellation?.Cancel();
            using CancellationTokenSource cancellation = new();
            _loadCancellation = cancellation;
            int request = ++_requestVersion;
            IsLoading = true;
            StatusText = _strings.GetString("RemoteLoadingState");
            try
            {
                GitRemoteState state = await _service.GetStateAsync(repository, cancellation.Token);
                if (request != _requestVersion)
                {
                    return;
                }
                if (_repository != repository)
                {
                    return;
                }
                ApplyState(state);
                if (state.Remotes.Count == 0) { StatusText = _strings.GetString("RemoteNoRemotesConfigured"); }
                else if (state.IsDetached == true) { StatusText = _strings.GetString("RemoteDetachedHint"); }
                else if (state.HasUpstream == false) { StatusText = _strings.GetString("RemoteNoUpstreamHint"); }
                else { StatusText = _strings.GetString("RemoteStateRefreshed"); }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (request == _requestVersion)
                {
                    StatusText = _errors.GetDisplayMessage(exception);
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
                    IsLoading = false;
                }
            }
        }

        public async Task<bool> FetchAsync(bool fetchAll)
        {
            string remote = SelectedRemote;
            bool prune = Prune;
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
                        return new OperationExecutionResult(message, RemoteOperationOutcome.PartiallySucceeded);
                    }
                    return new OperationExecutionResult(message, RemoteOperationOutcome.Failed);
                }
                return new OperationExecutionResult(message, RemoteOperationOutcome.Succeeded);
            });
        }

        public async Task<IReadOnlyList<string>> GetRemoteBranchesAsync(string remote)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                return Array.Empty<string>();
            }
            GitRemoteState state = _state;
            if (state == null)
            {
                return await _service.GetRemoteBranchesAsync(repository, remote);
            }
            return await _service.GetRemoteBranchesAsync(repository, state, remote);
        }

        public async Task ValidatePushTargetAsync(string remote, string branch)
        {
            GitRepository repository = _repository;
            if (repository == null)
            {
                throw new GitException("RemoteSelectRepository", null, Array.Empty<object>());
            }
            GitRemoteState state = _state;
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
                IReadOnlyList<GitRemoteMessage> summary = await _service.PullWithProgressAsync(repository, state, remote, branch, strategy, progress, token);
                string resultText = _strings.Format("RemotePullCompleted", FormatStrategy(strategy), remote, branch);
                if (request != _requestVersion)
                {
                    return new OperationExecutionResult(resultText, RemoteOperationOutcome.Succeeded);
                }
                if (_repository != repository)
                {
                    return new OperationExecutionResult(resultText, RemoteOperationOutcome.Succeeded);
                }
                PullSummaryText = FormatSummary(summary);
                return new OperationExecutionResult(resultText, RemoteOperationOutcome.Succeeded);
            });
        }

        private void ApplyPullProgress(int request, GitPullProgress progress)
        {
            if (request != _requestVersion)
            {
                return;
            }
            if (progress.IncomingSummary != null)
            {
                PullSummaryText = FormatSummary(progress.IncomingSummary);
            }
            if (IsBusy == false)
            {
                return;
            }
            if (progress.Stage == GitPullStage.Fetching)
            {
                OperationStageText = _strings.GetString("RemoteStageFetching");
            }
            if (progress.Stage == GitPullStage.Inspecting)
            {
                OperationStageText = _strings.GetString("RemoteStageInspecting");
            }
            if (progress.Stage == GitPullStage.Applying)
            {
                OperationStageText = _strings.GetString("RemoteStageApplying");
            }
            if (progress.TransferStatus != null)
            {
                TransferStatusText = _strings.Format(progress.TransferStatus.Key, progress.TransferStatus.Arguments.ToArray());
            }
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

        public async Task<bool> PrepareUpstreamPushAsync(string remote, string branch)
        {
            GitRepository repository = _repository;
            GitRemoteState previous = _state;
            if (repository == null)
            {
                return false;
            }
            if (previous == null)
            {
                return false;
            }
            if (IsBusy)
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
            using CancellationTokenSource cancellation = new();
            _loadCancellation = cancellation;
            int request = ++_requestVersion;
            IsLoading = true;
            StatusText = _strings.GetString("RemoteCheckingPushState");
            LastOperationOutcome = RemoteOperationOutcome.None;
            try
            {
                GitRemoteState latest = await _service.GetStateAsync(repository, cancellation.Token);
                if (request != _requestVersion)
                {
                    return false;
                }
                if (_repository != repository)
                {
                    return false;
                }

                ApplyState(latest);
                if (latest.BranchName != previous.BranchName)
                {
                    StatusText = _strings.GetString("RemotePushBranchChanged");
                    return false;
                }
                if (latest.UpstreamRemote != remote)
                {
                    StatusText = _strings.GetString("RemotePushUpstreamChanged");
                    return false;
                }
                if (latest.UpstreamBranch != branch)
                {
                    StatusText = _strings.GetString("RemotePushUpstreamChanged");
                    return false;
                }
                if (HasNoOutgoingPushCommits)
                {
                    StatusText = PushAvailabilityText;
                    LastOperationOutcome = RemoteOperationOutcome.NoNewCommits;
                    return false;
                }
                StatusText = _strings.GetString("RemotePushCommitsConfirmed");
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                if (request == _requestVersion)
                {
                    StatusText = _errors.GetDisplayMessage(exception);
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
                    IsLoading = false;
                }
            }
        }

        public void Cancel()
        {
            if (IsBusy == false)
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
            StatusText = _strings.GetString("RemoteCancelRequested");
            _cancellation.Cancel();
            OnPropertyChanged(nameof(CanCancel));
        }

        private async Task<bool> RunAsync(string progressText, Func<GitRepository, GitRemoteState, CancellationToken, Task<OperationExecutionResult>> operation)
        {
            GitRepository repository = _repository;
            GitRemoteState state = _state;
            if (repository == null)
            {
                return false;
            }
            if (state == null)
            {
                return false;
            }
            if (IsBusy == true)
            {
                return false;
            }
            _loadCancellation?.Cancel();
            IsLoading = false;
            int request = ++_requestVersion;
            LatestOperationStateSnapshot = null;
            using CancellationTokenSource cancellation = new();
            _cancellation = cancellation;
            IsBusy = true;
            StatusText = progressText;
            LastOperationOutcome = RemoteOperationOutcome.Running;
            OperationStageText = string.Empty;
            TransferStatusText = string.Empty;
            PullSummaryText = string.Empty;
            OperationExecutionResult completedResult = null;
            try
            {
                completedResult = await operation(repository, state, cancellation.Token);
                if (request != _requestVersion)
                {
                    return false;
                }
                if (_repository != repository)
                {
                    return false;
                }
                if (OperationStageText.Length > 0)
                {
                    OperationStageText = _strings.GetString("RemoteStageCheckingState");
                }
                GitRepository updated = await _repositoryService.OpenAsync(repository.RootPath, cancellation.Token);
                GitRemoteState updatedState = await _service.GetStateAsync(updated, cancellation.Token);
                if (request != _requestVersion)
                {
                    return false;
                }
                if (_repository != repository)
                {
                    return false;
                }
                _repository = updated;
                ApplyState(updatedState);
                LatestOperationStateSnapshot = new RemoteOperationStateSnapshot(updated, updatedState);
                StatusText = completedResult.Message;
                LastOperationOutcome = completedResult.Outcome;
                if (completedResult.Outcome == RemoteOperationOutcome.NoNewCommits)
                {
                    NotifyState();
                    return false;
                }
                if (OperationStageText.Length > 0)
                {
                    OperationStageText = _strings.GetString("RemoteStageComplete");
                }
                if (completedResult.Outcome == RemoteOperationOutcome.Succeeded || completedResult.Outcome == RemoteOperationOutcome.PartiallySucceeded)
                {
                    OperationCompleted?.Invoke(updated);
                }
                if (completedResult.Outcome == RemoteOperationOutcome.Succeeded)
                {
                    _showPullStrategies = false;
                }
                NotifyState();
                return completedResult.Outcome == RemoteOperationOutcome.Succeeded;
            }
            catch (OperationCanceledException)
            {
                if (request == _requestVersion)
                {
                    string refreshError = await RefreshAfterOutcomeAsync(repository, request);
                    if (completedResult == null)
                    {
                        StatusText = _strings.GetString("RemoteOperationCanceled");
                        LastOperationOutcome = RemoteOperationOutcome.Canceled;
                    }
                    else
                    {
                        StatusText = _strings.Format("RemoteStateCheckCanceled", completedResult.Message);
                        LastOperationOutcome = RemoteOperationOutcome.RefreshFailed;
                    }
                    if (refreshError.Length > 0)
                    {
                        StatusText += _strings.Format("RemoteRefreshFailedSuffix", refreshError);
                    }
                }
                return false;
            }
            catch (Exception exception)
            {
                if (request == _requestVersion)
                {
                    string refreshError = await RefreshAfterOutcomeAsync(repository, request);
                    StatusText = _errors.GetDisplayMessage(exception);
                    if (completedResult == null)
                    {
                        LastOperationOutcome = RemoteOperationOutcome.Failed;
                    }
                    else
                    {
                        StatusText = _strings.Format("RemoteStateRefreshFailed", completedResult.Message, _errors.GetDisplayMessage(exception));
                        LastOperationOutcome = RemoteOperationOutcome.RefreshFailed;
                    }
                    if (refreshError.Length > 0)
                    {
                        StatusText += _strings.Format("RemoteRefreshFailedSuffix", refreshError);
                    }
                    if (IsPullDivergence(exception))
                    {
                        _showPullStrategies = true;
                        OnPropertyChanged(nameof(ShowPullStrategies));
                    }
                }
                return false;
            }
            finally
            {
                if (request == _requestVersion)
                {
                    IsBusy = false;
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
                GitRemoteState state = await _service.GetStateAsync(updated);
                if (request != _requestVersion)
                {
                    return string.Empty;
                }
                if (_repository != repository)
                {
                    return string.Empty;
                }
                _repository = updated;
                ApplyState(state);
                LatestOperationStateSnapshot = new RemoteOperationStateSnapshot(updated, state);
                OperationCompleted?.Invoke(updated);
                return string.Empty;
            }
            catch (Exception exception)
            {
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

        private string FormatSummary(IReadOnlyList<GitRemoteMessage> messages)
        {
            List<string> lines = [];
            foreach (GitRemoteMessage message in messages)
            {
                if (message.Key == null)
                {
                    lines.Add(message.Arguments[0]?.ToString() ?? string.Empty);
                    continue;
                }
                lines.Add(_strings.Format(message.Key, message.Arguments.ToArray()));
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static bool IsPullDivergence(Exception exception)
        {
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

        private void NotifyState()
        {
            OnPropertyChanged(nameof(CurrentBranchText));
            OnPropertyChanged(nameof(UpstreamText));
            OnPropertyChanged(nameof(UpstreamRemote));
            OnPropertyChanged(nameof(UpstreamBranch));
            OnPropertyChanged(nameof(AheadBehindText));
            OnPropertyChanged(nameof(HasUpstream));
            OnPropertyChanged(nameof(CanFetch));
            OnPropertyChanged(nameof(CanFetchAll));
            OnPropertyChanged(nameof(CanSelectFetchRemote));
            OnPropertyChanged(nameof(CanPull));
            OnPropertyChanged(nameof(CanPush));
            OnPropertyChanged(nameof(CanPushTo));
            OnPropertyChanged(nameof(HasNoOutgoingPushCommits));
            OnPropertyChanged(nameof(PushAvailabilityText));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(ShowPullStrategies));
        }

        private void ApplyState(GitRemoteState state)
        {
            _state = state;
            _remotes.Clear();
            foreach (string remote in state.Remotes) { _remotes.Add(remote); }
            if (_remotes.Contains(SelectedRemote))
            {
                NotifyState();
                return;
            }
            if (state.HasUpstream == true)
            {
                SelectedRemote = state.UpstreamRemote;
            }
            else
            {
                SelectedRemote = string.Empty;
                if (state.Remotes.Count == 1)
                {
                    SelectedRemote = state.Remotes[0];
                }
            }
            NotifyState();
        }

        private class OperationExecutionResult
        {
            public OperationExecutionResult(string message, RemoteOperationOutcome outcome)
            {
                Message = message;
                Outcome = outcome;
            }

            public string Message { get; }
            public RemoteOperationOutcome Outcome { get; }
        }
    }
}
