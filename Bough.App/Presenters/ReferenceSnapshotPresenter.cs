using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class ReferenceSnapshotPresenter
    {
        private readonly GitReferenceService _referenceService;
        private GitRepository _repository;
        private int _requestVersion;
        private CancellationTokenSource _activeCancellation;

        public ReferenceSnapshotPresenter(GitReferenceService referenceService)
        {
            _referenceService = referenceService;
        }

        public int RequestVersion { get { return _requestVersion; } }
        public bool HasActiveLoad { get { return _activeCancellation != null; } }

        public void BindRepository(GitRepository repository)
        {
            _repository = repository;
            Invalidate();
        }

        public void Invalidate()
        {
            _requestVersion++;
            _activeCancellation?.Cancel();
        }

        public void TrackExternal(CancellationTokenSource cancellation)
        {
            _activeCancellation = cancellation;
        }

        public void ReleaseExternal(CancellationTokenSource cancellation)
        {
            if (_activeCancellation == cancellation)
            {
                _activeCancellation = null;
            }
        }

        public async Task<ReferenceSnapshotResult> LoadAsync(GitRepository repository)
        {
            _activeCancellation?.Cancel();
            _repository = repository;
            using CancellationTokenSource cancellation = new();
            _activeCancellation = cancellation;
            int requestVersion = ++_requestVersion;
            try
            {
                GitReferenceSnapshot snapshot = await _referenceService.GetSnapshotAsync(repository, cancellation.Token);
                return new ReferenceSnapshotResult(requestVersion, IsCurrent(requestVersion, repository), snapshot, null);
            }
            catch (OperationCanceledException)
            {
                return new ReferenceSnapshotResult(requestVersion, false, null, null);
            }
            catch (Exception exception)
            {
                return new ReferenceSnapshotResult(requestVersion, IsCurrent(requestVersion, repository), null, exception);
            }
            finally
            {
                ReleaseExternal(cancellation);
            }
        }

        private bool IsCurrent(int requestVersion, GitRepository repository)
        {
            if (requestVersion != _requestVersion)
            {
                return false;
            }
            if (_repository != repository)
            {
                return false;
            }
            return true;
        }
    }

    public class ReferenceSnapshotResult
    {
        public ReferenceSnapshotResult(int requestVersion, bool isCurrent, GitReferenceSnapshot snapshot, Exception error)
        {
            RequestVersion = requestVersion;
            IsCurrent = isCurrent;
            Snapshot = snapshot;
            Error = error;
        }

        public int RequestVersion { get; }
        public bool IsCurrent { get; }
        public GitReferenceSnapshot Snapshot { get; }
        public Exception Error { get; }
    }
}
