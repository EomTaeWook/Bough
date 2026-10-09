using System;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class MainWindowRemoteCompletionPresenter
    {
        private readonly MainWindowViewModel _model;

        public MainWindowRemoteCompletionPresenter(MainWindowViewModel model)
        {
            _model = model;
        }

        public async Task CompleteAsync(GitRepository updated, RemoteOperationStateSnapshot snapshot,
            bool worktreeMayChange, int repositoryRequestVersion)
        {
            if (_model.TryAdoptRepository(updated, repositoryRequestVersion) == false)
            {
                return;
            }
            int request = repositoryRequestVersion;
            try
            {
                if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                {
                    return;
                }
                if (snapshot != null)
                {
                    _model.RemoteOperations.ApplyOperationStateSnapshot(snapshot);
                }
                _model.MarkHistoryReferencesDirty(updated);
                Task references = _model.References.SetRepositoryAsync(updated, propagateReadError: true);
                Task history = Task.CompletedTask;
                if (_model.IsHistoryView)
                {
                    if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                    {
                        return;
                    }
                    history = _model.LoadHistoryAsync(updated, request);
                }
                Task rebase = Task.CompletedTask;
                Task revert = Task.CompletedTask;
                if (worktreeMayChange)
                {
                    if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                    {
                        return;
                    }
                    rebase = _model.RefreshRebaseStateAsync(updated, request);
                    revert = _model.RefreshRevertStateAsync(updated, request);
                }
                Exception readError = null;
                try
                {
                    await Task.WhenAll(references, history, rebase, revert);
                }
                catch (Exception exception)
                {
                    readError = exception;
                }
                if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                {
                    return;
                }
                if (worktreeMayChange)
                {
                    try
                    {
                        await _model.RefreshLocalChangesAndConflictsAsync(updated, request, propagateReadError: true);
                    }
                    catch (Exception exception)
                    {
                        if (readError == null)
                        {
                            readError = exception;
                        }
                    }
                    if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                    {
                        return;
                    }
                }
                if (readError != null)
                {
                    ExceptionDispatchInfo.Capture(readError).Throw();
                }
            }
            catch (Exception exception)
            {
                if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                {
                    return;
                }
                _model.ReportRemoteCompletionFailure(exception, updated, request);
                throw;
            }
        }
    }
}
