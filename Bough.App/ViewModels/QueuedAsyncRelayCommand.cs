using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Dignus.Log;

namespace Bough.App.ViewModels
{
    public class QueuedAsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private readonly Action<Exception> _onError;

        public QueuedAsyncRelayCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception> onError)
        {
            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            if (canExecute == null)
            {
                throw new ArgumentNullException(nameof(canExecute));
            }

            if (onError == null)
            {
                throw new ArgumentNullException(nameof(onError));
            }

            _execute = execute;
            _canExecute = canExecute;
            _onError = onError;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return _canExecute();
        }

        public async void Execute(object parameter)
        {
            if (CanExecute(parameter) == false)
            {
                return;
            }

            try
            {
                await _execute();
            }
            catch (Exception exception)
            {
                try
                {
                    _onError(exception);
                }
                catch (Exception handlingException)
                {
                    try
                    {
                        LogHelper.Error(exception);
                        LogHelper.Error(handlingException);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void NotifyCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
