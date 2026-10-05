using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;
using Dignus.Log;
using Bough.Core.Git.Models;

namespace Bough.Core.Git
{
    public class GitOperationQueue
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, RepositoryOperationQueue> _repositoryQueues;
        private readonly AsyncLocal<ExecutionFrame> _execution = new();

        public GitOperationQueue()
        {
            StringComparer comparer = StringComparer.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                comparer = StringComparer.OrdinalIgnoreCase;
            }
            _repositoryQueues = new Dictionary<string, RepositoryOperationQueue>(comparer);
        }

        // Notifications can arrive out of order after the queue lock is released.
        // Subscribers must marshal to their UI context and call GetState for the root just before applying it.
        public event Action<GitOperationQueueState> StateChanged;

        // Every accepted request runs in FIFO order unless the caller explicitly cancels it.
        // The callback must include dependent commands and refreshes; enqueuing the same root inside it is rejected.
        public Task EnqueueAsync(string repositoryRoot, string operationName,
            Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(action);
            return EnqueueAsync<object>(repositoryRoot, operationName, async token =>
            {
                await action(token);
                return null;
            }, cancellationToken);
        }

        public Task<T> EnqueueAsync<T>(string repositoryRoot, string operationName,
            Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
            ArgumentNullException.ThrowIfNull(action);
            cancellationToken.ThrowIfCancellationRequested();

            string root = NormalizeRoot(repositoryRoot);
            ExecutionFrame frame = _execution.Value;
            if (frame != null)
            {
                if (frame.IsActive == true)
                {
                    if (_repositoryQueues.Comparer.Equals(frame.RepositoryRoot, root))
                    {
                        throw new GitException("GitOperationQueueReentry", null, Array.Empty<object>());
                    }
                }
            }

            RepositoryOperationQueue repositoryQueue;
            QueuedOperation<T> request;
            GitOperationQueueState state;
            bool startConsumer = false;
            lock (_sync)
            {
                if (_repositoryQueues.TryGetValue(root, out repositoryQueue) == false)
                {
                    repositoryQueue = new RepositoryOperationQueue(root);
                    _repositoryQueues.Add(root, repositoryQueue);
                }

                request = new QueuedOperation<T>(operationName, action, cancellationToken);
                repositoryQueue.Pending.Add(request);
                repositoryQueue.PendingCount++;
                if (repositoryQueue.ConsumerRunning == false)
                {
                    repositoryQueue.ConsumerRunning = true;
                    startConsumer = true;
                }
                state = CreateState(repositoryQueue);
            }

            PublishState(state);
            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = cancellationToken.Register(() => CancelPendingRequest(repositoryQueue, request));
                bool keepRegistration;
                lock (_sync)
                {
                    keepRegistration = request.IsPending;
                    if (ReferenceEquals(repositoryQueue.Running, request))
                    {
                        keepRegistration = true;
                    }
                    if (keepRegistration)
                    {
                        request.Registration = registration;
                    }
                }
                if (keepRegistration == false)
                {
                    registration.Dispose();
                }
            }
            if (startConsumer)
            {
                _ = ConsumeAsync(repositoryQueue);
            }
            return request.Task;
        }

        public GitOperationQueueState GetState(string repositoryRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
            string root = NormalizeRoot(repositoryRoot);
            lock (_sync)
            {
                if (_repositoryQueues.TryGetValue(root, out RepositoryOperationQueue repositoryQueue))
                {
                    return CreateState(repositoryQueue);
                }
            }
            return new GitOperationQueueState(root, string.Empty, 0);
        }

        // Running work is left alone. Every queued task completes as canceled.
        public void CancelPending(string repositoryRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
            string root = NormalizeRoot(repositoryRoot);
            List<CancellationTokenRegistration> registrations = new();
            GitOperationQueueState state;
            lock (_sync)
            {
                if (_repositoryQueues.TryGetValue(root, out RepositoryOperationQueue repositoryQueue) == false)
                {
                    return;
                }
                if (repositoryQueue.PendingCount == 0)
                {
                    return;
                }
                while (repositoryQueue.Pending.Count > 0)
                {
                    QueuedOperation request = repositoryQueue.Pending.Read();
                    if (request.IsPending == false)
                    {
                        registrations.Add(TakeRegistrationUnderLock(request));
                        continue;
                    }
                    registrations.Add(CancelPendingUnderLock(repositoryQueue, request, default));
                }
                state = CreateState(repositoryQueue);
            }
            foreach (CancellationTokenRegistration registration in registrations)
            {
                registration.Dispose();
            }
            PublishState(state);
        }

        private void CancelPendingRequest(RepositoryOperationQueue repositoryQueue, QueuedOperation request)
        {
            GitOperationQueueState state;
            CancellationTokenRegistration registration;
            lock (_sync)
            {
                if (request.IsPending == false)
                {
                    return;
                }
                registration = CancelPendingUnderLock(repositoryQueue, request, request.CancellationToken);
                RemoveCanceledRequestUnderLock(repositoryQueue, request);
                state = CreateState(repositoryQueue);
            }
            registration.Dispose();
            PublishState(state);
        }

        private static CancellationTokenRegistration CancelPendingUnderLock(RepositoryOperationQueue repositoryQueue, QueuedOperation request, CancellationToken cancellationToken)
        {
            if (request.IsPending == false)
            {
                return default;
            }
            request.IsPending = false;
            repositoryQueue.PendingCount--;
            request.Cancel(cancellationToken);
            return TakeRegistrationUnderLock(request);
        }

        private static CancellationTokenRegistration TakeRegistrationUnderLock(QueuedOperation request)
        {
            CancellationTokenRegistration registration = request.Registration;
            request.Registration = default;
            return registration;
        }

        private static void RemoveCanceledRequestUnderLock(RepositoryOperationQueue repositoryQueue, QueuedOperation canceledRequest)
        {
            ArrayQueue<QueuedOperation> remaining = new();
            while (repositoryQueue.Pending.Count > 0)
            {
                QueuedOperation request = repositoryQueue.Pending.Read();
                if (ReferenceEquals(request, canceledRequest))
                {
                    continue;
                }
                remaining.Add(request);
            }
            repositoryQueue.Pending = remaining;
        }

        private async Task ConsumeAsync(RepositoryOperationQueue repositoryQueue)
        {
            await Task.Yield();
            while (true)
            {
                QueuedOperation request = null;
                List<CancellationTokenRegistration> skippedRegistrations = new();
                GitOperationQueueState state;
                lock (_sync)
                {
                    while (repositoryQueue.Pending.Count > 0)
                    {
                        QueuedOperation next = repositoryQueue.Pending.Read();
                        if (next.IsPending == false)
                        {
                            skippedRegistrations.Add(TakeRegistrationUnderLock(next));
                            continue;
                        }
                        request = next;
                        request.IsPending = false;
                        repositoryQueue.PendingCount--;
                        repositoryQueue.Running = request;
                        break;
                    }
                    if (request == null)
                    {
                        repositoryQueue.ConsumerRunning = false;
                        if (_repositoryQueues.TryGetValue(repositoryQueue.RepositoryRoot, out RepositoryOperationQueue currentQueue))
                        {
                            if (ReferenceEquals(currentQueue, repositoryQueue))
                            {
                                _repositoryQueues.Remove(repositoryQueue.RepositoryRoot);
                            }
                        }
                    }
                    state = CreateState(repositoryQueue);
                }
                foreach (CancellationTokenRegistration registration in skippedRegistrations)
                {
                    registration.Dispose();
                }
                PublishState(state);
                if (request == null)
                {
                    return;
                }

                ExecutionFrame previousFrame = _execution.Value;
                ExecutionFrame currentFrame = new(repositoryQueue.RepositoryRoot);
                _execution.Value = currentFrame;
                try
                {
                    await request.ExecuteAsync();
                }
                finally
                {
                    currentFrame.IsActive = false;
                    _execution.Value = previousFrame;
                    request.Registration.Dispose();
                    lock (_sync)
                    {
                        repositoryQueue.Running = null;
                        state = CreateState(repositoryQueue);
                    }
                    PublishState(state);
                }
            }
        }

        private static string NormalizeRoot(string repositoryRoot)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        }

        private static GitOperationQueueState CreateState(RepositoryOperationQueue repositoryQueue)
        {
            string running = repositoryQueue.Running?.OperationName ?? string.Empty;
            return new GitOperationQueueState(repositoryQueue.RepositoryRoot, running, repositoryQueue.PendingCount);
        }

        private void PublishState(GitOperationQueueState state)
        {
            try
            {
                StateChanged?.Invoke(state);
            }
            catch (Exception exception)
            {
                LogHelper.Error($"Git operation queue state notification failed: {exception}");
            }
        }

        private class RepositoryOperationQueue
        {
            public RepositoryOperationQueue(string repositoryRoot)
            {
                RepositoryRoot = repositoryRoot;
            }

            public string RepositoryRoot { get; }
            public ArrayQueue<QueuedOperation> Pending { get; set; } = new();
            public QueuedOperation Running { get; set; }
            public int PendingCount { get; set; }
            public bool ConsumerRunning { get; set; }
        }

        private class ExecutionFrame
        {
            public ExecutionFrame(string repositoryRoot)
            {
                RepositoryRoot = repositoryRoot;
                IsActive = true;
            }

            public string RepositoryRoot { get; }
            public bool IsActive { get; set; }
        }

        private abstract class QueuedOperation
        {
            protected QueuedOperation(string operationName, CancellationToken cancellationToken)
            {
                OperationName = operationName;
                CancellationToken = cancellationToken;
                IsPending = true;
            }

            public string OperationName { get; }
            public CancellationToken CancellationToken { get; }
            public CancellationTokenRegistration Registration { get; set; }
            public bool IsPending { get; set; }
            public abstract Task ExecuteAsync();
            public abstract void Cancel(CancellationToken cancellationToken);
        }

        private class QueuedOperation<T> : QueuedOperation
        {
            private readonly Func<CancellationToken, Task<T>> _action;
            private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public QueuedOperation(string operationName, Func<CancellationToken, Task<T>> action,
                CancellationToken cancellationToken)
                : base(operationName, cancellationToken)
            {
                _action = action;
            }

            public Task<T> Task { get { return _completion.Task; } }

            public override async Task ExecuteAsync()
            {
                try
                {
                    CancellationToken.ThrowIfCancellationRequested();
                    T result = await _action(CancellationToken);
                    _completion.TrySetResult(result);
                }
                catch (OperationCanceledException exception)
                {
                    _completion.TrySetCanceled(exception.CancellationToken);
                }
                catch (Exception exception)
                {
                    _completion.TrySetException(exception);
                }
            }

            public override void Cancel(CancellationToken cancellationToken)
            {
                _completion.TrySetCanceled(cancellationToken);
            }
        }
    }
}
