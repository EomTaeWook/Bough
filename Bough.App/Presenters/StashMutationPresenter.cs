using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Internals;
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

        public async Task<StashMutationResult> RunAsync(StashViewModel screen, GitRepository repository, StashMutationKind kind, GitStashEntry entry, string message, bool includeUntracked, IReadOnlyList<GitWorktreeFile> expectedFiles, string operationName)
        {
            try
            {
                return await _operationQueue.EnqueueAsync(repository.RootPath, operationName,
                    cancellationToken => UiQueuedOperation.RunAsync(() => ExecuteAsync(screen, repository, kind, entry, message, includeUntracked, expectedFiles, cancellationToken)));
            }
            catch (Exception exception)
            {
                string errorText = screen.GetMutationErrorText(exception);
                screen.ApplyMutationError(repository, errorText);
                return new StashMutationResult(repository, kind, false, false, false, null, errorText);
            }
        }

        private async Task<StashMutationResult> ExecuteAsync(StashViewModel screen, GitRepository repository, StashMutationKind kind, GitStashEntry entry, string message, bool includeUntracked, IReadOnlyList<GitWorktreeFile> expectedFiles, CancellationToken cancellationToken)
        {
            bool active = screen.BeginMutation(repository);
            try
            {
                return await ExecuteCoreAsync(screen, repository, kind, entry, message, includeUntracked, expectedFiles, cancellationToken);
            }
            finally
            {
                screen.EndMutation(repository, active);
            }
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
                string entryName = saved?.Created.Name;
                if (entryName == null)
                {
                    entryName = entry.Name;
                }
                screen.ApplyMutationSuccess(repository, requestVersion, kind, entryName, message);
            }

            if (stashesMayHaveChanged == true)
            {
                try
                {
                    IReadOnlyList<GitStashEntry> entries = saved?.Entries;
                    if (entries == null)
                    {
                        entries = await _stashService.GetStashesAsync(repository, cancellationToken);
                    }
                    screen.ApplyMutationEntries(repository, requestVersion, entries);
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
            if (failure != null)
            {
                failureText = screen.GetMutationErrorText(failure);
                screen.ApplyMutationError(repository, failureText);
            }

            return new StashMutationResult(repository, kind, succeeded, worktreeMayHaveChanged, stashesMayHaveChanged, status, failureText);
        }
    }
}
