using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Bough.App.Internals;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;
using Dignus.DependencyInjection.Attributes;

namespace Bough.App.Presenters
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class RemoteAutoFetchSettingsPresenter
    {
        private readonly GitSettingsService _settings;
        private GitSettingsViewModel _screen;
        private Action _settingsChanged;
        private Task _saveTask = Task.CompletedTask;
        private int _viewVersion;
        private bool _saving;

        public RemoteAutoFetchSettingsPresenter(GitSettingsService settings)
        {
            _settings = settings;
        }

        public void Attach(GitSettingsViewModel screen)
        {
            if (ReferenceEquals(_screen, screen) == true)
            {
                return;
            }
            if (_screen != null)
            {
                Detach(_screen);
            }

            _screen = screen;
            int version = ++_viewVersion;
            _settingsChanged = () => Dispatcher.UIThread.Post(() =>
            {
                if (version != _viewVersion)
                {
                    return;
                }
                if (ReferenceEquals(_screen, screen) == false)
                {
                    return;
                }
                screen.RefreshAutoFetchSettings();
            });
            _settings.AutoFetchSettingsChanged += _settingsChanged;
            screen.SetAutoFetchSettingsSaving(_saving);
            screen.RefreshAutoFetchSettings();
        }

        public void Detach(GitSettingsViewModel screen)
        {
            if (ReferenceEquals(_screen, screen) == false)
            {
                return;
            }
            _settings.AutoFetchSettingsChanged -= _settingsChanged;
            _settingsChanged = null;
            _screen = null;
            _viewVersion++;
        }

        public Task SelectAsync(GitSettingsViewModel screen, bool enabled, int intervalMinutes)
        {
            if (ReferenceEquals(_screen, screen) == false)
            {
                return Task.CompletedTask;
            }
            if (_saving == true)
            {
                return Task.CompletedTask;
            }

            _saveTask = SaveAsync(screen, enabled, intervalMinutes);
            return _saveTask;
        }

        public Task WaitForSaveAsync()
        {
            return _saveTask;
        }

        private async Task SaveAsync(GitSettingsViewModel screen, bool enabled, int intervalMinutes)
        {
            int version = _viewVersion;
            _saving = true;
            screen.SetAutoFetchSettingsSaving(true);
            GitSettingsDisplayResult result;
            try
            {
                await Task.Run(() => _settings.SaveAutoFetchSettings(enabled, intervalMinutes));
                result = new GitSettingsDisplayResult(GitSettingsDisplayTarget.Status, "AutoFetchSettingsSaved", Array.Empty<object>(), null, null);
            }
            catch (Exception exception)
            {
                result = new GitSettingsDisplayResult(GitSettingsDisplayTarget.Status, string.Empty, Array.Empty<object>(), exception, null);
            }
            finally
            {
                _saving = false;
                screen.SetAutoFetchSettingsSaving(false);
                if (_screen != null)
                {
                    _screen.SetAutoFetchSettingsSaving(false);
                    _screen.RefreshAutoFetchSettings();
                }
            }

            if (version != _viewVersion)
            {
                return;
            }
            if (ReferenceEquals(_screen, screen) == false)
            {
                return;
            }
            screen.ApplyAutoFetchSettingsResult(result);
        }
    }
}
