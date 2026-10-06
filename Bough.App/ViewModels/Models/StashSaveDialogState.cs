using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.App.ViewModels.Models
{
    public class StashSaveDialogState : ViewModelBase
    {
        private readonly List<StashSaveRefreshFailure> _refreshFailures = [];
        private bool _batchSaved;
        private bool _batchFailed;

        public bool Saved { get; private set; }

        public int PendingSaves { get; private set; }

        public bool IsRefreshing { get; private set; }

        public string SaveErrorText { get; private set; } = string.Empty;

        public Exception SaveException { get; private set; }

        public IReadOnlyList<StashSaveRefreshFailure> RefreshFailures => _refreshFailures;

        public bool HasRefreshFailures => _refreshFailures.Count > 0;

        public bool CanSave
        {
            get
            {
                if (HasRefreshFailures == true)
                {
                    return false;
                }
                return IsRefreshing == false;
            }
        }

        public bool CanRetry
        {
            get
            {
                if (HasRefreshFailures == false)
                {
                    return false;
                }
                if (PendingSaves != 0)
                {
                    return false;
                }
                return IsRefreshing == false;
            }
        }

        public bool CanClose
        {
            get
            {
                if (PendingSaves != 0)
                {
                    return false;
                }
                return IsRefreshing == false;
            }
        }

        public bool ShouldClose
        {
            get
            {
                if (CanClose == false)
                {
                    return false;
                }
                if (_batchSaved == false)
                {
                    return false;
                }
                if (_batchFailed == true)
                {
                    return false;
                }
                return HasRefreshFailures == false;
            }
        }

        internal void BeginSave()
        {
            if (PendingSaves == 0)
            {
                _batchSaved = false;
                _batchFailed = false;
                SaveErrorText = string.Empty;
                SaveException = null;
            }
            PendingSaves++;
            OnPropertyChanged(string.Empty);
        }

        internal void CompleteSave(StashMutationResult result, Exception exception)
        {
            if (result == null)
            {
                _batchFailed = true;
                SaveException = exception;
                return;
            }
            if (result.Succeeded == false)
            {
                _batchFailed = true;
                SaveErrorText = result.ErrorText;
                SaveException = exception;
                return;
            }

            Saved = true;
            _batchSaved = true;
            if (exception != null)
            {
                _refreshFailures.Add(new StashSaveRefreshFailure(result, exception));
                return;
            }
            if (string.IsNullOrEmpty(result.ErrorText) == false)
            {
                _refreshFailures.Add(new StashSaveRefreshFailure(result, null));
            }
        }

        internal void EndSave()
        {
            PendingSaves--;
            OnPropertyChanged(string.Empty);
        }

        internal void BeginRefresh()
        {
            IsRefreshing = true;
            OnPropertyChanged(string.Empty);
        }

        internal void CompleteRefresh(StashMutationResult result, Exception exception)
        {
            StringComparison comparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows() == true)
            {
                comparison = StringComparison.OrdinalIgnoreCase;
            }
            _refreshFailures.RemoveAll(failure => string.Equals(failure.Result.Repository.RootPath, result.Repository.RootPath, comparison));
            if (exception != null)
            {
                _refreshFailures.Add(new StashSaveRefreshFailure(result, exception));
                return;
            }
            if (string.IsNullOrEmpty(result.ErrorText) == false)
            {
                _refreshFailures.Add(new StashSaveRefreshFailure(result, null));
            }
        }

        internal StashMutationResult[] GetRefreshTargets()
        {
            StringComparer comparer = StringComparer.Ordinal;
            if (OperatingSystem.IsWindows() == true)
            {
                comparer = StringComparer.OrdinalIgnoreCase;
            }
            return _refreshFailures.GroupBy(failure => failure.Result.Repository.RootPath, comparer).Select(group => group.Last().Result).ToArray();
        }

        internal void EndRefresh()
        {
            IsRefreshing = false;
            OnPropertyChanged(string.Empty);
        }
    }
}
