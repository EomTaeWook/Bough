using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.ViewModels.Models;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class TagDeletionDialogPresenter
    {
        private readonly Func<string, CancellationToken, Task<GitRemoteTagDeletionPreview>> _lookup;
        private readonly Func<Task<string>> _deleteLocal;
        private readonly Func<GitRemoteTagDeletionPreview, Task<string>> _deleteRemote;
        private CancellationTokenSource _lookupCancellation;
        private int _lookupVersion;

        public TagDeletionDialogPresenter(string remoteName,
            Func<string, CancellationToken, Task<GitRemoteTagDeletionPreview>> lookup,
            Func<Task<string>> deleteLocal, Func<GitRemoteTagDeletionPreview, Task<string>> deleteRemote)
        {
            _lookup = lookup;
            _deleteLocal = deleteLocal;
            _deleteRemote = deleteRemote;
            State = new TagDeletionDialogModel { RemoteName = remoteName ?? string.Empty };
        }

        public event Action StateChanged;
        public TagDeletionDialogModel State { get; }

        public Task SelectScopeAsync(bool remote)
        {
            if (State.IsClosed)
            {
                return Task.CompletedTask;
            }
            if (State.IsDeleting)
            {
                return Task.CompletedTask;
            }
            if (State.IsRemoteMode == remote)
            {
                return Task.CompletedTask;
            }
            InvalidatePreview();
            State.IsRemoteMode = remote;
            StateChanged?.Invoke();
            if (remote == false)
            {
                return Task.CompletedTask;
            }
            return ReloadAsync();
        }

        public Task SelectRemoteAsync(string remoteName)
        {
            if (State.IsClosed)
            {
                return Task.CompletedTask;
            }
            if (State.IsDeleting)
            {
                return Task.CompletedTask;
            }
            if (State.RemoteName == remoteName)
            {
                return Task.CompletedTask;
            }
            InvalidatePreview();
            State.RemoteName = remoteName ?? string.Empty;
            StateChanged?.Invoke();
            return ReloadAsync();
        }

        public void ConfirmRemote(bool confirmed)
        {
            if (State.IsClosed)
            {
                return;
            }
            if (State.IsDeleting)
            {
                return;
            }
            if (State.RemotePreview == null)
            {
                confirmed = false;
            }
            if (State.RemoteConfirmed == confirmed)
            {
                return;
            }
            State.RemoteConfirmed = confirmed;
            StateChanged?.Invoke();
        }

        public async Task ReloadAsync()
        {
            if (State.IsClosed)
            {
                return;
            }
            if (State.IsDeleting)
            {
                return;
            }
            if (State.IsRemoteMode == false)
            {
                return;
            }
            InvalidatePreview();
            if (string.IsNullOrEmpty(State.RemoteName))
            {
                StateChanged?.Invoke();
                return;
            }
            string remoteName = State.RemoteName;
            int request = _lookupVersion;
            CancellationTokenSource cancellation = new();
            _lookupCancellation = cancellation;
            State.IsLookingUp = true;
            StateChanged?.Invoke();
            try
            {
                GitRemoteTagDeletionPreview preview = await _lookup(remoteName, cancellation.Token);
                if (request != _lookupVersion)
                {
                    return;
                }
                if (State.IsClosed)
                {
                    return;
                }
                if (State.IsRemoteMode == false)
                {
                    return;
                }
                if (State.RemoteName != remoteName)
                {
                    return;
                }
                State.RemotePreview = preview;
                State.RemoteConfirmed = false;
            }
            catch (Exception exception)
            {
                if (request != _lookupVersion)
                {
                    return;
                }
                if (State.IsClosed)
                {
                    return;
                }
                State.Error = exception;
            }
            finally
            {
                if (ReferenceEquals(_lookupCancellation, cancellation))
                {
                    _lookupCancellation = null;
                }
                cancellation.Dispose();
                if (request == _lookupVersion)
                {
                    State.IsLookingUp = false;
                    if (State.IsClosed == false)
                    {
                        StateChanged?.Invoke();
                    }
                }
            }
        }

        public async Task<bool> DeleteAsync()
        {
            if (State.CanDelete == false)
            {
                return false;
            }
            GitRemoteTagDeletionPreview preview = State.RemotePreview;
            bool remote = State.IsRemoteMode;
            State.Error = null;
            State.FailureMessage = null;
            State.IsDeleting = true;
            StateChanged?.Invoke();
            try
            {
                string failure;
                if (remote)
                {
                    failure = await _deleteRemote(preview);
                }
                else
                {
                    failure = await _deleteLocal();
                }
                if (failure == null)
                {
                    return true;
                }
                State.FailureMessage = failure;
            }
            catch (Exception exception)
            {
                State.Error = exception;
            }
            finally
            {
                State.IsDeleting = false;
                if (remote)
                {
                    State.RemotePreview = null;
                    State.RemoteConfirmed = false;
                }
                StateChanged?.Invoke();
            }
            return false;
        }

        public void Close()
        {
            State.IsClosed = true;
            InvalidatePreview();
        }

        private void InvalidatePreview()
        {
            _lookupVersion++;
            _lookupCancellation?.Cancel();
            _lookupCancellation = null;
            State.RemotePreview = null;
            State.RemoteConfirmed = false;
            State.IsLookingUp = false;
            State.Error = null;
            State.FailureMessage = null;
        }
    }
}
