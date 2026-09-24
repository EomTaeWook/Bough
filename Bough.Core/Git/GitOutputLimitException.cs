namespace Bough.Core.Git
{
    public class GitOutputLimitException : GitException
    {
        public GitOutputLimitException(int maxBytes)
            : base($"Git output exceeds {maxBytes} bytes.")
        {
        }
    }
}
