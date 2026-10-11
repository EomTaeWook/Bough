using System;
using System.IO;
using System.Text.Json;
using Bough.Core.Internals;

namespace Bough.Core.Git
{
    public class GitExecutableSettings
    {
        private readonly string _filePath;
        private string _configuredPath;
        private GitPullStrategy _defaultPullStrategy;
        private bool _autoFetchEnabled;
        private int _autoFetchIntervalMinutes;

        public static GitExecutableSettings Default { get; } = new GitExecutableSettings();

        public GitExecutableSettings()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bough", "git-settings.json"))
        {
        }

        public GitExecutableSettings(string filePath)
        {
            _filePath = filePath;
            GitExecutableState state = LoadState();
            _configuredPath = state.Path ?? string.Empty;
            _defaultPullStrategy = NormalizePullStrategy(state.DefaultPullStrategy);
            _autoFetchEnabled = state.AutoFetchEnabled;
            _autoFetchIntervalMinutes = NormalizeAutoFetchInterval(state.AutoFetchIntervalMinutes);
        }

        public string ConfiguredPath { get { return _configuredPath; } }
        public GitPullStrategy DefaultPullStrategy { get { return _defaultPullStrategy; } }
        public bool AutoFetchEnabled { get { return _autoFetchEnabled; } }
        public int AutoFetchIntervalMinutes { get { return _autoFetchIntervalMinutes; } }
        public string ExecutablePath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_configuredPath) == true)
                {
                    return "git";
                }

                return _configuredPath;
            }
        }

        public void SavePath(string path)
        {
            string selectedPath = path?.Trim() ?? string.Empty;
            if (selectedPath.Length > 0 && Path.IsPathFullyQualified(selectedPath) == false)
            {
                throw new GitException("GitExecutableAbsolutePathRequired", null, selectedPath);
            }

            string directory = Path.GetDirectoryName(_filePath);
            if (directory == null)
            {
                throw new GitException("GitExecutableSettingsPathInvalid", null, _filePath);
            }

            SaveState(directory, selectedPath, _defaultPullStrategy, _autoFetchEnabled, _autoFetchIntervalMinutes);
            _configuredPath = selectedPath;
        }

        public void SaveDefaultPullStrategy(GitPullStrategy strategy)
        {
            if (Enum.IsDefined(strategy) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null);
            }

            string directory = Path.GetDirectoryName(_filePath);
            if (directory == null)
            {
                throw new GitException("GitExecutableSettingsPathInvalid", null, _filePath);
            }

            SaveState(directory, _configuredPath, strategy, _autoFetchEnabled, _autoFetchIntervalMinutes);
            _defaultPullStrategy = strategy;
        }

        public void SaveAutoFetchSettings(bool enabled, int intervalMinutes)
        {
            if (NormalizeAutoFetchInterval(intervalMinutes) != intervalMinutes)
            {
                throw new ArgumentOutOfRangeException(nameof(intervalMinutes), intervalMinutes, null);
            }
            string directory = Path.GetDirectoryName(_filePath);
            if (directory == null)
            {
                throw new GitException("GitExecutableSettingsPathInvalid", null, _filePath);
            }
            SaveState(directory, _configuredPath, _defaultPullStrategy, enabled, intervalMinutes);
            _autoFetchEnabled = enabled;
            _autoFetchIntervalMinutes = intervalMinutes;
        }

        private static int NormalizeAutoFetchInterval(int intervalMinutes)
        {
            if (intervalMinutes == 1 || intervalMinutes == 5 || intervalMinutes == 10 || intervalMinutes == 30)
            {
                return intervalMinutes;
            }
            return 5;
        }

        private GitExecutableState LoadState()
        {
            if (File.Exists(_filePath) == false)
            {
                return new GitExecutableState();
            }

            try
            {
                GitExecutableState state = JsonSerializer.Deserialize<GitExecutableState>(File.ReadAllText(_filePath));
                if (state == null)
                {
                    return new GitExecutableState();
                }

                return state;
            }
            catch (JsonException)
            {
                return new GitExecutableState();
            }
            catch (IOException)
            {
                return new GitExecutableState();
            }
            catch (UnauthorizedAccessException)
            {
                return new GitExecutableState();
            }
        }

        private void SaveState(string directory, string path, GitPullStrategy strategy, bool autoFetchEnabled, int autoFetchIntervalMinutes)
        {
            Directory.CreateDirectory(directory);
            GitExecutableState state = new()
            {
                Path = path,
                DefaultPullStrategy = strategy,
                AutoFetchEnabled = autoFetchEnabled,
                AutoFetchIntervalMinutes = autoFetchIntervalMinutes
            };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(state));
        }

        private static GitPullStrategy NormalizePullStrategy(GitPullStrategy strategy)
        {
            if (Enum.IsDefined(strategy) == false)
            {
                return GitPullStrategy.FastForwardOnly;
            }

            return strategy;
        }

        private class GitExecutableState
        {
            public string Path { get; set; }
            public GitPullStrategy DefaultPullStrategy { get; set; }
            public bool AutoFetchEnabled { get; set; }
            public int AutoFetchIntervalMinutes { get; set; }
        }
    }
}
