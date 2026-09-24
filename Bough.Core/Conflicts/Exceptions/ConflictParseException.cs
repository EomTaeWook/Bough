using System;

namespace Bough.Core.Conflicts.Exceptions
{
    public class ConflictParseException : Exception
    {
        public ConflictParseException(string message)
            : base(message)
        {
        }
    }
}
