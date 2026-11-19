using System.Collections.Generic;

namespace Bough.Core.Git.Models
{
    public class GitRemotePushUrlSettings
    {
        internal GitRemotePushUrlSettings()
        {
        }

        public IReadOnlyList<string> FetchUrls { get; internal set; }
        public IReadOnlyList<string> PushUrls { get; internal set; }
        public IReadOnlyList<(string Scope, string Origin, string Url)> ExplicitPushUrls { get; internal set; }
        public string PushSourceCode { get; internal set; }
        public (string Rule, string Scope, string Origin)? FetchRewrite { get; internal set; }
        public (string Rule, string Scope, string Origin)? PushRewrite { get; internal set; }
        public string SharedConfigPath { get; internal set; }
        public string PushUrlAfterRemoval { get; internal set; }
        public bool IsReadable { get; internal set; }
        internal string RemovalFingerprint { get; set; }
    }
}
