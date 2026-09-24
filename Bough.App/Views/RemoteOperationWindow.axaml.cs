using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Bough.App.Localization;
using Bough.App.ViewModels;
using Bough.Core.Git;

namespace Bough.App.Views
{
    public partial class RemoteOperationWindow : Window
    {
        private readonly RemoteOperationsViewModel _viewModel;
        private readonly Func<Task<bool>> _operation;
        private readonly GitErrorLocalizer _errorLocalizer;
        private readonly StringHelper _strings;
        private readonly CancellationTokenSource _cancellation;
        private readonly GitOperationQueue _operationQueue;
        private readonly string _repositoryRoot;
        private readonly bool _closeOnSuccess;
        private readonly DispatcherTimer _elapsedTimer;
        private DateTimeOffset _startedAt;
        private bool _started;
        private bool _finished;

        public RemoteOperationWindow()
        {
            InitializeComponent();
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += ElapsedTick;
            Opened += OperationOpened;
            Closed += OperationClosed;
        }

        public RemoteOperationWindow(RemoteOperationsViewModel viewModel, string operationName, string target, Func<Task<bool>> operation, bool closeOnSuccess, StringHelper strings, GitErrorLocalizer errorLocalizer, CancellationTokenSource cancellation, GitOperationQueue operationQueue, string repositoryRoot)
            : this()
        {
            _viewModel = viewModel;
            _operation = operation;
            _errorLocalizer = errorLocalizer;
            _strings = strings;
            _cancellation = cancellation;
            _operationQueue = operationQueue;
            _repositoryRoot = repositoryRoot;
            _operationQueue.StateChanged += OperationQueueStateChanged;
            _closeOnSuccess = closeOnSuccess;
            DataContext = viewModel;
            Title = operationName;
            OperationNameText.Text = operationName;
            TargetText.Text = target;
            ToolTip.SetTip(TransferStatusBlock, strings.GetString("RemoteTransferStatusTip"));
            AutomationProperties.SetName(ResultText, strings.GetString("RemoteResultAutomation"));
            PullSummaryHeading.Text = strings.GetString("RemotePullSummaryHeading");
            AutomationProperties.SetName(PullSummaryBlock, strings.GetString("RemotePullSummaryAutomation"));
            StopButton.Content = strings.GetString("RemoteStopAction");
            ToolTip.SetTip(StopButton, strings.GetString("RemoteStopTip"));
            AutomationProperties.SetName(StopButton, strings.GetString("RemoteStopAutomation"));
            CloseButton.Content = strings.GetString("RemoteCloseAction");
            AutomationProperties.SetName(CloseButton, strings.GetString("RemoteCloseAutomation"));
        }

        protected override void OnClosing(WindowClosingEventArgs eventArgs)
        {
            if (_started == true)
            {
                if (_finished == false)
                {
                    eventArgs.Cancel = true;
                    return;
                }
            }
            base.OnClosing(eventArgs);
        }

        private async void OperationOpened(object sender, EventArgs eventArgs)
        {
            if (_operation == null)
            {
                _finished = true;
                StopButton.IsVisible = false;
                CloseButton.IsVisible = true;
                return;
            }
            _started = true;
            _startedAt = DateTimeOffset.UtcNow;
            UpdateElapsed();
            _elapsedTimer.Start();
            bool succeeded = false;
            try
            {
                succeeded = await _operation();
            }
            catch (OperationCanceledException)
            {
                OutcomeText.Text = _strings.GetString("RemoteOutcomeCanceled");
                ResultText.Text = _strings.GetString("RemoteOperationCanceled");
            }
            catch (Exception exception)
            {
                OutcomeText.Text = _strings.GetString("RemoteOutcomeFailed");
                ResultText.Text = _errorLocalizer.GetDisplayMessage(exception);
            }
            finally
            {
                _finished = true;
                _elapsedTimer.Stop();
                StopButton.IsVisible = false;
                QueueStatusText.Text = string.Empty;
            }
            if (succeeded == true)
            {
                if (_closeOnSuccess == true)
                {
                    Close();
                    return;
                }
            }
            CloseButton.IsVisible = true;
            CloseButton.Focus();
        }

        private void ElapsedTick(object sender, EventArgs eventArgs)
        {
            UpdateElapsed();
        }

        private void UpdateElapsed()
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - _startedAt;
            ElapsedText.Text = _strings.Format("RemoteElapsedTime", $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}");
        }

        private void StopClicked(object sender, RoutedEventArgs eventArgs)
        {
            _cancellation.Cancel();
            _viewModel.Cancel();
        }

        private void CloseClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (_finished == false)
            {
                return;
            }
            Close();
        }

        private void OperationClosed(object sender, EventArgs eventArgs)
        {
            _elapsedTimer.Stop();
            _operationQueue?.StateChanged -= OperationQueueStateChanged;
        }

        private void OperationQueueStateChanged(GitOperationQueueState state)
        {
            if (Dispatcher.UIThread.CheckAccess() == false)
            {
                Dispatcher.UIThread.Post(() => OperationQueueStateChanged(state));
                return;
            }
            state = _operationQueue.GetState(state.RepositoryRoot);
            if (_finished == true)
            {
                return;
            }
            StringComparison comparison = StringComparison.Ordinal;
            if (OperatingSystem.IsWindows())
            {
                comparison = StringComparison.OrdinalIgnoreCase;
            }
            if (string.Equals(_repositoryRoot, state.RepositoryRoot, comparison) == false)
            {
                return;
            }
            string status = state.RunningOperationName;
            if (state.PendingCount > 0)
            {
                if (status.Length > 0)
                {
                    status += " · ";
                }
                status += $"+{state.PendingCount}";
            }
            QueueStatusText.Text = status;
        }
    }
}
