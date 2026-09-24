using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Git;

namespace Bough.App.ViewModels
{
    public enum RemoteOperationOutcome
    {
        None,
        Running,
        Succeeded,
        Failed,
        Canceled,
        PartiallySucceeded,
        RefreshFailed,
        NoNewCommits
    }

    public enum RemoteOperationKind
    {
        Fetch,
        Pull,
        Push
    }

    public class RemoteOperationRequest
    {
        private RemoteOperationRequest(RemoteOperationKind kind, GitRepository repository, string localBranch,
            string remote, string remoteBranch, bool fetchAll, bool prune, GitPullStrategy pullStrategy, bool pushTargetConfirmed)
        {
            ArgumentNullException.ThrowIfNull(repository);
            Kind = kind;
            Repository = repository;
            LocalBranch = localBranch ?? string.Empty;
            Remote = remote ?? string.Empty;
            RemoteBranch = remoteBranch ?? string.Empty;
            FetchAll = fetchAll;
            Prune = prune;
            PullStrategy = pullStrategy;
            PushTargetConfirmed = pushTargetConfirmed;
        }

        public RemoteOperationKind Kind { get; }
        public GitRepository Repository { get; }
        public string LocalBranch { get; }
        public string Remote { get; }
        public string RemoteBranch { get; }
        public bool FetchAll { get; }
        public bool Prune { get; }
        public GitPullStrategy PullStrategy { get; }
        public bool PushTargetConfirmed { get; }

        public static RemoteOperationRequest ForFetch(GitRepository repository, string remote, bool fetchAll, bool prune)
        {
            return new RemoteOperationRequest(RemoteOperationKind.Fetch, repository, string.Empty,
                remote, string.Empty, fetchAll, prune, default, false);
        }

