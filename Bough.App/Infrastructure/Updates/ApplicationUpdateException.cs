using System;
using System.Collections.Generic;

namespace Bough.App.Infrastructure.Updates
{
    public class ApplicationUpdateException : Exception
    {
        public ApplicationUpdateException(string code, Exception innerException, params object[] arguments)
            : base(code, innerException)
        {
            ErrorCode = code;
            object[] values = Array.Empty<object>();
            if (arguments != null)
            {
                values = (object[])arguments.Clone();
            }
            Arguments = Array.AsReadOnly(values);
        }

        public string ErrorCode { get; }
        public IReadOnlyList<object> Arguments { get; }
        public Uri ReleasePage { get; internal set; }
    }
}
