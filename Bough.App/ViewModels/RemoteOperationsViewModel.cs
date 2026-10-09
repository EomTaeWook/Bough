using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.Core.Git;
using Bough.App.ViewModels.Models;
using Bough.Core.Git.Models;
using Bough.Core.Internals;
using Bough.App.Internals;

namespace Bough.App.ViewModels
{
    public class RemoteOperationsViewModel : ViewModelBase
    {
        private LocalizedText _statusTextLocalization;
        private LocalizedText _operationOutcomeTextLocalization;
        private LocalizedText _operationStageTextLocalization;
        private LocalizedText _transferStatusTextLocalization;
        internal void SetLocalizedStatusText(LocalizedText text)
        {
            StatusText = text.GetText(_strings);
            _statusTextLocalization = text;
        }

        private void SetLocalizedOperationOutcomeText(LocalizedText text)
        {
            OperationOutcomeText = text.GetText(_strings);
            _operationOutcomeTextLocalization = text;
        }

        internal void SetLocalizedOperationStageText(LocalizedText text)
        {
            OperationStageText = text.GetText(_strings);
            _operationStageTextLocalization = text;
        }

        internal void SetLocalizedTransferStatusText(LocalizedText text)
        {
            TransferStatusText = text.GetText(_strings);
            _transferStatusTextLocalization = text;
        }

        private readonly RemoteOperationExecutionPresenter _executionPresenter;
        private readonly StringHelper _strings;
        private readonly ObservableCollection<string> _remotes;
        private readonly Dictionary<string, string> _selectedRemotesByRepository;
        private GitRepository _repository;
        private GitRemoteState _state;
        private string _selectedRemote;
        private string _statusText;
        private string _operationOutcomeText;
        private RemoteOperationOutcome _lastOperationOutcome;
        private string _operationStageText;
        private string _transferStatusText;
        private string _operationDetailsText = string.Empty;
        private bool _isBusy;
        private bool _isLoading;
        private bool _prune;
        private bool _showPullStrategies;

        public RemoteOperationsViewModel(GitRemoteOperationService service, GitRepositoryService repositoryService, StringHelper strings, GitErrorLocalizer errors)
        {
            _executionPresenter = new RemoteOperationExecutionPresenter(this, service, repositoryService, strings, errors);
            _strings = strings;
            _remotes = [];
            StringComparer repositoryComparer = StringComparer.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                repositoryComparer = StringComparer.OrdinalIgnoreCase;
            }
            _selectedRemotesByRepository = new Dictionary<string, string>(repositoryComparer);
            Remotes = new ReadOnlyObservableCollection<string>(_remotes);
            _selectedRemote = string.Empty;
            SetLocalizedStatusText(new LocalizedText("RemoteSelectRepository"));
            _operationOutcomeText = string.Empty;
            _operationStageText = string.Empty;
            _transferStatusText = string.Empty;
        }

        internal GitRemoteState RemoteState { get { return _state; } }
        internal bool PullStrategiesRequested
        {
            get { return _showPullStrategies; }
            set { _showPullStrategies = value; }
        }

        internal void NotifyOperationCompleted(GitRepository repository)
        {
            OperationCompleted?.Invoke(repository);
        }

