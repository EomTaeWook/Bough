namespace Bough.Core.Git
{
    public class GitHubRemoteAccount
    {
        public GitHubRemoteAccount(string remoteName, string fetchUrl, string pushUrl, string selectedUserName, bool isRepositorySelection, bool canSelect, string unavailableReasonCode)
        {
            RemoteName = remoteName;
            FetchUrl = fetchUrl;
            PushUrl = pushUrl;
            SelectedUserName = selectedUserName;
            IsRepositorySelection = isRepositorySelection;
            CanSelect = canSelect;
            UnavailableReasonCode = unavailableReasonCode;
        }

        public string RemoteName { get; }
        public string FetchUrl { get; }
        public string PushUrl { get; }
        public string SelectedUserName { get; }
        public bool IsRepositorySelection { get; }
        public bool CanSelect { get; }
        public string UnavailableReasonCode { get; }
    }
}
