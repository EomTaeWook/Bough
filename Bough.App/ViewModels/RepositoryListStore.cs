using System;
using System.IO;
using System.Text.Json;
using Bough.Core.Git;
using Bough.App.ViewModels.Models;

namespace Bough.App.ViewModels
{
    public class RepositoryListStore
    {
        private readonly string _filePath;

        public RepositoryListStore()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _filePath = Path.Combine(appData, "Bough", "repositories.json");
        }

        public RepositoryListState Load()
        {
            if (File.Exists(_filePath) == false)
            {
                return new RepositoryListState();
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<RepositoryListState>(json) ?? new RepositoryListState();
            }
            catch (JsonException)
            {
                return new RepositoryListState();
            }
            catch (IOException)
            {
                return new RepositoryListState();
            }
            catch (UnauthorizedAccessException)
            {
                return new RepositoryListState();
            }
        }

        public void Save(RepositoryListState state)
        {
            string directoryPath = Path.GetDirectoryName(_filePath);
            if (directoryPath == null)
            {
                throw new GitException("RepositoryListPathInvalid", null, _filePath);
            }

            Directory.CreateDirectory(directoryPath);
            string json = JsonSerializer.Serialize(state);
            File.WriteAllText(_filePath, json);
        }
    }
}
