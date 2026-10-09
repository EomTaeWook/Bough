using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.ViewModels;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class StashPreviewPresenter
    {
        private readonly StashViewModel _screen;
        private readonly GitStashService _service;
        private GitRepository _pendingRepository;
        private GitStashEntry _pendingEntry;
        private int _pendingRequestVersion;
        private int _version;
        private Task _consumerTask;
        private CancellationTokenSource _activeCancellation;
        private bool _suspended;
        private bool _resumeNeeded;

        public StashPreviewPresenter(StashViewModel screen, GitStashService service)
        {
            _screen = screen;
            _service = service;
        }

        public void Request(GitStashEntry entry)
        {
            Invalidate();
            _pendingRepository = _screen.CurrentRepository;
            if (_pendingRepository == null)
            {
                return;
            }
            _pendingEntry = entry;
            _pendingRequestVersion = _screen.RequestVersion;
            if (_suspended)
            {
                _resumeNeeded = true;
                return;
            }
            if (_consumerTask == null)
            {
                _consumerTask = ConsumeAsync();
            }
        }

        public void Invalidate()
        {
            _version++;
            _pendingEntry = null;
            _pendingRepository = null;
            _resumeNeeded = false;
            _activeCancellation?.Cancel();
        }

        public void Suspend()
        {
            bool resumeNeeded = _consumerTask != null;
            if (_resumeNeeded)
            {
                resumeNeeded = true;
            }
            Invalidate();
            _suspended = true;
            _resumeNeeded = resumeNeeded;
        }

        public void Resume()
        {
            _suspended = false;
            if (_resumeNeeded == false)
            {
                return;
            }
            _resumeNeeded = false;
            if (_screen.SelectedStash == null)
            {
                return;
            }
            Request(_screen.SelectedStash);
        }

        private async Task ConsumeAsync()
        {
            await Task.Yield();
            try
            {
                while (_pendingEntry != null)
                {
                    if (_suspended)
                    {
                        return;
                    }
                    GitRepository repository = _pendingRepository;
                    GitStashEntry entry = _pendingEntry;
                    int requestVersion = _pendingRequestVersion;
                    int version = _version;
                    _pendingEntry = null;
                    using CancellationTokenSource cancellation = new();
                    _activeCancellation = cancellation;
                    try
                    {
                        if (IsCurrent(repository, entry, requestVersion, version) == false)
                        {
                            continue;
                        }
                        GitStashPreview preview = await Task.Run(() => _service.GetPreviewAsync(repository, entry, cancellation.Token), cancellation.Token);
                        if (IsCurrent(repository, entry, requestVersion, version) == false)
                        {
                            continue;
                        }
                        _screen.ApplyPreviewDisplay(preview);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                    }
                    catch (Exception exception)
                    {
                        if (IsCurrent(repository, entry, requestVersion, version))
                        {
                            _screen.ApplyPreviewError(exception);
                        }
                    }
                    finally
                    {
                        _activeCancellation = null;
                    }
                }
            }
            finally
            {
                _consumerTask = null;
            }
        }

        private bool IsCurrent(GitRepository repository, GitStashEntry entry, int requestVersion, int version)
        {
            if (version != _version)
            {
                return false;
            }
            if (_suspended)
            {
                return false;
            }
            if (ReferenceEquals(repository, _screen.CurrentRepository) == false)
            {
                return false;
            }
            if (requestVersion != _screen.RequestVersion)
            {
                return false;
            }
            if (ReferenceEquals(entry, _screen.SelectedStash) == false)
            {
                return false;
            }
            return true;
        }
    }
}
