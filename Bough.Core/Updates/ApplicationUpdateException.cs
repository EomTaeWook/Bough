using System;
using Bough.Core.Git;

namespace Bough.Core.Updates
{
    // Uses the existing code/arguments display boundary; no raw network diagnostic is displayed.
    public class ApplicationUpdateException : GitException
    {
        public ApplicationUpdateException(string code, Exception innerException, params object[] arguments)
            : base(code, innerException, arguments)
        {
        }

        public Uri ReleasePage { get; internal set; }
    }
}