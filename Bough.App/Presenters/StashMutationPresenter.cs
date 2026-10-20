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
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class StashMutationPresenter
    {
        private readonly GitStashService _stashService;
        private readonly GitWorkingTreeService _workingTreeService;
        private readonly GitOperationQueue _operationQueue;

        public StashMutationPresenter(GitStashService stashService, GitWorkingTreeService workingTreeService, GitOperationQueue operationQueue)
        {
            _stashService = stashService;
            _workingTreeService = workingTreeService;
            _operationQueue = operationQueue;
        }

        public async Task<StashMutationResult> RunAsync(StashViewModel screen, GitRepository repository, StashMutationKind kind, GitStashEntry entry, string message, bool includeUntracked, IReadOnlyList<GitWorktreeFile> expectedFiles, string operationName, IStashMutationCompletion completion = null)
        {
            try
            {
                return await _operationQueue.EnqueueAsync(repository.RootPath, operationName, cancellationToken => UiQueuedOperation.RunAsync(() => ExecuteAsync(screen, repository, kind, entry, message, includeUntracked, expectedFiles, completion, cancellationToken)));
            }
            catch (Exception exception)
            {
                string errorText = screen.GetMutationErrorText(exception);
                LocalizedText[] errors = [new LocalizedText(exception)];
                screen.ApplyMutationError(repository, errors);
                return new StashMutationResult(repository, kind, false, false, false, null, errorText, errors);
            }
        }

        public async Task<StashMutationResult> RetrySaveRefreshAsync(StashViewModel screen, StashMutationResult savedResult, IStashMutationCompletion completion = null)
        {
            GitRepository repository = savedResult.Repository;
            int requestVersion = screen.BeginSavedRefresh(repository);
            GitWorktreeStatus status = null;
            Exception failure = null;
            try
            {
                try
                {
                    IReadOnlyList<GitStashEntry> entries = await _stashService.GetStashesAsync(repository, CancellationToken.None);
                    screen.ApplyMutationEntries(repository, requestVersion, entries);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                try
                {
                    status = await _workingTreeService.GetStatusAsync(repository);
                    screen.ApplyMutationStatus(repository, requestVersion, status);
                }
                catch (Exception exception)
                {
                    if (failure == null)
                    {
                        failure = exception;
                    }
                }

                if (screen.IsSavedRefreshCurrent(repository, requestVersion) == false)
                {
                    status = null;
                    if (failure == null)
                    {
                        failure = new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
                    }
                }

                string errorText = string.Empty;
                IReadOnlyList<LocalizedText> errors = Array.Empty<LocalizedText>();
                if (failure != null)
                {
                    errorText = screen.GetMutationErrorText(failure);
                    errors = new LocalizedText[] { new LocalizedText(failure) };
                }
                screen.ApplySavedRefreshError(repository, requestVersion, errors);
                StashMutationResult result = new(repository, savedResult.Kind, savedResult.Succeeded, savedResult.WorktreeMayHaveChanged, savedResult.StashesMayHaveChanged, status, errorText, errors);
                return await CompleteAsync(screen, completion, result);
            }
            finally
            {
                screen.EndSavedRefresh(repository, requestVersion);
            }
        }

        private async Task<StashMutationResult> ExecuteAsync(StashViewModel screen, GitRepository repository, StashMutationKind kind, GitStashEntry entry, string message, bool includeUntracked, IReadOnlyList<GitWorktreeFile> expectedFiles, IStashMutationCompletion completion, CancellationToken cancellationToken)
        {
            bool active = screen.BeginMutation(repository);
            try
            {
                StashMutationResult result = await ExecuteCoreAsync(screen, repository, kind, entry, message, includeUntracked, expectedFiles, cancellationToken);
                return await CompleteAsync(screen, completion, result);
            }
            finally
            {
                screen.EndMutation(repository, active);
            }
        }

        private async Task<StashMutationResult> CompleteAsync(StashViewModel screen, IStashMutationCompletion completion, StashMutationResult result)
        {
            if (completion == null)
            {
                return result;
            }

            List<LocalizedText> errors = result.Errors.ToList();
            if (result.Errors.Count == 0)
            {
                if (string.IsNullOrEmpty(result.ErrorText) == false)
                {
                    errors.Add(new LocalizedText(new GitException(result.ErrorText)));
                }
            }
            GitWorktreeStatus status = result.WorktreeStatus;
            try
            {
                switch (result.Kind)
                {
                    case StashMutationKind.Save:
                        StashMutationResult completed = await completion.CompleteStashSaveAsync(result);
                        if (completed == null)
                        {
                            throw new GitException("StashSaveCompletionResultMissing", null, Array.Empty<object>());
                        }
                        status = completed.WorktreeStatus ?? status;
                        foreach (LocalizedText error in completed.Errors)
                        {
                            if (errors.Contains(error) == false)
                            {
                                errors.Add(error);
                            }
                        }
                        if (completed.Errors.Count == 0)
                        {
                            if (string.IsNullOrEmpty(completed.ErrorText) == false)
                            {
                                if (completed.ErrorText != result.ErrorText)
                                {
                                    errors.Add(new LocalizedText(new GitException(completed.ErrorText)));
                                }
                            }
                        }
                        break;
                    case StashMutationKind.Apply:
                        await completion.CompleteStashApplyAsync(result);
                        break;
                    case StashMutationKind.Pop:
                        await completion.CompleteStashPopAsync(result);
                        break;
                    case StashMutationKind.Drop:
                        await completion.CompleteStashDropAsync(result);
                        break;
                }
            }
            catch (Exception exception)
            {
                errors.Add(new LocalizedText(exception));
            }

            string errorText = string.Join(Environment.NewLine, errors.Select(error => error.GetText(screen.Strings)));
            if (errors.Count > 0)
            {
                screen.ApplyMutationError(result.Repository, errors);
            }
            return new StashMutationResult(result.Repository, result.Kind, result.Succeeded, result.WorktreeMayHaveChanged, result.StashesMayHaveChanged, status, errorText, errors);
        }

        private async Task<StashMutationResult> ExecuteCoreAsync(StashViewModel screen, GitRepository repository, StashMutationKind kind, GitStashEntry entry, string message, bool includeUntracked, IReadOnlyList<GitWorktreeFile> expectedFiles, CancellationToken cancellationToken)
        {
            Exception failure = null;
            bool succeeded = false;
            bool worktreeMayHaveChanged = false;
            bool stashesMayHaveChanged = false;
            GitStashSaveResult saved = null;
            GitWorktreeStatus status = null;
            try
            {
                switch (kind)
                {
                    case StashMutationKind.Save:
                        GitWorktreeStatus current = await _workingTreeService.GetStatusAsync(repository, cancellationToken);
                        _stashService.ValidateSaveSelection(expectedFiles, current.Files, includeUntracked);
                        saved = await _stashService.SaveWithEntriesAsync(repository, message, includeUntracked, cancellationToken);
                        worktreeMayHaveChanged = true;
                        stashesMayHaveChanged = true;
                        succeeded = true;
                        failure = saved.ReadError;
                        break;
                    case StashMutationKind.Apply:
                        await _stashService.ApplyAsync(repository, entry, cancellationToken);
                        worktreeMayHaveChanged = true;
                        succeeded = true;
                        break;
                    case StashMutationKind.Pop:
                        await _stashService.PopAsync(repository, entry, cancellationToken);
                        worktreeMayHaveChanged = true;
                        stashesMayHaveChanged = true;
                        succeeded = true;
                        break;
                    case StashMutationKind.Drop:
                        await _stashService.DropAsync(repository, entry, cancellationToken);
                        stashesMayHaveChanged = true;
                        succeeded = true;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind));
                }
            }
            catch (GitStashMutationException exception)
            {
                failure = exception;
                worktreeMayHaveChanged = exception.WorktreeMayHaveChanged;
                stashesMayHaveChanged = exception.StashesMayHaveChanged;
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            int requestVersion = screen.InvalidateMutationReads(repository, succeeded, worktreeMayHaveChanged, stashesMayHaveChanged);
            if (succeeded == true)
            {
                try
                {
                    string entryName = saved?.Created?.Name;
                    if (entryName == null)
                    {
                        entryName = entry?.Name;
                    }
                    screen.ApplyMutationSuccess(repository, requestVersion, kind, entryName, message);
                }
                catch (Exception exception)
                {
                    if (failure == null)
                    {
                        failure = exception;
                    }
                }
            }

            if (stashesMayHaveChanged == true)
            {
                try
                {
                    IReadOnlyList<GitStashEntry> entries = saved?.Entries;
                    if (saved == null)
                    {
                        entries = await _stashService.GetStashesAsync(repository, cancellationToken);
                    }
                    if (entries != null)
                    {
                        screen.ApplyMutationEntries(repository, requestVersion, entries);
                    }
                }
                catch (Exception exception)
                {
                    if (failure == null)
                    {
                        failure = exception;
                    }
                }
            }

            if (worktreeMayHaveChanged == true)
            {
                try
                {
                    status = await _workingTreeService.GetStatusAsync(repository, cancellationToken);
                    screen.ApplyMutationStatus(repository, requestVersion, status);
                }
                catch (Exception exception)
                {
                    if (failure == null)
                    {
                        failure = exception;
                    }
                }
            }

            string failureText = string.Empty;
            IReadOnlyList<LocalizedText> failureErrors = Array.Empty<LocalizedText>();
            if (failure != null)
            {
                failureText = screen.GetMutationErrorText(failure);
                failureErrors = new LocalizedText[] { new LocalizedText(failure) };
                screen.ApplyMutationError(repository, failureErrors);
            }

            return new StashMutationResult(repository, kind, succeeded, worktreeMayHaveChanged, stashesMayHaveChanged, status, failureText, failureErrors);
        }
    }
}
