using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Bough.App.ViewModels
{
    internal static class UiQueuedOperation
    {
        public static Task RunAsync(Func<Task> operation)
        {
            if (Dispatcher.UIThread.CheckAccess() == true)
            {
                return operation();
            }

            TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await operation();
                    completion.SetResult(true);
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
            return completion.Task;
        }

        public static Task<TResult> RunAsync<TResult>(Func<Task<TResult>> operation)
        {
            if (Dispatcher.UIThread.CheckAccess() == true)
            {
                return operation();
            }

            TaskCompletionSource<TResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    TResult result = await operation();
                    completion.SetResult(result);
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
            return completion.Task;
        }
    }
}
