using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class HistoryPreviewPresenter
    {
        private readonly HistoryViewModel _model;
        private readonly GitCommitInspectionService _service;
        private HistoryPreviewRequest _pendingRequest;
        private HistoryPreviewRequest _latestRequest;
        private HistoryPreviewRequest _suspendedRequest;
        private int _pendingVersion;
        private int _requestVersion;
        private Task _consumerTask;
        private CancellationTokenSource _activeCancellation;
        private Func<Action, CancellationToken, Task> _presentationScheduler;

        public HistoryPreviewPresenter(HistoryViewModel model, GitCommitInspectionService service)
        {
            _model = model;
            _service = service;
        }

        public void SetPresentationScheduler(Func<Action, CancellationToken, Task> scheduler)
        {
            _presentationScheduler = scheduler;
        }

        public Task RequestAsync(HistoryPreviewRequest request)
        {
            _suspendedRequest = null;
            _requestVersion++;
            _pendingVersion = _requestVersion;
            _pendingRequest = request;
            _latestRequest = request;
            _activeCancellation?.Cancel();
            if (_consumerTask == null)
            {
                _consumerTask = ConsumeAsync();
            }
            return _consumerTask;
        }

        public void Invalidate()
        {
            _requestVersion++;
            _pendingRequest = null;
            _latestRequest = null;
            _suspendedRequest = null;
            _activeCancellation?.Cancel();
        }

        public void Suspend()
        {
            HistoryPreviewRequest request = _suspendedRequest;
            if (_consumerTask != null)
            {
                if (_latestRequest != null)
                {
                    request = _latestRequest;
                }
            }
            Invalidate();
            _suspendedRequest = request;
        }

        public void Resume()
        {
            HistoryPreviewRequest request = _suspendedRequest;
            _suspendedRequest = null;
            if (request == null)
            {
                return;
            }
            _ = RequestAsync(request);
        }

        public void Clear()
        {
            _ = RequestAsync(new HistoryPreviewRequest());
        }

        public void ClearChangedSelection()
        {
            if (_latestRequest != null)
            {
                if (_latestRequest.IsExplicit)
                {
                    return;
                }
            }
            Clear();
        }

        public void ClearTreeSelection()
        {
            if (_latestRequest == null)
            {
                return;
            }
            if (_latestRequest.TreeSelection == null)
            {
                return;
            }
            Clear();
        }

        private async Task ConsumeAsync()
        {
            await Task.Yield();
            try
            {
                while (_pendingRequest != null)
                {
                    HistoryPreviewRequest request = _pendingRequest;
                    int version = _pendingVersion;
                    _pendingRequest = null;
                    using CancellationTokenSource cancellation = new();
                    _activeCancellation = cancellation;
                    try
                    {
                        if (IsCurrent(request, version) == false)
                        {
                            continue;
                        }
                        if (request.IsClear)
                        {
                            await PresentAsync(request, version, _model.ClearPreviewDisplay, cancellation.Token);
                            continue;
                        }
                        await PresentAsync(request, version, () => _model.BeginPreviewDisplay(request), cancellation.Token);
                        if (IsCurrent(request, version) == false)
                        {
                            continue;
                        }
                        if (string.IsNullOrEmpty(request.Revision))
                        {
                            await PresentAsync(request, version, _model.ShowAbsentPreview, cancellation.Token);
                            continue;
                        }
                        GitRepository repository = request.Repository;
                        string revision = request.Revision;
                        string path = request.Path;
                        CancellationToken token = cancellation.Token;
                        GitCommitFileContent content = null;
                        Exception error = null;
                        try
                        {
                            content = await Task.Run(() => _service.GetFileContentAsync(repository, revision, path, token), token);
                        }
                        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                        {
                            continue;
                        }
                        catch (Exception exception)
                        {
                            error = exception;
                        }
                        if (IsCurrent(request, version) == false)
                        {
                            continue;
                        }
                        if (error != null)
                        {
                            await PresentAsync(request, version, () => _model.ShowPreviewError(error), cancellation.Token);
                            continue;
                        }
                        await PresentAsync(request, version, () => _model.ApplyPreviewContent(content), cancellation.Token);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                    }
                    finally
                    {
                        if (ReferenceEquals(_activeCancellation, cancellation))
                        {
                            _activeCancellation = null;
                        }
                    }
                }
            }
            finally
            {
                _consumerTask = null;
            }
        }

        private Task PresentAsync(HistoryPreviewRequest request, int version, Action apply, CancellationToken cancellationToken)
        {
            Action guardedApply = () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsCurrent(request, version) == false)
                {
                    return;
                }
                apply();
            };
            if (_presentationScheduler != null)
            {
                return _presentationScheduler(guardedApply, cancellationToken);
            }
            guardedApply();
            return Task.CompletedTask;
        }

        private bool IsCurrent(HistoryPreviewRequest request, int version)
        {
            if (version != _requestVersion)
            {
                return false;
            }
            if (ReferenceEquals(request, _latestRequest) == false)
            {
                return false;
            }
            if (request.IsClear)
            {
                return true;
            }
            if (ReferenceEquals(request.Repository, _model.CurrentRepository) == false)
            {
                return false;
            }
            if (ReferenceEquals(request.Inspection, _model.Inspection) == false)
            {
                return false;
            }
            if (request.InspectionVersion != _model.PreviewInspectionVersion)
            {
                return false;
            }
            if (request.Parent != _model.SelectedParent)
            {
                return false;
            }
            if (request.IsExplicit == false)
            {
                if (ReferenceEquals(request.Selection, _model.SelectedChangedFile) == false)
                {
                    return false;
                }
            }
            if (request.TreeSelection != null)
            {
                if (ReferenceEquals(request.TreeSelection, _model.SelectedTreeFile) == false)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
