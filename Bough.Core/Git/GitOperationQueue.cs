using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dignus.Collections;
using Dignus.Log;

namespace Bough.Core.Git
{
    public sealed class GitOperationQueueState
    {
        public GitOperationQueueState(string repositoryRoot, string runningOperationName, int pendingCount)
        {
            RepositoryRoot = repositoryRoot;
            RunningOperationName = runningOperationName;
            PendingCount = pendingCount;
        }

        public string RepositoryRoot { get; }
        public string RunningOperationName { get; }
        public bool IsRunning { get { return RunningOperationName.Length > 0; } }
        public int PendingCount { get; }
    }

    public sealed class GitOperationQueue
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, RepositoryLane> _lanes;
        private readonly AsyncLocal<ExecutionFrame> _execution = new();

        public GitOperationQueue()
        {
            StringComparer comparer = StringComparer.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                comparer = StringComparer.OrdinalIgnoreCase;
            }
            _lanes = new Dictionary<string, RepositoryLane>(comparer);
        }

        // Notifications can arrive out of order after the lane lock is released.
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
                    if (_lanes.Comparer.Equals(frame.RepositoryRoot, root))
                    {
                        throw new InvalidOperationException("A Git operation cannot enqueue another operation for the same repository. Await dependent work inside the existing callback.");
                    }
                }
            }

            RepositoryLane lane;
            QueuedOperation<T> request;
            GitOperationQueueState state;
            bool startConsumer = false;
            lock (_sync)
            {
                if (_lanes.TryGetValue(root, out lane) == false)
                {
                    lane = new RepositoryLane(root);
                    _lanes.Add(root, lane);
                }

                request = new QueuedOperation<T>(operationName, action, cancellationToken);
                lane.Pending.Add(request);
                lane.PendingCount++;
                if (lane.ConsumerRunning == false)
                {
                    lane.ConsumerRunning = true;
                    startConsumer = true;
                }
                state = CreateState(lane);
            }

            PublishState(state);
            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = cancellationToken.Register(() => CancelPendingRequest(lane, request));
                bool keepRegistration;
                lock (_sync)
                {
                    keepRegistration = request.IsPending;
                    if (ReferenceEquals(lane.Running, request))
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
                _ = ConsumeAsync(lane);
            }
            return request.Task;
        }

        public GitOperationQueueState GetState(string repositoryRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
            string root = NormalizeRoot(repositoryRoot);
            lock (_sync)
            {
                if (_lanes.TryGetValue(root, out RepositoryLane lane))
                {
                    return CreateState(lane);
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
                if (_lanes.TryGetValue(root, out RepositoryLane lane) == false)
                {
                    return;
                }
                if (lane.PendingCount == 0)
                {
                    return;
                }
                while (lane.Pending.Count > 0)
                {
                    QueuedOperation request = lane.Pending.Read();
                    if (request.IsPending == false)
                    {
                        registrations.Add(TakeRegistrationUnderLock(request));
                        continue;
                    }
                    registrations.Add(CancelPendingUnderLock(lane, request, default));
                }
                state = CreateState(lane);
            }
            foreach (CancellationTokenRegistration registration in registrations)
            {
                registration.Dispose();
            }
            PublishState(state);
        }

        private void CancelPendingRequest(RepositoryLane lane, QueuedOperation request)
        {
            GitOperationQueueState state;
            CancellationTokenRegistration registration;
            lock (_sync)
            {
                if (request.IsPending == false)
                {
                    return;
                }
                registration = CancelPendingUnderLock(lane, request, request.CancellationToken);
                RemoveCanceledRequestUnderLock(lane, request);
                state = CreateState(lane);
            }
            registration.Dispose();
            PublishState(state);
        }

        private static CancellationTokenRegistration CancelPendingUnderLock(RepositoryLane lane, QueuedOperation request, CancellationToken cancellationToken)
        {
            if (request.IsPending == false)
            {
                return default;
            }
            request.IsPending = false;
            lane.PendingCount--;
            request.Cancel(cancellationToken);
            return TakeRegistrationUnderLock(request);
        }

        private static CancellationTokenRegistration TakeRegistrationUnderLock(QueuedOperation request)
        {
            CancellationTokenRegistration registration = request.Registration;
            request.Registration = default;
            return registration;
        }

        private static void RemoveCanceledRequestUnderLock(RepositoryLane lane, QueuedOperation canceledRequest)
        {
            ArrayQueue<QueuedOperation> remaining = new();
            while (lane.Pending.Count > 0)
            {
                QueuedOperation request = lane.Pending.Read();
                if (ReferenceEquals(request, canceledRequest))
                {
                    continue;
                }
                remaining.Add(request);
            }
            lane.Pending = remaining;
        }

        private async Task ConsumeAsync(RepositoryLane lane)
        {
            await Task.Yield();
            while (true)
            {
                QueuedOperation request = null;
                List<CancellationTokenRegistration> skippedRegistrations = new();
                GitOperationQueueState state;
                lock (_sync)
                {
                    while (lane.Pending.Count > 0)
                    {
                        QueuedOperation next = lane.Pending.Read();
                        if (next.IsPending == false)
                        {
                            skippedRegistrations.Add(TakeRegistrationUnderLock(next));
                            continue;
                        }
                        request = next;
                        request.IsPending = false;
                        lane.PendingCount--;
                        lane.Running = request;
                        break;
                    }
                    if (request == null)
                    {
                        lane.ConsumerRunning = false;
                        if (_lanes.TryGetValue(lane.RepositoryRoot, out RepositoryLane currentLane))
                        {
                            if (ReferenceEquals(currentLane, lane))
                            {
                                _lanes.Remove(lane.RepositoryRoot);
                            }
                        }
                    }
                    state = CreateState(lane);
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
                ExecutionFrame currentFrame = new(lane.RepositoryRoot);
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
                        lane.Running = null;
                        state = CreateState(lane);
                    }
                    PublishState(state);
                }
            }
        }

        private static string NormalizeRoot(string repositoryRoot)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        }

        private static GitOperationQueueState CreateState(RepositoryLane lane)
        {
            string running = lane.Running?.OperationName ?? string.Empty;
            return new GitOperationQueueState(lane.RepositoryRoot, running, lane.PendingCount);
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

        private sealed class RepositoryLane
        {
            public RepositoryLane(string repositoryRoot)
            {
                RepositoryRoot = repositoryRoot;
            }

            public string RepositoryRoot { get; }
            public ArrayQueue<QueuedOperation> Pending { get; set; } = new();
            public QueuedOperation Running { get; set; }
            public int PendingCount { get; set; }
            public bool ConsumerRunning { get; set; }
        }

        private sealed class ExecutionFrame
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

        private sealed class QueuedOperation<T> : QueuedOperation
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
