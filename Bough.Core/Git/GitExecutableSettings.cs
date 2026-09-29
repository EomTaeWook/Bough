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
        }

        public string ConfiguredPath { get { return _configuredPath; } }
        public GitPullStrategy DefaultPullStrategy { get { return _defaultPullStrategy; } }
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

            SaveState(directory, selectedPath, _defaultPullStrategy);
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

            SaveState(directory, _configuredPath, strategy);
            _defaultPullStrategy = strategy;
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

        private void SaveState(string directory, string path, GitPullStrategy strategy)
        {
            Directory.CreateDirectory(directory);
            GitExecutableState state = new() { Path = path, DefaultPullStrategy = strategy };
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
        }
    }
}
