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
            if (snapshot != null)
            {
                _model.RemoteOperations.ApplyOperationStateSnapshot(snapshot);
            }

            int request = _model.RepositoryRequestVersion;
            _ = _model.ObserveRepositoryAreaAsync(() => _model.References.SetRepositoryAsync(updated), request, null);
            if (_model.IsHistoryView)
            {
                _ = _model.ObserveRepositoryAreaAsync(() => _model.History.LoadAsync(updated), request, null);
            }
            if (worktreeMayChange == false)
            {
                return;
            }

            try
            {
                await _model.RefreshRebaseStateAsync(updated, request);
                await _model.RefreshLocalChangesAndConflictsAsync(updated, request);
            }
            catch (Exception exception)
            {
                _model.ReportRemoteCompletionFailure(exception, request);
            }
        }
    }
}
