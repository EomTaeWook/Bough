using System.Collections.Generic;
using System.Threading.Tasks;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class HistoryFileTreePresenter
    {
        private readonly GitCommitInspectionService _inspectionService;
        private readonly Dictionary<(string RepositoryRoot, string CommitHash, string DirectoryPath), Task<GitCommitTreeListing>> _pendingLoads = [];

        public HistoryFileTreePresenter(GitCommitInspectionService inspectionService)
        {
            _inspectionService = inspectionService;
        }

        public async Task<GitCommitTreeListing> LoadAsync(GitRepository repository, string commitHash, string directoryPath = "")
        {
            (string RepositoryRoot, string CommitHash, string DirectoryPath) key = (repository.RootPath, commitHash, directoryPath);
            if (_pendingLoads.TryGetValue(key, out Task<GitCommitTreeListing> pending))
            {
                return await pending;
            }

            Task<GitCommitTreeListing> load = _inspectionService.GetTreeEntriesAsync(repository, commitHash, directoryPath);
            _pendingLoads.Add(key, load);
            try
            {
                return await load;
            }
            finally
            {
                _pendingLoads.Remove(key);
            }
        }
    }
}
