using System;
using System.Threading.Tasks;
using Bough.App.Interfaces;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;

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
                result = await _screen.SaveAsync(completion);
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
                        result = await _screen.RetrySaveRefreshAsync(target, completion);
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

    }
}
