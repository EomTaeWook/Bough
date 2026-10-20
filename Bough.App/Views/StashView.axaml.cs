using Avalonia.Controls;
using Avalonia.Interactivity;
using Bough.App.Interfaces;
using Bough.App.Localization;
using Bough.App.ViewModels;

namespace Bough.App.Views
{
    public partial class StashView : UserControl
    {
        private StashViewModel _previewSource;
        private bool _previewAttached;

        public StashView()
        {
            InitializeComponent();
            LanguageChangeBinding.Bind(this, () => (DataContext as StashViewModel)?.Strings);
            DataContextChanged += delegate { BindPreview(); };
            AttachedToVisualTree += delegate { _previewAttached = true; BindPreview(); };
            DetachedFromVisualTree += delegate { _previewAttached = false; UnbindPreview(); };
        }

        private void BindPreview()
        {
            StashViewModel next = DataContext as StashViewModel;
            if (object.ReferenceEquals(next, _previewSource) == false)
            {
                UnbindPreview();
            }
            if (_previewAttached == false)
            {
                return;
            }
            _previewSource = next;
            _previewSource?.ResumePreview();
        }

        private void UnbindPreview()
        {
            _previewSource?.SuspendPreview();
            _previewSource = null;
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

            await viewModel.ApplyAsync(completion);
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

            await viewModel.PopAsync(completion);
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

            await viewModel.ConfirmDropAsync(completion);
        }
    }
}
