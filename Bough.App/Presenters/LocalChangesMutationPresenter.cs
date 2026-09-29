using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.ViewModels;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.Presenters
{
    public class LocalChangesMutationPresenter
    {
        private readonly GitOperationQueue _operationQueue;
        private readonly GitWorkingTreeService _workingTreeService;

        public LocalChangesMutationPresenter(GitWorkingTreeService workingTreeService, GitOperationQueue operationQueue)
        {
            _workingTreeService = workingTreeService;
            _operationQueue = operationQueue;
        }

        public Task StageAsync(LocalChangesViewModel screen, GitRepository repository, IReadOnlyList<GitWorktreeFile> expectedFiles, string selectedPath, string preferredPath, string operationName)
        {
            return EnqueueAsync(screen, repository, operationName, cancellationToken => RunOperationAsync(screen, repository, async token =>
            {
                GitStagePlan plan;
                if (selectedPath == null)
                {
                    plan = await _workingTreeService.PrepareStageAllAsync(repository, token);
                }
                else
                {
                    plan = await _workingTreeService.PrepareStageSelectedAsync(repository, selectedPath, token);
                }

                _workingTreeService.ValidateSelectedFiles(expectedFiles, plan.Files, false);
                if (plan.LargeFiles.Count > 0)
                {
                    Func<IReadOnlyList<GitLargeFileCandidate>, CancellationToken, Task<bool>> confirm = screen.LargeFileConfirmation;
                    if (confirm == null)
                    {
                        throw new GitException("LocalLargeStageRequiresConfirmation", null, Array.Empty<object>());
                    }

                    bool accepted = await confirm(plan.LargeFiles, token);
                    if (accepted == false)
                    {
                        return false;
                    }
                }

                token.ThrowIfCancellationRequested();
                await _workingTreeService.ApplyStagePlanAsync(repository, plan, token);
                return true;
            }, preferredPath, true, cancellationToken));
        }

        public Task UnstageSelectedAsync(LocalChangesViewModel screen, GitRepository repository, GitWorktreeFile file, string operationName)
        {
            return EnqueueAsync(screen, repository, operationName, cancellationToken => RunOperationAsync(screen, repository, async token =>
            {
                GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository, token);
                GitWorktreeFile current = status.Files.FirstOrDefault(candidate => candidate.Path == file.Path);
                if (current == null)
                {
                    throw new GitException("LocalSelectedFileChanged", null, file.Path);
                }
                _workingTreeService.ValidateSelectedFiles([file], [current], true);
                await _workingTreeService.UnstageAsync(repository, current, token);
                return true;
            }, file.Path, false, cancellationToken));
        }

        public Task UnstageAllAsync(LocalChangesViewModel screen, GitRepository repository, IReadOnlyList<GitWorktreeFile> expectedFiles, string preferredPath, string operationName)
        {
            return EnqueueAsync(screen, repository, operationName, cancellationToken => RunOperationAsync(screen, repository, async token =>
            {
                GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository, token);
                _workingTreeService.ValidateSelectedFiles(expectedFiles, status.Files.Where(file => file.IsStaged).ToArray(), true);
                await _workingTreeService.UnstageAllAsync(repository, token);
                return true;
            }, preferredPath, false, cancellationToken));
        }

        public Task CommitAsync(LocalChangesViewModel screen, GitRepository repository, string message, bool amend, IReadOnlyList<GitWorktreeFile> expectedFiles, string operationName)
        {
            return EnqueueAsync(screen, repository, operationName, async cancellationToken =>
            {
                string hash = null;
                await RunOperationAsync(screen, repository, async token =>
                {
                    GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository, token);
                    _workingTreeService.ValidateSelectedFiles(expectedFiles, status.Files.Where(file => file.IsStaged).ToArray(), true);
                    hash = await _workingTreeService.CommitAsync(repository, message, amend, token);
                    screen.ClearCommittedDraft(repository, message, amend);
                    return true;
                }, null, false, cancellationToken);
                screen.ApplyCommitResult(repository, hash);
            });
        }

        public async Task DiscardAsync(LocalChangesViewModel screen, GitRepository repository, IReadOnlyList<GitWorktreeFile> files, CancellationToken cancellationToken)
        {
            bool active = screen.BeginMutation(repository);
            try
            {
                IReadOnlyList<GitDiscardPlan> plans = await _workingTreeService.PrepareDiscardsAsync(repository, files, cancellationToken);
                Func<IReadOnlyList<GitDiscardPlan>, CancellationToken, Task<bool>> confirm = screen.DiscardConfirmation;
                if (confirm == null)
                {
                    throw new GitException("LocalDiscardRequiresConfirmation", null, Array.Empty<object>());
                }

                bool accepted = await confirm(plans, cancellationToken);
                if (accepted == false)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                GitDiscardBatchResult result = await _workingTreeService.ApplyDiscardsAsync(repository, plans, cancellationToken);
                await screen.RefreshAfterMutationAsync(repository, null, false);
                if (result.HasError == true)
                {
                    throw new GitException("DiscardBatchPartialFailure", result.Error, result.CompletedPaths.Count, string.Join("\n", result.CompletedPaths), result.RemainingPaths.Count, string.Join("\n", result.RemainingPaths), screen.GetMutationErrorText(result.Error));
                }
            }
            finally
            {
                screen.EndMutation(repository, active);
            }
        }

        public async Task StopTrackingAsync(LocalChangesViewModel screen, GitRepository repository, IReadOnlyList<GitWorktreeFile> files, CancellationToken cancellationToken)
        {
            bool active = screen.BeginMutation(repository);
            try
            {
                IReadOnlyList<GitDiscardPlan> plans = await _workingTreeService.PrepareStopTrackingAsync(repository, files, cancellationToken);
                Func<IReadOnlyList<GitDiscardPlan>, CancellationToken, Task<bool>> confirm = screen.StopTrackingConfirmation;
                if (confirm == null)
                {
                    throw new GitException("LocalStopTrackingRequiresConfirmation", null, Array.Empty<object>());
                }

                bool accepted = await confirm(plans, cancellationToken);
                if (accepted == false)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await _workingTreeService.StopTrackingAsync(repository, plans, cancellationToken);
                await screen.RefreshAfterMutationAsync(repository, null, false);
            }
            finally
            {
                screen.EndMutation(repository, active);
            }
        }

        public async Task IgnoreAsync(LocalChangesViewModel screen, GitRepository repository, IReadOnlyList<GitWorktreeFile> files, GitIgnoreLocation location, CancellationToken cancellationToken)
        {
            bool active = screen.BeginMutation(repository);
            try
            {
                GitIgnorePlan plan = await _workingTreeService.Ignore.PrepareAsync(repository, files, location, cancellationToken);
                Func<GitIgnorePlan, CancellationToken, Task<bool>> confirm = screen.IgnoreConfirmation;
                if (confirm == null)
                {
                    throw new GitException("LocalIgnoreRequiresConfirmation", null, Array.Empty<object>());
                }

                bool accepted = await confirm(plan, cancellationToken);
                if (accepted == false)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await _workingTreeService.Ignore.ApplyAsync(repository, plan, cancellationToken);
                await screen.RefreshAfterMutationAsync(repository, null, false);
            }
            finally
            {
                screen.EndMutation(repository, active);
            }
        }

        public async Task EnqueueAsync(LocalChangesViewModel screen, GitRepository repository, string operationName, Func<CancellationToken, Task> operation)
        {
            bool resultRecorded = false;
            try
            {
                await _operationQueue.EnqueueAsync(repository.RootPath, operationName,
                    cancellationToken => UiQueuedOperation.RunAsync(async () =>
                    {
                        screen.ClearQueuedMutationError(repository);
                        try
                        {
                            await operation(cancellationToken);
                            resultRecorded = true;
                        }
                        catch (Exception exception)
                        {
                            resultRecorded = true;
                            screen.RememberMutationError(repository, operationName, exception);
                            throw;
                        }
                    }));
            }
            catch (Exception exception)
            {
                if (resultRecorded == true)
                {
                    return;
                }

                await UiQueuedOperation.RunAsync(() =>
                {
                    screen.RememberMutationError(repository, operationName, exception);
                    return Task.CompletedTask;
                });
            }
        }

        public async Task RunOperationAsync(LocalChangesViewModel screen, GitRepository repository, Func<CancellationToken, Task<bool>> operation, string preferredPath, bool preferStaged, CancellationToken cancellationToken)
        {
            bool active = screen.BeginMutation(repository);
            try
            {
                bool changed = await operation(cancellationToken);
                if (changed == true)
                {
                    await screen.RefreshAfterMutationAsync(repository, preferredPath, preferStaged);
                }
            }
            catch
            {
                await screen.RefreshAfterMutationAsync(repository, null, false);
                throw;
            }
            finally
            {
                screen.EndMutation(repository, active);
            }
        }
    }
}
