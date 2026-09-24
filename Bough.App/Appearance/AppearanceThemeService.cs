using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;

namespace Bough.App.Appearance
{
    public enum AppearanceThemeMode
    {
        Light,
        Dark,
        System
    }

    public class AppearanceThemeService
    {
        private readonly Application _application;
        private readonly string _settingsPath;
        private readonly SemaphoreSlim _saveGate;
        private int _changeVersion;

        public AppearanceThemeService(Application application)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            _application = application;
            _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bough", "appearance.json");
            _saveGate = new SemaphoreSlim(1, 1);
            SelectedMode = LoadMode();
            ApplyTheme(SelectedMode);
        }

        public event Action<AppearanceThemeMode> ThemeChanged;

        public AppearanceThemeMode SelectedMode { get; private set; }

        public async Task SetThemeAsync(AppearanceThemeMode mode)
        {
            switch (mode)
            {
                case AppearanceThemeMode.Light:
                case AppearanceThemeMode.Dark:
                case AppearanceThemeMode.System:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }

            if (SelectedMode == mode)
            {
                return;
            }

            SelectedMode = mode;
            ApplyTheme(mode);
            ThemeChanged?.Invoke(mode);
            int version = ++_changeVersion;
            await _saveGate.WaitAsync();
            try
            {
                if (version != _changeVersion)
                {
                    return;
                }

                string directory = Path.GetDirectoryName(_settingsPath);
                Directory.CreateDirectory(directory);
                string value = JsonSerializer.Serialize(mode.ToString());
                await File.WriteAllTextAsync(_settingsPath, value);
            }
            finally
            {
                _saveGate.Release();
            }
        }

        private AppearanceThemeMode LoadMode()
        {
            if (File.Exists(_settingsPath) == false)
            {
                return AppearanceThemeMode.Light;
            }

            try
            {
                string value = JsonSerializer.Deserialize<string>(File.ReadAllText(_settingsPath));
                if (Enum.TryParse(value, true, out AppearanceThemeMode mode) == false)
                {
                    return AppearanceThemeMode.Light;
                }

                switch (mode)
                {
                    case AppearanceThemeMode.Light:
                    case AppearanceThemeMode.Dark:
                    case AppearanceThemeMode.System:
                        return mode;
                    default:
                        return AppearanceThemeMode.Light;
                }
            }
            catch (JsonException)
            {
                return AppearanceThemeMode.Light;
            }
            catch (IOException)
            {
                return AppearanceThemeMode.Light;
            }
            catch (UnauthorizedAccessException)
            {
                return AppearanceThemeMode.Light;
            }
        }

        private void ApplyTheme(AppearanceThemeMode mode)
        {
            switch (mode)
            {
                case AppearanceThemeMode.Light:
                    _application.RequestedThemeVariant = ThemeVariant.Light;
                    break;
                case AppearanceThemeMode.Dark:
                    _application.RequestedThemeVariant = ThemeVariant.Dark;
                    break;
                case AppearanceThemeMode.System:
                    _application.RequestedThemeVariant = ThemeVariant.Default;
                    break;
            }
        }
    }
}