        public static RemoteOperationRequest ForPull(GitRepository repository, string localBranch, string remote,
            string remoteBranch, GitPullStrategy strategy)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localBranch);
            ArgumentException.ThrowIfNullOrWhiteSpace(remote);
            ArgumentException.ThrowIfNullOrWhiteSpace(remoteBranch);
            return new RemoteOperationRequest(RemoteOperationKind.Pull, repository, localBranch,
                remote, remoteBranch, false, false, strategy, false);
        }

        public static RemoteOperationRequest ForPush(GitRepository repository, string localBranch, string remote,
            string remoteBranch, bool targetConfirmed)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localBranch);
            ArgumentException.ThrowIfNullOrWhiteSpace(remote);
            ArgumentException.ThrowIfNullOrWhiteSpace(remoteBranch);
            return new RemoteOperationRequest(RemoteOperationKind.Push, repository, localBranch,
                remote, remoteBranch, false, false, default, targetConfirmed);
        }
    }

    public class RemoteOperationStateSnapshot
    {
        public RemoteOperationStateSnapshot(GitRepository repository, GitRemoteState state)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(state);
            Repository = repository;
            State = state;
        }

        public GitRepository Repository { get; }
        public GitRemoteState State { get; }
    }

    public class RemoteOperationsViewModel : ViewModelBase
    {
        private readonly GitRemoteOperationService _service;
        private readonly GitRepositoryService _repositoryService;
        private readonly ObservableCollection<string> _remotes;
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

        public RemoteOperationsViewModel(GitRemoteOperationService service, GitRepositoryService repositoryService)
        {
            _service = service;
            _repositoryService = repositoryService;
            _remotes = [];
            Remotes = new ReadOnlyObservableCollection<string>(_remotes);
            _selectedRemote = string.Empty;
            _statusText = "Select a repository.";
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
                    return $"보낼 새 커밋 없음 · upstream보다 {_state.Behind}개 뒤처짐";
                }
                return "보낼 새 커밋 없음";
            }
        }
        public bool CanCancel { get { return IsBusy && _cancellation != null && _cancellation.IsCancellationRequested == false; } }
        public bool ShowPullStrategies { get { return _showPullStrategies || (_state != null && _state.Ahead > 0 && _state.Behind > 0); } }
        public string CurrentBranchText
        {
            get
            {
                if (_state != null && _state.BranchName.Length > 0) { return _state.BranchName; }
                return "Detached HEAD / no commits";
            }
        }
        public string UpstreamText
        {
            get
            {
                if (_state != null && _state.UpstreamName.Length > 0) { return _state.UpstreamName; }
                return "No upstream";
            }
        }
        public string AheadBehindText
        {
            get
            {
                if (_state == null || _state.HasUpstream == false) { return "Ahead / behind unavailable"; }
                if (_state.Ahead < 0 || _state.Behind < 0) { return "Ahead / behind unavailable"; }
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
                    case RemoteOperationOutcome.Running: OperationOutcomeText = "진행 중"; break;
                    case RemoteOperationOutcome.Succeeded: OperationOutcomeText = "성공"; break;
                    case RemoteOperationOutcome.Failed: OperationOutcomeText = "실패"; break;
                    case RemoteOperationOutcome.Canceled: OperationOutcomeText = "중단됨"; break;
                    case RemoteOperationOutcome.PartiallySucceeded: OperationOutcomeText = "부분 성공"; break;
                    case RemoteOperationOutcome.RefreshFailed: OperationOutcomeText = "상태 갱신 실패"; break;
                    case RemoteOperationOutcome.NoNewCommits: OperationOutcomeText = "보낼 새 커밋 없음"; break;
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
                if (SetProperty(ref _selectedRemote, value) == true) { NotifyState(); }
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
            InvalidatePendingRequests();
            _repository = repository;
            LatestOperationStateSnapshot = null;
            _state = null;
            _remotes.Clear();
            SelectedRemote = string.Empty;
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
                StatusText = "Select a repository.";
                return;
            }
            StatusText = "Loading remote state...";
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
                    StatusText = $"요청한 브랜치 {request.LocalBranch}이(가) 현재 체크아웃되어 있지 않습니다. 현재 브랜치: {_state.BranchName}.";
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
            StatusText = "Loading remote state...";
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
                if (state.Remotes.Count == 0) { StatusText = "No remotes configured."; }
                else if (state.IsDetached == true) { StatusText = "Detached HEAD: Fetch is available; Pull and Push require a branch."; }
                else if (state.HasUpstream == false) { StatusText = "Choose an existing remote branch for Pull or confirm a destination for the first Push."; }
                else { StatusText = "Remote state refreshed."; }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (request == _requestVersion)
                {
                    StatusText = exception.Message;
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
            string target = remote;
            if (fetchAll == true)
            {
                target = "all remotes";
            }
            return await RunAsync($"Fetching from {target}...", async (repository, state, token) =>
            {
                GitFetchResult result = await _service.FetchAsync(repository, state, remote, fetchAll, prune, token);
                string succeeded = string.Join(", ", result.SucceededRemotes);
                string updated = "No ref changes";
                if (result.UpdatedReferences.Count > 0)
                {
                    updated = string.Join(", ", result.UpdatedReferences);
                }
                string message = $"Fetched: {succeeded}. Updated: {updated}.";
                if (result.FailedRemotes.Count > 0)
                {
                    message += $" Failed: {string.Join("; ", result.FailedRemotes)}";
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
                throw new GitException("저장소를 선택하세요.");
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
            return await RunAsync($"Pulling from {remote}/{branch}...", async (repository, state, token) =>
            {
                string summary = await _service.PullWithProgressAsync(repository, state, remote, branch, strategy, progress, token);
                string resultText = $"Pull {strategy} completed from {remote}/{branch}.";
                if (request != _requestVersion)
                {
                    return new OperationExecutionResult(resultText, RemoteOperationOutcome.Succeeded);
                }
                if (_repository != repository)
                {
                    return new OperationExecutionResult(resultText, RemoteOperationOutcome.Succeeded);
                }
                PullSummaryText = summary;
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
                PullSummaryText = progress.IncomingSummary;
            }
            if (IsBusy == false)
            {
                return;
            }
            if (progress.Stage == GitPullStage.Fetching)
            {
                OperationStageText = "가져오는 중";
            }
            if (progress.Stage == GitPullStage.Inspecting)
            {
                OperationStageText = "받은 커밋과 파일 확인 중";
            }
            if (progress.Stage == GitPullStage.Applying)
            {
                OperationStageText = "브랜치에 반영 중";
            }
            if (progress.TransferStatus != null)
            {
                TransferStatusText = progress.TransferStatus;
            }
        }

        public async Task<bool> PushAsync(string remote, string branch, bool targetConfirmed)
        {
            return await RunAsync($"Pushing to {remote}/{branch}...", async (repository, state, token) =>
            {
                bool pushed = await _service.PushAsync(repository, state, remote, branch, targetConfirmed, token);
                if (pushed == false)
                {
                    return new OperationExecutionResult("보낼 새 커밋 없음", RemoteOperationOutcome.NoNewCommits);
                }
                return new OperationExecutionResult($"Pushed to {remote}/{branch}.", RemoteOperationOutcome.Succeeded);
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
            StatusText = "Push 상태 확인 중...";
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
                    StatusText = "현재 브랜치가 변경되었습니다. Push 대상을 다시 확인하세요.";
                    return false;
                }
                if (latest.UpstreamRemote != remote)
                {
                    StatusText = "Upstream이 변경되었습니다. Push 대상을 다시 확인하세요.";
                    return false;
                }
                if (latest.UpstreamBranch != branch)
                {
                    StatusText = "Upstream이 변경되었습니다. Push 대상을 다시 확인하세요.";
                    return false;
                }
                if (HasNoOutgoingPushCommits)
                {
                    StatusText = PushAvailabilityText;
                    LastOperationOutcome = RemoteOperationOutcome.NoNewCommits;
                    return false;
                }
                StatusText = "Push할 새 커밋을 확인했습니다.";
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
                    StatusText = exception.Message;
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
            StatusText = "원격 작업 중단을 요청했습니다. 종료를 기다리는 중...";
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
                    OperationStageText = "저장소 상태 확인 중";
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
                    OperationStageText = "완료";
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
                        StatusText = "원격 작업이 중단되었습니다. 이미 적용된 변경은 되돌리지 않습니다.";
                        LastOperationOutcome = RemoteOperationOutcome.Canceled;
                    }
                    else
                    {
                        StatusText = $"{completedResult.Message} 상태 확인 중 중단되었습니다.";
                        LastOperationOutcome = RemoteOperationOutcome.RefreshFailed;
                    }
                    if (refreshError.Length > 0)
                    {
                        StatusText += $" Refresh failed: {refreshError}";
                    }
                }
                return false;
            }
            catch (Exception exception)
            {
                if (request == _requestVersion)
                {
                    string refreshError = await RefreshAfterOutcomeAsync(repository, request);
                    StatusText = exception.Message;
                    if (completedResult == null)
                    {
                        LastOperationOutcome = RemoteOperationOutcome.Failed;
                    }
                    else
                    {
                        StatusText = $"{completedResult.Message} 상태 갱신 실패: {exception.Message}";
                        LastOperationOutcome = RemoteOperationOutcome.RefreshFailed;
                    }
                    if (refreshError.Length > 0)
                    {
                        StatusText += $" Refresh failed: {refreshError}";
                    }
                    if (exception.Message.Contains("fast-forward", StringComparison.OrdinalIgnoreCase) == true || exception.Message.Contains("divergent", StringComparison.OrdinalIgnoreCase) == true)
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
                return exception.Message;
            }
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
