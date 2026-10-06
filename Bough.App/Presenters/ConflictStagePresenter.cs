using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Internals;
using Bough.App.Interfaces;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class ConflictStagePresenter
    {
        private readonly GitRepositoryService _repositoryService;
        private readonly GitOperationQueue _operationQueue;
        private readonly ConflictResolutionViewModel _model;
        private IConflictStageCompletion _completion;
        private int _requestVersion;

        public ConflictStagePresenter(GitRepositoryService repositoryService, GitOperationQueue operationQueue,
            ConflictResolutionViewModel model)
        {
            _repositoryService = repositoryService;
            _operationQueue = operationQueue;
            _model = model;
        }

        public void SetCompletion(IConflictStageCompletion completion)
        {
            ArgumentNullException.ThrowIfNull(completion);
            _completion = completion;
        }

        public void Invalidate()
        {
            _requestVersion++;
            _model.SetStageResult(null);
        }

        public async Task<ConflictStageResult> SaveAndStageAsync(string operationName)
        {
            GitRepository repository = _model.StageRepository;
            GitConflictFile conflict = _model.StageConflict;
            if (_completion == null)
            {
                return PublishFailure(repository, conflict?.RelativePath ?? string.Empty, "ConflictStageUnavailable");
            }
            if (repository == null)
            {
                return PublishFailure(null, string.Empty, "OpenRepositoryToFindConflicts");
            }
            if (conflict == null)
            {
                return PublishFailure(repository, string.Empty, "SelectConflictFile");
            }

            int request = _requestVersion;
            string resultText = _model.ResultText;
            string path = conflict.RelativePath;
            ConflictStageResult result;
            try
            {
                result = await _operationQueue.EnqueueAsync(repository.RootPath, $"{operationName} · {path}",
                    token => ExecuteAsync(repository, conflict, resultText, request, token));
            }
            catch (OperationCanceledException exception)
            {
                result = new ConflictStageResult(repository, path, ConflictStageOutcome.Canceled, exception: exception);
            }
            catch (Exception exception)
            {
                result = new ConflictStageResult(repository, path, ConflictStageOutcome.Failed, exception: exception);
            }

            if (IsCurrent(repository, conflict, request) == false)
            {
                return result;
            }
            if (result.Succeeded)
            {
                _model.MarkStageSaved(conflict, resultText);
                try
                {
                    await _completion.CompleteConflictStageAsync(repository, path);
                }
                catch (Exception exception)
                {
                    result = new ConflictStageResult(repository, path, ConflictStageOutcome.RefreshFailed, exception: exception);
                }
            }

            bool staleConflict = result.Outcome == ConflictStageOutcome.NoLongerConflicted ||
                result.Outcome == ConflictStageOutcome.FileChanged;
            if (staleConflict)
            {
                try
                {
                    await _completion.RefreshConflictStateAsync(repository);
                }
                catch (Exception exception)
                {
                    result = new ConflictStageResult(repository, path, result.Outcome,
                        exception: result.Exception, refreshException: exception);
                }
            }
            if (IsCurrent(repository, conflict, request))
            {
                _model.SetStageResult(result);
            }
            return result;
        }

        private ConflictStageResult PublishFailure(GitRepository repository, string path, string messageCode)
        {
            ConflictStageResult result = new(repository, path, ConflictStageOutcome.Failed, messageCode);
            _model.SetStageResult(result);
            return result;
        }

        private async Task<ConflictStageResult> ExecuteAsync(GitRepository repository, GitConflictFile conflict,
            string resultText, int request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool ownsView = IsCurrent(repository, conflict, request);
            _model.BeginStageRequest(ownsView);
            try
            {
                GitConflictStageResult stage = await _repositoryService.SaveAndStageAsync(repository, conflict, resultText, cancellationToken);
                return CreateResult(repository, conflict.RelativePath, stage);
            }
            finally
            {
                _model.EndStageRequest(IsCurrent(repository, conflict, request));
            }
        }

        private bool IsCurrent(GitRepository repository, GitConflictFile conflict, int request)
        {
            if (request != _requestVersion)
            {
                return false;
            }
            if (ReferenceEquals(_model.StageRepository, repository) == false)
            {
                return false;
            }
            if (ReferenceEquals(_model.StageConflict, conflict) == false)
            {
                return false;
            }
            return true;
        }

        private static ConflictStageResult CreateResult(GitRepository repository, string path, GitConflictStageResult stage)
        {
            switch (stage.Outcome)
            {
                case GitConflictStageOutcome.Succeeded:
                    return new ConflictStageResult(repository, path, ConflictStageOutcome.Succeeded);
                case GitConflictStageOutcome.NoLongerConflicted:
                    return new ConflictStageResult(repository, path, ConflictStageOutcome.NoLongerConflicted);
                case GitConflictStageOutcome.FileChanged:
                    return new ConflictStageResult(repository, path, ConflictStageOutcome.FileChanged, exception: stage.Exception);
                case GitConflictStageOutcome.IncompleteMarkers:
                    return new ConflictStageResult(repository, path, ConflictStageOutcome.IncompleteResolution, exception: stage.Exception);
                case GitConflictStageOutcome.UnresolvedMarkers:
                    return new ConflictStageResult(repository, path, ConflictStageOutcome.UnresolvedResolution);
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage));
            }
        }
    }
}
