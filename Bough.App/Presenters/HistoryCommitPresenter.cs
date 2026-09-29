using System;
using System.Threading.Tasks;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class HistoryCommitPresenter
    {
        private readonly GitCommitInspectionService _inspectionService;
        private readonly GitCommitMessageService _messageService;
        private int _requestVersion;

        public HistoryCommitPresenter(GitCommitInspectionService inspectionService, GitCommitMessageService messageService)
        {
            _inspectionService = inspectionService;
            _messageService = messageService;
        }

        public void Invalidate()
        {
            _requestVersion++;
        }

        public bool IsCurrent(int requestVersion)
        {
            return requestVersion == _requestVersion;
        }

        public async Task<HistoryCommitResult> LoadAsync(GitRepository repository, string commitHash)
        {
            int requestVersion = ++_requestVersion;
            try
            {
                GitCommitInspection inspection = await _inspectionService.GetCommitAsync(repository, commitHash);
                if (IsCurrent(requestVersion) == false)
                {
                    return new HistoryCommitResult(requestVersion, false, null, null, null);
                }
                string message = await _messageService.GetMessageAsync(repository, commitHash);
                return new HistoryCommitResult(requestVersion, IsCurrent(requestVersion), inspection, message, null);
            }
            catch (Exception exception)
            {
                return new HistoryCommitResult(requestVersion, IsCurrent(requestVersion), null, null, exception);
            }
        }
    }

    public class HistoryCommitResult
    {
        public HistoryCommitResult(int requestVersion, bool isCurrent, GitCommitInspection inspection, string message, Exception error)
        {
            RequestVersion = requestVersion;
            IsCurrent = isCurrent;
            Inspection = inspection;
            Message = message;
            Error = error;
        }

        public int RequestVersion { get; }
        public bool IsCurrent { get; }
        public GitCommitInspection Inspection { get; }
        public string Message { get; }
        public Exception Error { get; }
    }
}
