using System;
using System.Collections.Generic;

namespace Bough.Core.Git
{
    public class GitException : Exception
    {
        public const string ProcessStartFailedCode = "GitProcessStartFailed";
        public const string ExitWithoutErrorMessageCode = "GitExitedWithoutErrorMessage";

        public string ErrorCode { get; }
        public IReadOnlyList<object> Arguments { get; }

        public GitException(string message)
            : base(message)
        {
            Arguments = Array.Empty<object>();
        }

        public GitException(string message, Exception innerException)
            : base(message, innerException)
        {
            Arguments = Array.Empty<object>();
        }

        public GitException(string errorCode, Exception innerException, params object[] arguments)
            : base(errorCode, innerException)
        {
            ErrorCode = errorCode;
            object[] values = Array.Empty<object>();
            if (arguments != null)
            {
                values = (object[])arguments.Clone();
            }
            Arguments = Array.AsReadOnly(values);
        }
    }
}
