using System;
using System.IO;
using System.Text.Json;

namespace Bough.App.ViewModels
{
    internal class AuthorPhotoSettings
    {
        private readonly string _filePath;

        public AuthorPhotoSettings()
        {
            _filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bough", "author-photos.json");
        }

        public bool LoadEnabled()
        {
            try
            {
                if (File.Exists(_filePath) == false)
                {
                    return true;
                }

                return JsonSerializer.Deserialize<bool>(File.ReadAllText(_filePath));
            }
            catch (Exception exception) when (exception is JsonException || exception is IOException || exception is UnauthorizedAccessException)
            {
                return true;
            }
        }

        public void SaveEnabled(bool enabled)
        {
            try
            {
                string directory = Path.GetDirectoryName(_filePath);
                if (directory == null)
                {
                    return;
                }

                Directory.CreateDirectory(directory);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(enabled));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // A read-only profile keeps the current in-memory setting.
            }
        }
    }
}
