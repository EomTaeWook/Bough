using System;
using System.Linq;
using System.Threading.Tasks;
using Bough.App.Internals;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.Presenters
{
    public class HistoryNavigationPresenter
    {
        private readonly HistoryViewModel _model;
        private int _requestVersion;

        public HistoryNavigationPresenter(HistoryViewModel model)
        {
            _model = model;
        }

        public void Invalidate()
        {
            _requestVersion++;
        }

        public bool IsCurrent(int requestVersion)
        {
            return requestVersion == _requestVersion;
        }

        public async Task<HistoryCommitSelectionResult> SelectAsync(string commitHash)
        {
            int request = ++_requestVersion;
            GitRepository repository = _model.CurrentRepository;
            GitHistoryScope scope = _model.SelectedScope;
            string root = string.Empty;
            if (repository != null)
            {
                root = repository.RootPath;
            }
            HistoryCommitSelectionResult Result(HistoryCommitSelectionOutcome outcome, Exception error = null)
            {
                return new HistoryCommitSelectionResult(root, commitHash, scope, outcome, error, request);
            }
            if (repository == null)
            {
                return Result(HistoryCommitSelectionOutcome.Failed, new GitException("HistoryRepositoryRequired", null, Array.Empty<object>()));
            }
            try
            {
                while (true)
                {
                    await _model.WaitForHistoryListAsync();
                    if (IsCurrent(request) == false)
                    {
                        return Result(HistoryCommitSelectionOutcome.Superseded);
                    }
                    if (_model.CurrentRepository != repository)
                    {
                        return Result(HistoryCommitSelectionOutcome.Superseded);
                    }
                    if (_model.SelectedScope != scope)
                    {
                        return Result(HistoryCommitSelectionOutcome.Superseded);
                    }
                    Exception error = _model.HistoryQueryError;
                    if (error != null)
                    {
                        return Result(HistoryCommitSelectionOutcome.Failed, error);
                    }
                    if (_model.HasLoadedHistory == false)
                    {
                        await _model.LoadNavigationFirstPageAsync();
                        continue;
                    }
                    HistoryCommitItem match = _model.Commits.FirstOrDefault(item => item.Hash == commitHash);
                    if (match != null)
                    {
                        _model.ApplyNavigationSelection(match);
                        if (IsCurrent(request) == false)
                        {
                            return Result(HistoryCommitSelectionOutcome.Superseded);
                        }
                        if (_model.CurrentRepository != repository)
                        {
                            return Result(HistoryCommitSelectionOutcome.Superseded);
                        }
                        if (_model.SelectedScope != scope)
                        {
                            return Result(HistoryCommitSelectionOutcome.Superseded);
                        }
                        return Result(HistoryCommitSelectionOutcome.Found);
                    }
                    if (_model.HasMore == false)
                    {
                        return Result(HistoryCommitSelectionOutcome.NotFoundInScope);
                    }
                    int count = _model.Commits.Count;
                    await _model.LoadNavigationNextPageAsync();
                    if (IsCurrent(request) == false)
                    {
                        return Result(HistoryCommitSelectionOutcome.Superseded);
                    }
                    if (_model.CurrentRepository != repository)
                    {
                        return Result(HistoryCommitSelectionOutcome.Superseded);
                    }
                    if (_model.SelectedScope != scope)
                    {
                        return Result(HistoryCommitSelectionOutcome.Superseded);
                    }
                    if (_model.HistoryQueryError != null)
                    {
                        return Result(HistoryCommitSelectionOutcome.Failed, _model.HistoryQueryError);
                    }
                    if (_model.Commits.Count == count)
                    {
                        if (_model.HasMore)
                        {
                            return Result(HistoryCommitSelectionOutcome.Superseded);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                if (IsCurrent(request) == false)
                {
                    return Result(HistoryCommitSelectionOutcome.Superseded, exception);
                }
                if (_model.CurrentRepository != repository)
                {
                    return Result(HistoryCommitSelectionOutcome.Superseded, exception);
                }
                if (_model.SelectedScope != scope)
                {
                    return Result(HistoryCommitSelectionOutcome.Superseded, exception);
                }
                return Result(HistoryCommitSelectionOutcome.Failed, exception);
            }
        }
    }
}
