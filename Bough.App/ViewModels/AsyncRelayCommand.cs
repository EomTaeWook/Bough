using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Bough.App.ViewModels
{
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute)
            : this(execute, AlwaysCanExecute)
        {
        }

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            if (_isExecuting == true)
            {
                return false;
            }

            return _canExecute();
        }

        public async void Execute(object parameter)
        {
            if (CanExecute(parameter) == false)
            {
                return;
            }

            _isExecuting = true;
            NotifyCanExecuteChanged();

            try
            {
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                NotifyCanExecuteChanged();
            }
        }

        public void NotifyCanExecuteChanged()
        {
            EventHandler handler = CanExecuteChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static bool AlwaysCanExecute()
        {
            return true;
        }
    }
}
