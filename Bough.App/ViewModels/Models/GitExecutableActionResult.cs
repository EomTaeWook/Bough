using Bough.App.Internals;
using System;

namespace Bough.App.ViewModels.Models
{
    public class GitExecutableActionResult
    {
        public GitExecutableActionResult(GitExecutableActionKind kind, string version, string configuredPath, Exception error)
        {
            Kind = kind;
            Version = version;
            ConfiguredPath = configuredPath;
            Error = error;
        }

        public GitExecutableActionKind Kind { get; }
        public string Version { get; }
        public string ConfiguredPath { get; }
        public Exception Error { get; }
    }
}
