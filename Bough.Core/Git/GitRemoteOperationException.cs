namespace Bough.Core.Git
{
    public class GitRemoteOperationException : GitException
    {
        public GitRemoteOperationException(string errorCode, string details, params object[] arguments)
            : base(errorCode, null, arguments)
        {
            Details = details;
        }

        public string Details { get; }
    }
}