        public event Action<GitRepository> OperationCompleted;
        public ReadOnlyObservableCollection<string> Remotes { get; }
        public GitRepository CurrentRepository { get { return _repository; } internal set { _repository = value; } }
        public RemoteOperationStateSnapshot LatestOperationStateSnapshot { get; internal set; }
        public string UpstreamRemote { get { return _state?.UpstreamRemote ?? string.Empty; } }
        public string UpstreamBranch { get { return _state?.UpstreamBranch ?? string.Empty; } }
        public bool IsBusy
        {
            get { return _isBusy; }
            internal set
            {
                if (SetProperty(ref _isBusy, value) == true)
                {
                    NotifyState();
                }
            }
        }
        public bool IsLoading { get { return _isLoading; } internal set { SetProperty(ref _isLoading, value); } }
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
        public bool CanCancel { get { return _executionPresenter.CanCancel; } }
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
        public string StatusText
        {
            get
            {
                if (_statusTextLocalization != null)
                {
                    return _statusTextLocalization.GetText(_strings);
                }
                return _statusText;
            }
            internal set
            {
                _statusTextLocalization = null; SetProperty(ref _statusText, value);
            }
        }
        public string OperationOutcomeText
        {
            get
            {
                if (_operationOutcomeTextLocalization != null)
                {
                    return _operationOutcomeTextLocalization.GetText(_strings);
                }
                return _operationOutcomeText;
            }
            private set
            {
                _operationOutcomeTextLocalization = null; SetProperty(ref _operationOutcomeText, value);
            }
        }
        public RemoteOperationOutcome LastOperationOutcome
        {
            get { return _lastOperationOutcome; }
            internal set
            {
                if (SetProperty(ref _lastOperationOutcome, value) == false)
                {
                    return;
                }
                switch (value)
                {
                    case RemoteOperationOutcome.Running: SetLocalizedOperationOutcomeText(new LocalizedText("RemoteOutcomeRunning")); break;
                    case RemoteOperationOutcome.Succeeded: SetLocalizedOperationOutcomeText(new LocalizedText("RemoteOutcomeSucceeded")); break;
                    case RemoteOperationOutcome.Failed: SetLocalizedOperationOutcomeText(new LocalizedText("RemoteOutcomeFailed")); break;
                    case RemoteOperationOutcome.Canceled: SetLocalizedOperationOutcomeText(new LocalizedText("RemoteOutcomeCanceled")); break;
                    case RemoteOperationOutcome.PartiallySucceeded: SetLocalizedOperationOutcomeText(new LocalizedText("RemoteOutcomePartiallySucceeded")); break;
                    case RemoteOperationOutcome.RefreshFailed: SetLocalizedOperationOutcomeText(new LocalizedText("RemoteOutcomeRefreshFailed")); break;
                    case RemoteOperationOutcome.NoNewCommits: SetLocalizedOperationOutcomeText(new LocalizedText("RemotePushNoNewCommits")); break;
                    default: OperationOutcomeText = string.Empty; break;
                }
            }
        }
        public string OperationStageText
        {
            get
            {
                if (_operationStageTextLocalization != null)
                {
                    return _operationStageTextLocalization.GetText(_strings);
                }
                return _operationStageText;
            }
            internal set
            {
                _operationStageTextLocalization = null; SetProperty(ref _operationStageText, value);
            }
        }
        public string TransferStatusText
        {
            get
            {
                if (_transferStatusTextLocalization != null)
                {
                    return _transferStatusTextLocalization.GetText(_strings);
                }
                return _transferStatusText;
            }
            internal set
            {
                _transferStatusTextLocalization = null; SetProperty(ref _transferStatusText, value);
            }
        }
        public string OperationDetailsText
        {
            get { return _operationDetailsText; }
            internal set
            {
                if (SetProperty(ref _operationDetailsText, value))
                {
                    OnPropertyChanged(nameof(HasOperationDiagnostic));
                }
            }
        }
        public bool HasOperationDiagnostic { get { return OperationDetailsText.Length > 0; } }
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
        public Task SetRepositoryAsync(GitRepository repository, CancellationToken cancellationToken = default)
        {
            return _executionPresenter.SetRepositoryAsync(repository, cancellationToken);
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
            OperationDetailsText = string.Empty;
            IsBusy = false;
            IsLoading = false;
            NotifyState();
            if (repository == null)
            {
                SetLocalizedStatusText(new LocalizedText("RemoteSelectRepository"));
                return;
            }
            SetLocalizedStatusText(new LocalizedText("RemoteLoadingState"));
        }

        public Task<bool> ExecuteRequestAsync(RemoteOperationRequest request)
        {
            return _executionPresenter.ExecuteRequestAsync(request);
        }

        public void InvalidatePendingRequests()
        {
            _executionPresenter.InvalidatePendingRequests();
        }

        public bool ApplyOperationStateSnapshot(RemoteOperationStateSnapshot snapshot)
        {
            return _executionPresenter.ApplyOperationStateSnapshot(snapshot);
        }

        public Task RefreshAsync()
        {
            return _executionPresenter.RefreshAsync();
        }

        public Task<bool> FetchAsync(bool fetchAll)
        {
            return _executionPresenter.FetchAsync(fetchAll);
        }

        public Task<IReadOnlyList<string>> GetRemoteBranchesAsync(string remote)
        {
            return _executionPresenter.GetRemoteBranchesAsync(remote);
        }

        public Task ValidatePushTargetAsync(string remote, string branch)
        {
            return _executionPresenter.ValidatePushTargetAsync(remote, branch);
        }

        public Task<bool> PullAsync(GitPullStrategy strategy, string remote, string branch)
        {
            return _executionPresenter.PullAsync(strategy, remote, branch);
        }

        public Task<bool> PushAsync(string remote, string branch, bool targetConfirmed)
        {
            return _executionPresenter.PushAsync(remote, branch, targetConfirmed);
        }

        public Task<bool> PrepareUpstreamPushAsync(string remote, string branch, CancellationToken cancellationToken = default)
        {
            return _executionPresenter.PrepareUpstreamPushAsync(remote, branch, cancellationToken);
        }

        public void Cancel()
        {
            _executionPresenter.Cancel();
        }

        internal void ReportCompletionFailure(Exception exception)
        {
            _executionPresenter.ReportCompletionFailure(exception);
        }

        internal void NotifyState()
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

        internal void ApplyState(GitRemoteState state)
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

    }
}
