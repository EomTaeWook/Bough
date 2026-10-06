using System;
using System.Threading.Tasks;
using Bough.App.Interfaces;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;

namespace Bough.App.Presenters
{
    public class StashSavePresenter
    {
        private readonly StashViewModel _screen;
        private readonly StashSaveDialogState _state;

        public StashSavePresenter(StashViewModel screen, StashSaveDialogState state)
        {
            _screen = screen;
            _state = state;
        }

        public async Task SaveAsync(IStashMutationCompletion completion)
        {
            if (_state.CanSave == false)
            {
                return;
            }
            if (_screen.CanSaveStash == false)
            {
                return;
            }

            _state.BeginSave();
            StashMutationResult result = null;
            try
            {
                result = await _screen.SaveAsync();
                result = await CompleteAsync(completion, result);
                _state.CompleteSave(result, null);
            }
            catch (Exception exception)
            {
                _state.CompleteSave(result, exception);
            }
            finally
            {
                _state.EndSave();
            }
        }

        public async Task RetryRefreshAsync(IStashMutationCompletion completion)
        {
            if (_state.CanRetry == false)
            {
                return;
            }

            StashMutationResult[] targets = _state.GetRefreshTargets();
            _state.BeginRefresh();
            try
            {
                foreach (StashMutationResult target in targets)
                {
                    StashMutationResult result = target;
                    try
                    {
                        result = await _screen.RetrySaveRefreshAsync(target);
                        result = await CompleteAsync(completion, result);
                        _state.CompleteRefresh(result, null);
                    }
                    catch (Exception exception)
                    {
                        _state.CompleteRefresh(result, exception);
                    }
                }
            }
            finally
            {
                _state.EndRefresh();
            }
        }

        private static async Task<StashMutationResult> CompleteAsync(IStashMutationCompletion completion, StashMutationResult result)
        {
            if (result == null)
            {
                throw new GitException("StashSaveCompletionResultMissing", null, Array.Empty<object>());
            }
            if (completion == null)
            {
                return result;
            }

            StashMutationResult completed = await completion.CompleteStashSaveAsync(result);
            if (completed == null)
            {
                throw new GitException("StashSaveCompletionResultMissing", null, Array.Empty<object>());
            }
            string errorText = completed.ErrorText;
            if (string.IsNullOrEmpty(result.ErrorText) == false)
            {
                errorText = result.ErrorText;
                if (string.IsNullOrEmpty(completed.ErrorText) == false)
                {
                    if (completed.ErrorText != result.ErrorText)
                    {
                        errorText = string.Join(Environment.NewLine, result.ErrorText, completed.ErrorText);
                    }
                }
            }
            return new StashMutationResult(result.Repository, result.Kind, result.Succeeded, result.WorktreeMayHaveChanged, result.StashesMayHaveChanged, completed.WorktreeStatus ?? result.WorktreeStatus, errorText);
        }
    }
}
