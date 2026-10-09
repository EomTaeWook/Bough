using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Internals;
using Bough.App.Interfaces;
using Bough.App.Localization;
using Bough.App.Threading;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;
using Bough.Core.Conflicts;
using Bough.Core.Conflicts.Models;
using Bough.Core.Internals;
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
        private int _batchVersion;
        private readonly ConflictParser _parser;

        public ConflictStagePresenter(GitRepositoryService repositoryService, GitOperationQueue operationQueue,
            ConflictParser parser, ConflictResolutionViewModel model)
        {
            _repositoryService = repositoryService;
            _operationQueue = operationQueue;
            _model = model;
            _parser = parser;
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

        public void InvalidateBatch()
        {
            _batchVersion++;
            _model.SetBatchStageResult(null);
            _model.SetBatchStaging(false);
        }

        public async Task<ConflictStageResult> SaveAndStageAsync(string operationName)
        {
            GitRepository repository = _model.CurrentRepository;
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

            _model.SetBatchStageResult(null);
            int request = _requestVersion;
            string resultText = _model.ResultText;
            bool deleteFile = _model.IsResultFileDeleted;
            string path = conflict.RelativePath;
            ConflictStageResult result;
            try
            {
                return await _operationQueue.EnqueueAsync(repository.RootPath, $"{operationName} · {path}",
                    token => UiQueuedOperation.RunAsync(() => ExecuteAsync(repository, conflict, resultText, deleteFile, request, token)));
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
            if (IsCurrent(repository, conflict, request))
            {
                _model.SetStageResult(result);
            }
            return result;
        }

        public async Task<ConflictBatchStageResult> ApplyRemainingAndStageAsync(ResolutionChoiceType choice, string operationName)
        {
            if (_completion == null)
            {
                PublishFailure(_model.CurrentRepository, string.Empty, "ConflictStageUnavailable");
                return null;
            }
            if (_model.CanApplyRemaining == false)
            {
                return null;
            }
            if (choice == ResolutionChoiceType.Unresolved)
            {
                throw new ArgumentOutOfRangeException(nameof(choice));
            }
            if (Enum.IsDefined(choice) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(choice));
            }
            GitRepository repository = _model.CurrentRepository;
            string[] paths = _model.ConflictFiles.Select(file => file.RelativePath).Distinct().ToArray();
            ConflictBatchFileSnapshot current = _model.CaptureBatchFile();
            bool isRevert = _model.IsRevertOperationInProgress;
            List<ConflictStageResult> results = new();
            Dictionary<string, LocalizedText> excluded = new();
            ConflictBatchStageResult previous = _model.BatchStageResult;
            if (previous != null)
            {
                if (ReferenceEquals(previous.Repository, repository))
                {
                    results.AddRange(previous.Files.Where(file => file.WasStaged).Where(file => paths.Contains(file.Path)));
                }
            }
            int batch = ++_batchVersion;
            bool stageEnded = false;
            _model.SetStageResult(null);
            _model.SetBatchStageResult(null);
            _model.SetBatchStaging(true);
            _model.BeginStageRequest(true);
            try
            {
                List<ConflictBatchFileSnapshot> files = await PrepareBatchAsync(repository, paths, current, isRevert, results, excluded);
                return await _operationQueue.EnqueueAsync(repository.RootPath, operationName,
                    token => UiQueuedOperation.RunAsync(async () =>
                    {
                        try
                        {
                            return await ExecuteBatchAsync(repository, files, choice, batch, results, excluded, token);
                        }
                        finally
                        {
                            EndBatchStage(repository, batch);
                            stageEnded = true;
                        }
                    }));
            }
            catch (Exception exception)
            {
                foreach (string path in paths)
                {
                    if (results.Any(result => result.Path == path))
                    {
                        continue;
                    }
                    if (excluded.ContainsKey(path))
                    {
                        continue;
                    }
                    ConflictStageOutcome outcome = ConflictStageOutcome.Failed;
                    if (exception is OperationCanceledException)
                    {
                        outcome = ConflictStageOutcome.Canceled;
                    }
                    results.Add(new ConflictStageResult(repository, path, outcome, exception: exception));
                }
                ConflictBatchStageResult result = new(repository, results, excluded);
                if (IsCurrentBatch(repository, batch))
                {
                    _model.SetBatchStageResult(result);
                }
                return result;
            }
            finally
            {
                if (stageEnded == false)
                {
                    EndBatchStage(repository, batch);
                }
            }
        }

        private Task<List<ConflictBatchFileSnapshot>> PrepareBatchAsync(GitRepository repository, string[] paths,
            ConflictBatchFileSnapshot current, bool isRevert, List<ConflictStageResult> results,
            Dictionary<string, LocalizedText> excluded)
        {
            string currentLabel = _model.Strings.GetString("CurrentChange");
            string incomingLabel = _model.Strings.GetString("IncomingChange");
            string rebaseCurrent = _model.Strings.GetString("RebaseTargetChange");
            string rebaseIncoming = _model.Strings.GetString("RebaseReplayChange");
            string rebaseCurrentSource = _model.Strings.GetString("RebaseTargetSource");
            string rebaseIncomingSource = _model.Strings.GetString("RebaseReplaySource");
            string revertIncoming = _model.Strings.GetString("RevertIncomingChange");
            string incomingIndexSource = _model.Strings.GetString("ConflictIncomingIndexStage3");
            string revertIndexSource = _model.Strings.GetString("RevertIncomingIndexSource");
            return Task.Run(async () =>
            {
                List<ConflictBatchFileSnapshot> files = new();
                bool isRebase = await _repositoryService.IsRebaseInProgressAsync(repository);
                string currentSource = repository.CurrentBranch;
                string incomingSource = incomingLabel;
                if (isRebase)
                {
                    currentLabel = rebaseCurrent;
                    incomingLabel = rebaseIncoming;
                    currentSource = rebaseCurrentSource;
                    incomingSource = rebaseIncomingSource;
                }
                else if (isRevert)
                {
                    incomingLabel = revertIncoming;
                    incomingSource = incomingLabel;
                    incomingIndexSource = revertIndexSource;
                }
                foreach (string path in paths)
                {
                    if (results.Any(result => result.Path == path))
                    {
                        continue;
                    }
                    try
                    {
                        ConflictBatchFileSnapshot file = current;
                        if (file != null)
                        {
                            if (file.Conflict.RelativePath != path)
                            {
                                file = null;
                            }
                        }
                        if (file == null)
                        {
                            GitConflictFile conflict = await _repositoryService.LoadConflictAsync(repository, path,
                                currentSource, incomingSource, incomingIndexSource);
                            if (conflict.WorkingText.Contains('\0'))
                            {
                                throw new GitException("ConflictBinaryFileCannotEdit", null, path);
                            }
                            if (conflict.OursText.Contains('\0'))
                            {
                                throw new GitException("ConflictBinaryFileCannotEdit", null, path);
                            }
                            if (conflict.TheirsText.Contains('\0'))
                            {
                                throw new GitException("ConflictBinaryFileCannotEdit", null, path);
                            }
                            ConflictDocument document = _parser.ParseFile(conflict.WorkingText, currentLabel,
                                conflict.OursText, incomingLabel, conflict.TheirsText, conflict.BaseText);
                            string rendered = document.Render(new Dictionary<int, ResolutionChoiceType>());
                            bool deleted = document.IsWholeFileConflict && conflict.WorkingFileExists == false;
                            file = new ConflictBatchFileSnapshot(conflict, document,
                                new Dictionary<int, ResolutionChoiceType>(), rendered, rendered, deleted);
                        }
                        if (HasManualBatchEdits(file))
                        {
                            excluded.Add(path, new LocalizedText("ConflictBatchStageManualExcluded"));
                            continue;
                        }
                        files.Add(file);
                    }
                    catch (Exception exception)
                    {
                        results.Add(new ConflictStageResult(repository, path, ConflictStageOutcome.Failed, exception: exception));
                    }
                }
                return files;
            });
        }

        private async Task<ConflictBatchStageResult> ExecuteBatchAsync(GitRepository repository,
            List<ConflictBatchFileSnapshot> files, ResolutionChoiceType choice, int batch,
            List<ConflictStageResult> results, Dictionary<string, LocalizedText> excluded, CancellationToken cancellationToken)
        {
            foreach (ConflictBatchFileSnapshot file in files)
            {
                ConflictStageResult result = null;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Dictionary<int, ResolutionChoiceType> choices = new(file.Choices);
                    foreach (ConflictHunk hunk in file.Document.Hunks)
                    {
                        if (choices.TryGetValue(hunk.Id, out ResolutionChoiceType selected))
                        {
                            if (selected != ResolutionChoiceType.Unresolved)
                            {
                                continue;
                            }
                        }
                        choices[hunk.Id] = choice;
                    }
                    string text = await Task.Run(() => file.Document.Render(choices), cancellationToken);
                    bool deleteFile = false;
                    if (file.Document.IsWholeFileConflict)
                    {
                        deleteFile = IsDeletedBatchChoice(file.Conflict, choices[file.Document.Hunks[0].Id]);
                    }
                    GitConflictStageResult stage = await _repositoryService.SaveAndStageAsync(repository, file.Conflict,
                        text, deleteFile, cancellationToken);
                    result = CreateResult(repository, file.Conflict.RelativePath, stage);
                    if (result.WasStaged)
                    {
                        if (IsCurrentBatch(repository, batch))
                        {
                            _model.ApplyBatchStageBaseline(file, choices, text, deleteFile);
                        }
                    }
                }
                catch (OperationCanceledException exception)
                {
                    ConflictStageOutcome outcome = ConflictStageOutcome.Canceled;
                    if (result != null)
                    {
                        if (result.WasStaged)
                        {
                            outcome = ConflictStageOutcome.RefreshFailed;
                        }
                    }
                    result = new ConflictStageResult(repository, file.Conflict.RelativePath, outcome, exception: exception);
                }
                catch (Exception exception)
                {
                    ConflictStageOutcome outcome = ConflictStageOutcome.Failed;
                    if (result != null)
                    {
                        if (result.WasStaged)
                        {
                            outcome = ConflictStageOutcome.RefreshFailed;
                        }
                    }
                    result = new ConflictStageResult(repository, file.Conflict.RelativePath, outcome, exception: exception);
                }
                results.Add(result);
            }
            ConflictBatchStageResult completed = new(repository, results, excluded);
            if (IsCurrentBatch(repository, batch) == false)
            {
                return completed;
            }
            _model.SetBatchStageResult(completed);
            _model.SetBatchStaging(false);
            try
            {
                await _completion.RefreshConflictStateAsync(repository);
            }
            catch (Exception exception)
            {
                completed = new ConflictBatchStageResult(repository, results, excluded, exception);
            }
            if (IsCurrentBatch(repository, batch))
            {
                _model.SetBatchStageResult(completed);
            }
            return completed;
        }

        private void EndBatchStage(GitRepository repository, int batch)
        {
            bool ownsView = IsCurrentBatch(repository, batch);
            if (ownsView)
            {
                _model.SetBatchStaging(false);
            }
            _model.EndStageRequest(ownsView);
        }

        private bool IsCurrentBatch(GitRepository repository, int batch)
        {
            if (batch != _batchVersion)
            {
                return false;
            }
            if (ReferenceEquals(_model.CurrentRepository, repository) == false)
            {
                return false;
            }
            return true;
        }

        private static bool HasManualBatchEdits(ConflictBatchFileSnapshot file)
        {
            if (file.ResultText != file.RenderedText)
            {
                return true;
            }
            if (file.Document.IsWholeFileConflict)
            {
                if (file.Choices.Count == 0)
                {
                    if (file.Conflict.WorkingFileExists)
                    {
                        bool matchesOurs = file.Conflict.HasOurs && file.Conflict.WorkingText == file.Conflict.OursText;
                        bool matchesTheirs = file.Conflict.HasTheirs && file.Conflict.WorkingText == file.Conflict.TheirsText;
                        if (matchesOurs == false && matchesTheirs == false)
                        {
                            return true;
                        }
                    }
                    else if (file.Conflict.HasOurs && file.Conflict.HasTheirs)
                    {
                        return true;
                    }
                }
            }
            bool deleted = false;
            if (file.Document.IsWholeFileConflict)
            {
                deleted = file.Conflict.WorkingFileExists == false;
                if (file.Choices.TryGetValue(file.Document.Hunks[0].Id, out ResolutionChoiceType selected))
                {
                    if (selected != ResolutionChoiceType.Unresolved)
                    {
                        deleted = IsDeletedBatchChoice(file.Conflict, selected);
                    }
                }
            }
            return file.DeleteFile != deleted;
        }

        private static bool IsDeletedBatchChoice(GitConflictFile conflict, ResolutionChoiceType choice)
        {
            switch (choice)
            {
                case ResolutionChoiceType.Ours:
                    return conflict.HasOurs == false;
                case ResolutionChoiceType.Theirs:
                    return conflict.HasTheirs == false;
                case ResolutionChoiceType.Both:
                    return conflict.HasOurs == false && conflict.HasTheirs == false;
                case ResolutionChoiceType.Remove:
                    return conflict.HasOurs == false || conflict.HasTheirs == false;
                default:
                    return false;
            }
        }

        private ConflictStageResult PublishFailure(GitRepository repository, string path, string messageCode)
        {
            ConflictStageResult result = new(repository, path, ConflictStageOutcome.Failed, messageCode);
            _model.SetStageResult(result);
            return result;
        }

        private async Task<ConflictStageResult> ExecuteAsync(GitRepository repository, GitConflictFile conflict,
            string resultText, bool deleteFile, int request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool ownsView = IsCurrent(repository, conflict, request);
            _model.BeginStageRequest(ownsView);
            ConflictStageResult result;
            try
            {
                GitConflictStageResult stage = await _repositoryService.SaveAndStageAsync(repository, conflict, resultText, deleteFile, cancellationToken);
                result = CreateResult(repository, conflict.RelativePath, stage);
            }
            catch (OperationCanceledException exception)
            {
                result = new ConflictStageResult(repository, conflict.RelativePath, ConflictStageOutcome.Canceled, exception: exception);
            }
            catch (Exception exception)
            {
                result = new ConflictStageResult(repository, conflict.RelativePath, ConflictStageOutcome.Failed, exception: exception);
            }
            finally
            {
                _model.EndStageRequest(IsCurrent(repository, conflict, request));
            }
            if (IsCurrent(repository, conflict, request) == false)
            {
                return result;
            }
            if (result.Succeeded)
            {
                _model.MarkStageSaved(conflict, resultText, deleteFile);
                try
                {
                    await _completion.CompleteConflictStageAsync(repository, conflict.RelativePath);
                }
                catch (Exception exception)
                {
                    result = new ConflictStageResult(repository, conflict.RelativePath, ConflictStageOutcome.RefreshFailed, exception: exception);
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
                    result = new ConflictStageResult(repository, conflict.RelativePath, result.Outcome,
                        exception: result.Exception, refreshException: exception);
                }
            }
            if (IsCurrent(repository, conflict, request))
            {
                _model.SetStageResult(result);
            }
            return result;
        }

        private bool IsCurrent(GitRepository repository, GitConflictFile conflict, int request)
        {
            if (request != _requestVersion)
            {
                return false;
            }
            if (ReferenceEquals(_model.CurrentRepository, repository) == false)
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
