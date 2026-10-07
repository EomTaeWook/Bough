using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bough.App.Localization;
using Bough.App.ViewModels;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.Presenters
{
    public class MainWindowRevertPresenter
    {
        private readonly HistoryActionPresenter _actions;
        private readonly MainWindowViewModel _model;
        private readonly StringHelper _strings;
        private readonly StringComparer _pathComparer;
        private int _readVersion;

        public MainWindowRevertPresenter(GitCommitActionService actionService, GitOperationQueue operationQueue,
            MainWindowViewModel model, StringHelper strings, StringComparer pathComparer)
        {
            _actions = new HistoryActionPresenter(actionService, operationQueue);
            _model = model;
            _strings = strings;
            _pathComparer = pathComparer;
        }

        public void ClearState()
        {
            _readVersion++;
            _model.SetRevertState(null, null);
            _model.IsRevertStateLoading = false;
            _model.NotifyRevertState();
        }

        public async Task RefreshStateAsync(GitRepository repository, int request, bool applyConflicts = false)
        {
            if (_model.IsCurrentRepositoryRequest(repository, request) == false)
            {
                return;
            }
            int read = ++_readVersion;
            _model.IsRevertStateLoading = true;
            _model.NotifyRevertState();
            try
            {
                GitRevertState state = await _actions.GetRevertStateAsync(repository);
                if (read != _readVersion)
                {
                    return;
                }
                if (_model.IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
                if (_pathComparer.Equals(state.RepositoryRoot, repository.RootPath) == false)
                {
                    return;
                }
                _model.SetRevertState(repository, state);
                _model.NotifyRevertState();
                if (applyConflicts)
                {
                    if (state.IsInProgress)
                    {
                        await _model.ApplyConflictPathsAsync(repository, state.ConflictPaths);
                        if (read != _readVersion)
                        {
                            return;
                        }
                        if (_model.IsCurrentRepositoryRequest(repository, request) == false)
                        {
                            return;
                        }
                    }
                }
            }
            catch
            {
                if (read != _readVersion)
                {
                    return;
                }
                if (_model.IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
                throw;
            }
            finally
            {
                if (read == _readVersion)
                {
                    if (_model.IsCurrentRepositoryRequest(repository, request))
                    {
                        _model.IsRevertStateLoading = false;
                        _model.NotifyRevertState();
                    }
                }
            }
        }

        public async Task ContinueAsync()
        {
            if (_model.CanContinueRevert == false)
            {
                return;
            }
            GitRepository repository = _model.CurrentRepository;
            GitRevertState state = _model.RevertState;
            int request = _model.RepositoryRequestVersion;
            try
            {
                GitRevertResult result = await _actions.ContinueRevertAsync(repository, state,
                    _strings.GetString("MainRevertContinueAction"),
                    completion => ApplyRevertResultAsync(repository, completion, request, null, null));
                _model.ReportRevertResult(repository, result, request, state.TargetHash);
            }
            catch (Exception exception)
            {
                if (_model.IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
                _model.ReportRevertFailure(exception);
            }
        }

        public async Task<GitRevertResult> AbortAsync(GitRepository repository, GitRevertState state,
            int request, string confirmedPath, string confirmedDraft)
        {
            if (_model.IsCurrentRepositoryRequest(repository, request) == false)
            {
                return null;
            }
            if (ReferenceEquals(_model.RevertState, state) == false)
            {
                return null;
            }
            if (_model.CanAbortRevert == false)
            {
                return null;
            }
            try
            {
                GitRevertResult result = await _actions.AbortRevertAsync(repository, state,
                    _strings.GetString("MainRevertAbortAction"),
                    completion => ApplyRevertResultAsync(repository, completion, request, confirmedPath, confirmedDraft));
                _model.ReportRevertResult(repository, result, request, state.TargetHash);
                return result;
            }
            catch (Exception exception)
            {
                if (_model.IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return null;
                }
                _model.ReportRevertFailure(exception);
                return null;
            }
        }

        private async Task ApplyRevertResultAsync(GitRepository original, GitRevertResult result, int request,
            string confirmedPath, string confirmedDraft)
        {
            if (_pathComparer.Equals(result.RepositoryRoot, original.RootPath) == false)
            {
                throw new GitException("CommitRevertRepositoryChanged", null, Array.Empty<object>());
            }
            // The original root remains dirty even if its completion belongs to an older screen.
            _model.MarkHistoryReferencesDirty(original);
            if (_model.IsCurrentRepositoryRequest(original, request) == false)
            {
                return;
            }
            GitRepository repository = original;
            if (result.Repository != null)
            {
                if (_pathComparer.Equals(result.Repository.RootPath, original.RootPath) == false)
                {
                    throw new GitException("CommitRevertRepositoryChanged", null, Array.Empty<object>());
                }
                repository = result.Repository;
                if (_model.TryAdoptRepository(repository, request) == false)
                {
                    return;
                }
            }
            _readVersion++;
            _model.IsRevertStateLoading = false;
            if (result.State != null)
            {
                if (_pathComparer.Equals(result.State.RepositoryRoot, original.RootPath) == false)
                {
                    throw new GitException("CommitRevertRepositoryChanged", null, Array.Empty<object>());
                }
                _model.SetRevertState(repository, result.State);
            }
            _model.NotifyRevertState();
            if (result.Outcome == GitRevertOutcome.Aborted)
            {
                if (result.State != null)
                {
                    if (result.State.IsInProgress == false)
                    {
                        if (_model.Conflicts.CurrentFilePath == confirmedPath)
                        {
                            if (_model.Conflicts.ResultText == confirmedDraft)
                            {
                                _model.Conflicts.DiscardClosedWindowEdits();
                            }
                        }
                    }
                }
            }
            if (_model.IsCurrentRepositoryRequest(repository, request) == false)
            {
                return;
            }
            _model.IsLocalChangesLoading = true;
            try
            {
                await _model.LocalChanges.LoadWorktreeAsync(repository);
                if (_model.IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
                IReadOnlyList<string> paths = _model.LocalChanges.ConflictFiles.Select(file => file.Path).ToArray();
                if (result.State != null)
                {
                    paths = result.State.ConflictPaths;
                }
                await _model.ApplyConflictPathsAsync(repository, paths);
                if (_model.IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
            }
            finally
            {
                if (_model.IsCurrentRepositoryRequest(repository, request))
                {
                    _model.IsLocalChangesLoading = false;
                }
            }
            if (_model.IsCurrentRepositoryRequest(repository, request) == false)
            {
                return;
            }
            await _model.References.SetRepositoryAsync(repository);
            if (_model.IsCurrentRepositoryRequest(repository, request) == false)
            {
                return;
            }
            await _model.RemoteOperations.SetRepositoryAsync(repository);
            if (_model.IsCurrentRepositoryRequest(repository, request) == false)
            {
                return;
            }
            if (_model.IsHistoryView)
            {
                await _model.LoadHistoryAsync(repository, request);
                if (_model.IsCurrentRepositoryRequest(repository, request) == false)
                {
                    return;
                }
            }
        }
    }
}
