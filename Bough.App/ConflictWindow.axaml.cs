using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Bough.App.ViewModels;
using Bough.App.Views;

namespace Bough.App
{
    public partial class ConflictWindow : Window
    {
        private readonly ConflictResolutionViewModel _viewModel;
        private readonly Func<Task> _refreshRepository;
        private bool _closeConfirmed;
        private bool _confirmationPending;

        public ConflictWindow()
        {
            InitializeComponent();
            Closing += async (sender, eventArgs) =>
            {
                if (_viewModel == null)
                {
                    return;
                }
                if (_closeConfirmed == true)
                {
                    return;
                }
                if (_viewModel.HasUnsavedConflictEdits == false)
                {
                    return;
                }

                eventArgs.Cancel = true;
                if (_confirmationPending == true)
                {
                    return;
                }

                _confirmationPending = true;
                bool discard;
                try
                {
                    discard = await GitActionDialogs.ConfirmAsync(this,
                        _viewModel.DiscardResolutionTitle,
                        _viewModel.DiscardResolutionCloseMessage,
                        _viewModel.DiscardResolutionConfirmText);
                }
                finally
                {
                    _confirmationPending = false;
                }
                if (discard == true)
                {
                    CloseAfterConfirmation();
                }
            };
            KeyDown += async (sender, eventArgs) =>
            {
                if (eventArgs.Key != Key.F5)
                {
                    return;
                }
                eventArgs.Handled = true;
                if (_refreshRepository != null)
                {
                    await _refreshRepository();
                }
            };
        }

        public ConflictWindow(ConflictResolutionViewModel viewModel, Func<Task> refreshRepository)
            : this()
        {
            if (viewModel == null)
            {
                throw new ArgumentNullException(nameof(viewModel));
            }
            if (refreshRepository == null)
            {
                throw new ArgumentNullException(nameof(refreshRepository));
            }
            _viewModel = viewModel;
            _refreshRepository = refreshRepository;
            DataContext = viewModel;
        }

        public void CloseAfterConfirmation()
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
