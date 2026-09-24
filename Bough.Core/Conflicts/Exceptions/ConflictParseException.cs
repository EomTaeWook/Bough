using Bough.Core.Git;

namespace Bough.Core.Conflicts.Exceptions
{
    public class ConflictParseException : GitException
    {
        public ConflictParseException(string errorCode, params object[] arguments)
            : base(errorCode, null, arguments)
        {
        }
    }
}
