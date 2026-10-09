using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.ViewModels;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class LocalChangesPreviewPresenter
    {
        private readonly LocalChangesViewModel _screen;
        private readonly GitWorkingTreeService _service;
        private GitRepository _pendingRepository;
        private GitWorktreeFile _pendingFile;
        private bool _pendingStaged;
        private int _pendingRequestVersion;
        private int _version;
        private Task _consumerTask;
        private CancellationTokenSource _activeCancellation;
        private bool _suspended;
        private bool _resumeNeeded;

        public LocalChangesPreviewPresenter(LocalChangesViewModel screen, GitWorkingTreeService service)
        {
            _screen = screen;
            _service = service;
        }

        public void Request(GitWorktreeFile file, bool staged)
        {
            Invalidate();
            _pendingRepository = _screen.CurrentRepository;
            if (_pendingRepository == null)
            {
                return;
            }
            _pendingFile = file;
            _pendingStaged = staged;
            _pendingRequestVersion = _screen.WorktreeRequestVersion;
            _screen.BeginPreviewDisplay();
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
            _pendingFile = null;
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
            _screen.RequestSelectedPreview();
        }

        private async Task ConsumeAsync()
        {
            await Task.Yield();
            try
            {
                while (_pendingFile != null)
                {
                    if (_suspended)
                    {
                        return;
                    }
                    GitRepository repository = _pendingRepository;
                    GitWorktreeFile file = _pendingFile;
                    bool staged = _pendingStaged;
                    int requestVersion = _pendingRequestVersion;
                    int version = _version;
                    _pendingFile = null;
                    using CancellationTokenSource cancellation = new();
                    _activeCancellation = cancellation;
                    try
                    {
                        if (IsCurrent(repository, file, staged, requestVersion, version) == false)
                        {
                            continue;
                        }
                        GitFilePreview preview = await Task.Run(() => _service.GetPreviewAsync(repository, file, staged, cancellation.Token), cancellation.Token);
                        if (IsCurrent(repository, file, staged, requestVersion, version) == false)
                        {
                            continue;
                        }
                        _screen.ApplyPreviewDisplay(file, preview);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                    }
                    catch (Exception exception)
                    {
                        if (IsCurrent(repository, file, staged, requestVersion, version))
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

        private bool IsCurrent(GitRepository repository, GitWorktreeFile file, bool staged, int requestVersion, int version)
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
            if (requestVersion != _screen.WorktreeRequestVersion)
            {
                return false;
            }
            if (_screen.IsPreviewSelection(file, staged) == false)
            {
                return false;
            }
            return true;
        }
    }
}
