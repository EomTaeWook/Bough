using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bough.App.Internals;
using Bough.App.ViewModels;

namespace Bough.App.Localization
{
    public class LanguageChangeBinding
    {
        private readonly Control _control;
        private readonly Func<StringHelper> _getStrings;
        private readonly Action _refreshLabels;
        private StringHelper _strings;
        private bool _attached;
        private bool _closed;

        private LanguageChangeBinding(Control control, Func<StringHelper> getStrings, Action refreshLabels)
        {
            _control = control;
            _getStrings = getStrings;
            _refreshLabels = refreshLabels;
            control.AttachedToVisualTree += delegate { _attached = true; Bind(); };
            control.DetachedFromVisualTree += delegate { _attached = false; Unbind(); };
            control.DataContextChanged += delegate { if (_attached) Bind(); };
            if (control is Window window)
            {
                window.Opened += delegate { _attached = true; Bind(); };
                window.Closed += delegate { _closed = true; _attached = false; Unbind(); };
            }
        }

        public static LanguageChangeBinding Bind(Control control, Func<StringHelper> getStrings, Action refreshLabels = null)
        {
            LanguageChangeBinding binding = new(control, getStrings, refreshLabels);
            binding.Rebind();
            return binding;
        }

        public void Rebind()
        {
            if (_closed)
            {
                return;
            }
            if (_control is Window window)
            {
                _attached = window.IsVisible;
            }
            else
            {
                _attached = _control.IsAttachedToVisualTree();
            }
            if (_attached == false)
            {
                Unbind();
                return;
            }
            Bind();
        }

        private void Bind()
        {
            Unbind();
            _strings = _getStrings();
            if (_strings == null)
            {
                return;
            }
            _strings.LanguageChanged += OnLanguageChanged;
            Refresh();
        }

        private void Unbind()
        {
            if (_strings == null)
            {
                return;
            }
            _strings.LanguageChanged -= OnLanguageChanged;
            _strings = null;
        }

        private void OnLanguageChanged(StringLanguage language)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Refresh();
                return;
            }
            Dispatcher.UIThread.Post(Refresh);
        }

        private void Refresh()
        {
            if (_attached == false)
            {
                return;
            }
            if (_strings == null)
            {
                return;
            }
            _refreshLabels?.Invoke();
            if (_control.DataContext is ViewModelBase viewModel)
            {
                viewModel.RefreshLocalization();
            }
        }
    }
}
