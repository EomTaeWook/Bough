using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Threading;
using Bough.App.Localization;
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

        public Task<GitWorktreeStatus> RefreshStashSaveWorktreeAsync(LocalChangesViewModel screen, GitRepository repository)
        {
            return UiQueuedOperation.RunAsync(() =>
            {
                if (screen.IsCurrentRepository(repository) == false)
                {
                    return Task.FromResult<GitWorktreeStatus>(null);
                }

                int requestVersion = screen.BeginStashSaveWorktreeRefresh();
                Task<GitWorktreeStatus> request = RefreshStashSaveWorktreeCoreAsync(screen, repository, requestVersion);
                screen.TrackStashSaveWorktreeRefresh(repository, requestVersion, request);
                return request;
            });
        }

        private async Task<GitWorktreeStatus> RefreshStashSaveWorktreeCoreAsync(LocalChangesViewModel screen, GitRepository repository, int requestVersion)
        {
            try
            {
                if (screen.IsCurrentRepository(repository) == false)
                {
                    return null;
                }
                if (requestVersion != screen.WorktreeRequestVersion)
                {
                    return null;
                }

                GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository);
                if (screen.IsCurrentRepository(repository) == false)
                {
                    return null;
                }
                if (requestVersion != screen.WorktreeRequestVersion)
                {
                    return null;
                }

                screen.ApplyStashSaveWorktreeSnapshot(status);
                if (screen.IsCurrentRepository(repository) == false)
                {
                    return null;
                }
                if (requestVersion != screen.WorktreeRequestVersion)
                {
                    return null;
                }
                return status;
            }
            catch
            {
                if (screen.IsCurrentRepository(repository) == false)
                {
                    return null;
                }
                if (requestVersion != screen.WorktreeRequestVersion)
                {
                    return null;
                }
                throw;
            }
            finally
            {
                screen.EndStashSaveWorktreeRefresh(repository, requestVersion);
            }
        }

        public Task StageAsync(LocalChangesViewModel screen, GitRepository repository, IReadOnlyList<GitWorktreeFile> expectedFiles, IReadOnlyList<string> selectedPaths, string preferredPath, LocalizedText operationName)
        {
            return EnqueueAsync(screen, repository, operationName, cancellationToken => RunOperationAsync(screen, repository, async token =>
            {
                GitStagePlan plan;
                if (selectedPaths == null)
                {
                    plan = await _workingTreeService.PrepareStageAllAsync(repository, token);
                }
                else
                {
                    plan = await _workingTreeService.PrepareStageSelectionAsync(repository, selectedPaths, token);
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

        public Task UnstageSelectedAsync(LocalChangesViewModel screen, GitRepository repository, IReadOnlyList<GitWorktreeFile> expectedFiles, string preferredPath, LocalizedText operationName)
        {
            return EnqueueAsync(screen, repository, operationName, cancellationToken => RunOperationAsync(screen, repository, async token =>
            {
                GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository, token);
                List<GitWorktreeFile> currentFiles = [];
                foreach (GitWorktreeFile expectedFile in expectedFiles)
                {
                    GitWorktreeFile currentFile = status.Files.FirstOrDefault(candidate => candidate.Path == expectedFile.Path);
                    if (currentFile == null)
                    {
                        throw new GitException("LocalSelectedFileChanged", null, expectedFile.Path);
                    }

                    currentFiles.Add(currentFile);
                }

                _workingTreeService.ValidateSelectedFiles(expectedFiles, currentFiles, true);
                await _workingTreeService.UnstageFilesAsync(repository, currentFiles, token);
                return true;
            }, preferredPath, false, cancellationToken));
        }

        public Task UnstageAllAsync(LocalChangesViewModel screen, GitRepository repository, IReadOnlyList<GitWorktreeFile> expectedFiles, string preferredPath, LocalizedText operationName)
        {
            return EnqueueAsync(screen, repository, operationName, cancellationToken => RunOperationAsync(screen, repository, async token =>
            {
                GitWorktreeStatus status = await _workingTreeService.GetStatusAsync(repository, token);
                _workingTreeService.ValidateSelectedFiles(expectedFiles, status.Files.Where(file => file.IsStaged).ToArray(), true);
                await _workingTreeService.UnstageAllAsync(repository, token);
                return true;
            }, preferredPath, false, cancellationToken));
        }

        public Task CommitAsync(LocalChangesViewModel screen, GitRepository repository, string message, bool amend, GitWorktreeStatus expectedStatus, LocalizedText operationName)
        {
            return EnqueueAsync(screen, repository, operationName, async cancellationToken =>
            {
                GitCommitResult result = null;
                await RunOperationAsync(screen, repository, async token =>
                {
                    result = await _workingTreeService.CommitAsync(repository, message, amend, expectedStatus, token);
                    screen.ClearCommittedDraft(repository, message, amend);
                    return true;
                }, null, false, cancellationToken);
                screen.ApplyCommitResult(repository, result.CommitHash, result.ReadError);
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
                    throw new GitException("DiscardBatchPartialFailure", result.Error, result.CompletedPaths.Count, string.Join("\n", result.CompletedPaths), result.RemainingPaths.Count, string.Join("\n", result.RemainingPaths), result.Error);
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
                GitStopTrackingPlan plan = await _workingTreeService.PrepareStopTrackingAsync(repository, files, cancellationToken);
                Func<IReadOnlyList<GitDiscardPlan>, CancellationToken, Task<bool>> confirm = screen.StopTrackingConfirmation;
                if (confirm == null)
                {
                    throw new GitException("LocalStopTrackingRequiresConfirmation", null, Array.Empty<object>());
                }

                bool accepted = await confirm(plan.Files, cancellationToken);
                if (accepted == false)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await _workingTreeService.StopTrackingAsync(repository, plan, cancellationToken);
                await screen.RefreshAfterMutationAsync(repository, null, false);
            }
            catch (Exception exception)
            {
                await screen.RefreshAfterMutationAsync(repository, null, false);
                if (exception is GitException gitException)
                {
                    if (gitException.ErrorCode == "WorkingStopTrackingPartialFailure")
                    {
                        throw new GitException(gitException.ErrorCode, gitException.InnerException, gitException.InnerException);
                    }
                }

                throw;
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
                if (screen.IsCurrentRepository(repository) == false)
                {
                    throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
                }
                GitRepository binding = screen.CurrentRepository;
                int requestVersion = screen.WorktreeRequestVersion;
                GitIgnorePlan plan = await Task.Run(() => _workingTreeService.Ignore.PrepareAsync(repository, files, location, cancellationToken), cancellationToken);
                if (ReferenceEquals(binding, screen.CurrentRepository) == false)
                {
                    throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
                }
                if (requestVersion != screen.WorktreeRequestVersion)
                {
                    throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
                }
                Func<GitIgnorePlan, Func<string, CancellationToken, Task<GitIgnorePlan>>, CancellationToken, Task<GitIgnorePlan>> confirm = screen.IgnoreConfirmation;
                if (confirm == null)
                {
                    throw new GitException("LocalIgnoreRequiresConfirmation", null, Array.Empty<object>());
                }

                GitIgnorePlan preparedPlan = plan;
                int preparationVersion = 0;
                async Task<GitIgnorePlan> PrepareIgnorePlanAsync(string directoryPath, CancellationToken preparationToken)
                {
                    int version = ++preparationVersion;
                    preparedPlan = null;
                    if (ReferenceEquals(binding, screen.CurrentRepository) == false)
                    {
                        throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
                    }
                    if (requestVersion != screen.WorktreeRequestVersion)
                    {
                        throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
                    }

                    GitIgnorePlan result;
                    if (string.IsNullOrEmpty(directoryPath) == true)
                    {
                        result = await Task.Run(() => _workingTreeService.Ignore.PrepareAsync(repository, files, location, preparationToken), preparationToken);
                    }
                    else
                    {
                        result = await Task.Run(() => _workingTreeService.Ignore.PrepareDirectoryAsync(repository, directoryPath, location, preparationToken), preparationToken);
                    }
                    preparationToken.ThrowIfCancellationRequested();
                    if (version != preparationVersion)
                    {
                        throw new GitException("IgnoreSelectedFilesChanged", null, Array.Empty<object>());
                    }
                    if (ReferenceEquals(binding, screen.CurrentRepository) == false)
                    {
                        throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
                    }
                    if (requestVersion != screen.WorktreeRequestVersion)
                    {
                        throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
                    }
                    preparedPlan = result;
                    return result;
                }

                GitIgnorePlan confirmedPlan = await confirm(plan, PrepareIgnorePlanAsync, cancellationToken);
                if (confirmedPlan == null)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (ReferenceEquals(binding, screen.CurrentRepository) == false)
                {
                    throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
                }
                if (requestVersion != screen.WorktreeRequestVersion)
                {
                    throw new GitException("LocalSelectionSetChanged", null, Array.Empty<object>());
                }
                if (ReferenceEquals(preparedPlan, confirmedPlan) == false)
                {
                    throw new GitException("IgnoreSelectedFilesChanged", null, Array.Empty<object>());
                }
                await Task.Run(() => _workingTreeService.Ignore.ApplyAsync(repository, confirmedPlan, cancellationToken), cancellationToken);
                await screen.RefreshAfterMutationAsync(repository, null, false);
            }
            finally
            {
                screen.EndMutation(repository, active);
            }
        }

        public async Task EnqueueAsync(LocalChangesViewModel screen, GitRepository repository, LocalizedText operationName, Func<CancellationToken, Task> operation)
        {
            bool resultRecorded = false;
            try
            {
                await _operationQueue.EnqueueAsync(repository.RootPath, operationName.GetText(screen.Strings),
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
