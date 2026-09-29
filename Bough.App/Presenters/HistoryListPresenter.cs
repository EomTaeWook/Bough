using System;
using System.Threading.Tasks;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.Presenters
{
    public class HistoryListPresenter
    {
        private readonly GitHistoryService _historyService;
        private int _requestVersion;

        public HistoryListPresenter(GitHistoryService historyService)
        {
            _historyService = historyService;
        }

        public void Invalidate()
        {
            _requestVersion++;
        }

        public bool IsCurrent(int requestVersion)
        {
            return requestVersion == _requestVersion;
        }

        public async Task<HistoryListResult> LoadFirstAsync(GitRepository repository, GitHistoryScope scope)
        {
            int requestVersion = ++_requestVersion;
            try
            {
                GitHistoryPage page = await _historyService.GetHistoryPageAsync(repository, 0, 200, scope);
                return new HistoryListResult(requestVersion, IsCurrent(requestVersion), page, null);
            }
            catch (Exception exception)
            {
                return new HistoryListResult(requestVersion, IsCurrent(requestVersion), null, exception);
            }
        }

        public async Task<HistoryListResult> LoadNextAsync(GitRepository repository, GitHistoryScope scope, int existingCount, string anchorHash)
        {
            int requestVersion = ++_requestVersion;
            try
            {
                GitHistoryPage page = await _historyService.GetHistoryPageAsync(repository, existingCount - 1, 201, scope);
                if (IsCurrent(requestVersion) == false)
                {
                    return new HistoryListResult(requestVersion, false, null, null);
                }
                if (page.Commits.Count <= 1)
                {
                    throw new GitException("HistoryCommitListChanged", null, Array.Empty<object>());
                }
                if (page.Commits[0].Hash != anchorHash)
                {
                    throw new GitException("HistoryCommitListChanged", null, Array.Empty<object>());
                }
                return new HistoryListResult(requestVersion, true, page, null);
            }
            catch (Exception exception)
            {
                return new HistoryListResult(requestVersion, IsCurrent(requestVersion), null, exception);
            }
        }
    }

    public class HistoryListResult
    {
        public HistoryListResult(int requestVersion, bool isCurrent, GitHistoryPage page, Exception error)
        {
            RequestVersion = requestVersion;
            IsCurrent = isCurrent;
            Page = page;
            Error = error;
        }

        public int RequestVersion { get; }
        public bool IsCurrent { get; }
        public GitHistoryPage Page { get; }
        public Exception Error { get; }
    }
}
