using System.Collections.Generic;

namespace Bough.Core.Git.Models
{
    public class GitSettingsSnapshot
    {
        public GitSettingsSnapshot(string localName, string localEmail, string globalName, string globalEmail, string credentialHelper, IReadOnlyList<GitRemote> remotes)
        {
            LocalName = localName;
            LocalEmail = localEmail;
            GlobalName = globalName;
            GlobalEmail = globalEmail;
            CredentialHelper = credentialHelper;
            Remotes = remotes;
        }

        public string LocalName { get; }
        public string LocalEmail { get; }
        public string GlobalName { get; }
        public string GlobalEmail { get; }
        public string CredentialHelper { get; }
        public IReadOnlyList<GitRemote> Remotes { get; }
    }
}
