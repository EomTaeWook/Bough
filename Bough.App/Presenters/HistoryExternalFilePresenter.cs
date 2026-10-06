using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class HistoryExternalFilePresenter
    {
        private readonly HistoryViewModel _model;
        private readonly GitCommitFileActionService _service;
        private CancellationTokenSource _preparationCancellation;
        private int _requestVersion;

        public HistoryExternalFilePresenter(HistoryViewModel model, GitCommitFileActionService service)
        {
            _model = model;
            _service = service;
        }

        public int RequestVersion { get { return _requestVersion; } }

        public void Invalidate()
        {
            _requestVersion++;
            _preparationCancellation?.Cancel();
        }

        public async Task<GitTemporarySnapshotFile> PrepareAsync(HistoryFileActionContext context)
        {
            GitRepository repository = _model.RequireFileActionRepository(context);
            if (context.IsDirectory)
            {
                throw new GitException("CommitFileNotRegularAtRevision", null, context.Path, context.CommitHash);
            }
            if (context.IsGitlink)
            {
                throw new GitException("CommitFileNotRegularAtRevision", null, context.Path, context.CommitHash);
            }
            string revision = context.CommitHash;
            if (context.IsDeleted)
            {
                revision = context.ComparisonParent;
                if (string.IsNullOrEmpty(revision))
                {
                    throw new GitException("CommitFileAbsentAtRevision", null, context.Path, context.CommitHash);
                }
            }
            Invalidate();
            int request = _requestVersion;
            using CancellationTokenSource cancellation = new();
            _preparationCancellation = cancellation;
            GitTemporarySnapshotFile snapshot = null;
            bool accepted = false;
            try
            {
                snapshot = await _service.PrepareSnapshotFileAsync(repository, revision, context.Path, cancellation.Token);
                if (request != _requestVersion)
                {
                    return null;
                }
                _model.RequireFileActionRepository(context);
                accepted = true;
                return snapshot;
            }
            catch (Exception)
            {
                if (request != _requestVersion)
                {
                    return null;
                }
                try
                {
                    _model.RequireFileActionRepository(context);
                }
                catch (GitException)
                {
                    return null;
                }
                throw;
            }
            finally
            {
                if (ReferenceEquals(_preparationCancellation, cancellation))
                {
                    _preparationCancellation = null;
                }
                if (accepted == false)
                {
                    await _service.DiscardSnapshotFileAsync(snapshot);
                }
            }
        }
    }
}
