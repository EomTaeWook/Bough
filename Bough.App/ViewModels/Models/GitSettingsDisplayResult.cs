using Bough.App.Internals;
using System;

namespace Bough.App.ViewModels.Models
{
    public class GitSettingsDisplayResult
    {
        public GitSettingsDisplayResult(GitSettingsDisplayTarget target, string code, object[] arguments, Exception error, GitHubRemoteAccountItem account, Exception refreshError = null)
        {
            Target = target;
            Code = code;
            Arguments = arguments;
            Error = error;
            Account = account;
            RefreshError = refreshError;
        }

        public GitSettingsDisplayTarget Target { get; }
        public string Code { get; }
        public object[] Arguments { get; }
        public Exception Error { get; }
        public GitHubRemoteAccountItem Account { get; }
        public Exception RefreshError { get; }
    }
}
