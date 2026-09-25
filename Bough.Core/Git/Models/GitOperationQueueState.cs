namespace Bough.Core.Git.Models
{
    public class GitOperationQueueState
    {
        public GitOperationQueueState(string repositoryRoot, string runningOperationName, int pendingCount)
        {
            RepositoryRoot = repositoryRoot;
            RunningOperationName = runningOperationName;
            PendingCount = pendingCount;
        }

        public string RepositoryRoot { get; }
        public string RunningOperationName { get; }
        public bool IsRunning { get { return RunningOperationName.Length > 0; } }
        public int PendingCount { get; }
    }
}
