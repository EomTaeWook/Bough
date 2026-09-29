using System;
using System.Threading.Tasks;
using Dignus.Collections;

namespace Bough.App.Presenters
{
    public class BranchCreationPresenter
    {
        private readonly Func<string, Task<string>> _submit;
        private readonly ArrayQueue<string> _requests = [];
        private Task _consumerTask = Task.CompletedTask;
        private bool _cancelRequested;
        private bool _completed;

        public BranchCreationPresenter(Func<string, Task<string>> submit)
        {
            _submit = submit;
        }

        public event Action StateChanged;
        public event Action<string, string, Exception> RequestFailed;
        public event Action<bool> Finished;

        public string ProcessingName { get; private set; }
        public int PendingCount { get { return _requests.Count; } }
        public bool IsCancelRequested { get { return _cancelRequested; } }
        public bool IsInputClosed { get { return _cancelRequested || _completed; } }
        public bool IsRunning { get { return _consumerTask.IsCompleted == false; } }
        public Task Completion { get { return _consumerTask; } }

        public void Submit(string name)
        {
            if (IsInputClosed)
            {
                return;
            }
            _requests.Add(name);
            StateChanged?.Invoke();
            if (_consumerTask.IsCompleted)
            {
                _consumerTask = ConsumeAsync();
            }
        }

        public void CancelPending()
        {
            if (IsInputClosed)
            {
                return;
            }
            _cancelRequested = true;
            _requests.Clear();
            StateChanged?.Invoke();
            if (_consumerTask.IsCompleted)
            {
                Finished?.Invoke(false);
            }
        }

        private async Task ConsumeAsync()
        {
            await Task.Yield();
            bool completedSuccessfully = false;
            bool hadFailure = false;
            while (_requests.Count > 0)
            {
                if (_cancelRequested)
                {
                    break;
                }
                string requestedName = _requests.Read();
                ProcessingName = requestedName;
                StateChanged?.Invoke();
                string failure = null;
                Exception error = null;
                try
                {
                    failure = await _submit(requestedName);
                }
                catch (Exception exception)
                {
                    error = exception;
                }
                if (failure == null && error == null)
                {
                    completedSuccessfully = true;
                }
                else
                {
                    hadFailure = true;
                    RequestFailed?.Invoke(requestedName, failure, error);
                }
                ProcessingName = null;
                StateChanged?.Invoke();
            }
            if (_cancelRequested)
            {
                Finished?.Invoke(false);
                return;
            }
            if (completedSuccessfully && hadFailure == false)
            {
                _completed = true;
                StateChanged?.Invoke();
                Finished?.Invoke(true);
                return;
            }
            Finished?.Invoke(false);
        }
    }
}
