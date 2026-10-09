using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bough.App.Interfaces;
using Bough.App.Localization;
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

        private async Task<StashMutationResult> CompleteAsync(IStashMutationCompletion completion, StashMutationResult result)
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
            List<LocalizedText> errors = result.Errors.ToList();
            foreach (LocalizedText error in completed.Errors)
            {
                if (errors.Contains(error) == false)
                {
                    errors.Add(error);
                }
            }
            if (result.Errors.Count == 0)
            {
                if (string.IsNullOrEmpty(result.ErrorText) == false)
                {
                    errors.Add(new LocalizedText(new GitException(result.ErrorText)));
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
            string errorText = string.Join(Environment.NewLine, errors.Select(error => error.GetText(_screen.Strings)));
            return new StashMutationResult(result.Repository, result.Kind, result.Succeeded, result.WorktreeMayHaveChanged, result.StashesMayHaveChanged, completed.WorktreeStatus ?? result.WorktreeStatus, errorText, errors);
        }
    }
}
