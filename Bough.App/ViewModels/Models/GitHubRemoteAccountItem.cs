using System;
using System.Collections.Generic;
using Bough.Core.Git.Models;

namespace Bough.App.ViewModels.Models
{
    public class GitHubRemoteAccountItem : ViewModelBase
    {
        private string _candidateName;
        private string _statusText = string.Empty;
        private string _fetchUrlText = string.Empty;
        private string _pushUrlText = string.Empty;
        private string _remoteUrlDetailsText = string.Empty;
        private string _remoteUrlDetailsLabel = string.Empty;
        private bool _isRemoteUrlDetailsExpanded;
        private string _removePushUrlText = string.Empty;
        private string _selectedAccountText = string.Empty;
        private string _unavailableReason = string.Empty;
        private string _selectionPreviewText = string.Empty;
        private string _existingAccountPlaceholder = string.Empty;
        private string _userNamePlaceholder = string.Empty;
        private string _remoteUserNameAutomationName = string.Empty;
        private string _selectAccountLabel = string.Empty;
        private string _addAccountLabel = string.Empty;
        private bool _canChange;
        private bool _canLogin;

        public GitHubRemoteAccountItem(GitHubRemoteAccount remote, IReadOnlyList<string> knownAccounts, bool sharedUrl)
        {
            RemoteName = remote.RemoteName;
            FetchUrl = remote.FetchUrl;
            PushUrl = remote.PushUrl;
            PushUrlSettings = remote.PushUrlSettings;
            SelectedUserName = remote.SelectedUserName;
            IsRepositorySelection = remote.IsRepositorySelection;
            IsSupported = remote.CanSelect;
            UnavailableReasonCode = remote.UnavailableReasonCode;
            if (sharedUrl)
            {
                IsSupported = false;
                UnavailableReasonCode = "GitHubSharedRemoteUrlUnsupported";
            }
            KnownAccounts = knownAccounts;
            _candidateName = remote.SelectedUserName;
        }

        public string RemoteName { get; }
        public string FetchUrl { get; }
        public string PushUrl { get; }
        public GitRemotePushUrlSettings PushUrlSettings { get; }
        public bool CanRemovePushUrl { get { return PushUrlSettings?.PushUrlAfterRemoval != null; } }
        public string SelectedUserName { get; }
        public bool IsRepositorySelection { get; }
        public bool IsSupported { get; }
        public string UnavailableReasonCode { get; }
        public IReadOnlyList<string> KnownAccounts { get; }
        public string FetchUrlText { get { return _fetchUrlText; } set { SetProperty(ref _fetchUrlText, value); } }
        public string PushUrlText
        {
            get { return _pushUrlText; }
            set
            {
                if (SetProperty(ref _pushUrlText, value))
                {
                    OnPropertyChanged(nameof(HasPushUrlSummary));
                }
            }
        }
        public bool HasPushUrlSummary { get { return PushUrlText.Length > 0; } }
        public string RemoteUrlDetailsText { get { return _remoteUrlDetailsText; } set { SetProperty(ref _remoteUrlDetailsText, value); } }
        public string RemoteUrlDetailsLabel { get { return _remoteUrlDetailsLabel; } set { SetProperty(ref _remoteUrlDetailsLabel, value); } }
        public bool IsRemoteUrlDetailsExpanded { get { return _isRemoteUrlDetailsExpanded; } set { SetProperty(ref _isRemoteUrlDetailsExpanded, value); } }
        public string RemovePushUrlText { get { return _removePushUrlText; } set { SetProperty(ref _removePushUrlText, value); } }
        public string SelectedAccountText { get { return _selectedAccountText; } set { SetProperty(ref _selectedAccountText, value); } }
        public string UnavailableReason
        {
            get { return _unavailableReason; }
            set
            {
                if (SetProperty(ref _unavailableReason, value))
                {
                    OnPropertyChanged(nameof(HasUnavailableReason));
                }
            }
        }
        public bool HasUnavailableReason { get { return UnavailableReason.Length > 0; } }
        public string ExistingAccountPlaceholder { get { return _existingAccountPlaceholder; } set { SetProperty(ref _existingAccountPlaceholder, value); } }
        public string UserNamePlaceholder { get { return _userNamePlaceholder; } set { SetProperty(ref _userNamePlaceholder, value); } }
        public string RemoteUserNameAutomationName { get { return _remoteUserNameAutomationName; } set { SetProperty(ref _remoteUserNameAutomationName, value); } }
        public string SelectAccountLabel { get { return _selectAccountLabel; } set { SetProperty(ref _selectAccountLabel, value); } }
        public string AddAccountLabel { get { return _addAccountLabel; } set { SetProperty(ref _addAccountLabel, value); } }
        public string CandidateName
        {
            get { return _candidateName; }
            set
            {
                if (SetProperty(ref _candidateName, value))
                {
                    OnPropertyChanged(nameof(CanApply));
                }
            }
        }
        public string SelectionPreviewText { get { return _selectionPreviewText; } set { SetProperty(ref _selectionPreviewText, value); } }
        public string StatusText { get { return _statusText; } set { SetProperty(ref _statusText, value); } }
        public bool CanChange
        {
            get { return _canChange; }
            set
            {
                if (SetProperty(ref _canChange, value))
                {
                    OnPropertyChanged(nameof(CanApply));
                }
            }
        }
        public bool CanLogin { get { return _canLogin; } set { SetProperty(ref _canLogin, value); } }
        public bool CanApply { get { return CanChange && string.IsNullOrWhiteSpace(CandidateName) == false; } }
    }
}
