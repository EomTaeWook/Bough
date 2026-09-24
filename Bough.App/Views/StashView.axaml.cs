using Avalonia.Controls;
using Avalonia.Interactivity;
using Bough.App.ViewModels;

namespace Bough.App.Views
{
    public partial class StashView : UserControl
    {
        public StashView()
        {
            InitializeComponent();
        }

        private async void ApplyClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not StashViewModel viewModel)
            {
                return;
            }

            if (viewModel.CanUseSelectedStash == false)
            {
                return;
            }

            IStashMutationCompletion completion = null;
            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                completion = owner.DataContext as IStashMutationCompletion;
            }

            StashMutationResult result = await viewModel.ApplyAsync();
            if (completion != null)
            {
                await completion.CompleteStashApplyAsync(result);
            }
        }

        private async void PopClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not StashViewModel viewModel)
            {
                return;
            }

            if (viewModel.CanUseSelectedStash == false)
            {
                return;
            }

            IStashMutationCompletion completion = null;
            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                completion = owner.DataContext as IStashMutationCompletion;
            }

            StashMutationResult result = await viewModel.PopAsync();
            if (completion != null)
            {
                await completion.CompleteStashPopAsync(result);
            }
        }

        private async void ConfirmDropClicked(object sender, RoutedEventArgs eventArgs)
        {
            if (DataContext is not StashViewModel viewModel)
            {
                return;
            }

            if (viewModel.CanConfirmDropStash == false)
            {
                return;
            }

            IStashMutationCompletion completion = null;
            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                completion = owner.DataContext as IStashMutationCompletion;
            }

            StashMutationResult result = await viewModel.ConfirmDropAsync();
            if (completion != null)
            {
                await completion.CompleteStashDropAsync(result);
            }
        }
    }
}
