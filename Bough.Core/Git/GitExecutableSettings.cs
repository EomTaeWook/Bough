using System;
using System.IO;
using System.Text.Json;

namespace Bough.Core.Git
{
    public class GitExecutableSettings
    {
        private readonly string _filePath;
        private string _configuredPath;

        public static GitExecutableSettings Default { get; } = new GitExecutableSettings();

        public GitExecutableSettings()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bough", "git-settings.json"))
        {
        }

        public GitExecutableSettings(string filePath)
        {
            _filePath = filePath;
            _configuredPath = LoadPath();
        }

        public string ConfiguredPath { get { return _configuredPath; } }
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
                throw new ArgumentException("Git 실행 파일의 절대 경로를 입력하세요.", nameof(path));
            }

            string directory = Path.GetDirectoryName(_filePath);
            if (directory == null)
            {
                throw new InvalidOperationException($"Git 설정 파일 경로가 올바르지 않습니다: {_filePath}");
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(new GitExecutableState { Path = selectedPath }));
            _configuredPath = selectedPath;
        }

        private string LoadPath()
        {
            if (File.Exists(_filePath) == false)
            {
                return string.Empty;
            }

            try
            {
                GitExecutableState state = JsonSerializer.Deserialize<GitExecutableState>(File.ReadAllText(_filePath));
                return state?.Path ?? string.Empty;
            }
            catch (JsonException)
            {
                return string.Empty;
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        private class GitExecutableState
        {
            public string Path { get; set; }
        }
    }
}
