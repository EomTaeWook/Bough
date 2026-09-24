using System.Collections.Generic;

namespace Bough.Core.Git
{
    public class GitHubAccountSnapshot
    {
        public GitHubAccountSnapshot(bool isGcmAvailable, string availabilityMessageCode, IReadOnlyList<string> knownAccounts, string accountListErrorCode, IReadOnlyList<GitHubRemoteAccount> remotes)
        {
            IsGcmAvailable = isGcmAvailable;
            AvailabilityMessageCode = availabilityMessageCode;
            KnownAccounts = knownAccounts;
            AccountListErrorCode = accountListErrorCode;
            Remotes = remotes;
        }

        public bool IsGcmAvailable { get; }
        public string AvailabilityMessageCode { get; }
        public IReadOnlyList<string> KnownAccounts { get; }
        public string AccountListErrorCode { get; }
        public IReadOnlyList<GitHubRemoteAccount> Remotes { get; }
    }
}
