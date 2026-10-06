using System;
using System.Threading.Tasks;
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
            if (_model.TryAdoptRemoteRepository(updated, repositoryRequestVersion) == false)
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
                _model.MarkRemoteHistoryReferencesDirty(updated);
                _ = _model.ObserveRepositoryAreaAsync(() => _model.References.SetRepositoryAsync(updated), request, null);
                if (_model.IsHistoryView)
                {
                    _ = _model.ObserveRepositoryAreaAsync(() => _model.LoadHistoryAsync(updated, request), request, null);
                }
                if (worktreeMayChange == false)
                {
                    return;
                }
                if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                {
                    return;
                }
                await _model.RefreshRebaseStateAsync(updated, request);
                if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                {
                    return;
                }
                await _model.RefreshLocalChangesAndConflictsAsync(updated, request);
                if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                if (_model.IsCurrentRepositoryRequest(updated, request) == false)
                {
                    return;
                }
                _model.ReportRemoteCompletionFailure(exception, updated, request);
            }
        }
    }
}
