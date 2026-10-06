using System;

namespace Bough.App.ViewModels.Models
{
    public class StashSaveRefreshFailure
    {
        public StashSaveRefreshFailure(StashMutationResult result, Exception exception)
        {
            Result = result;
            Exception = exception;
        }

        public StashMutationResult Result { get; }

        public Exception Exception { get; }
    }
}
