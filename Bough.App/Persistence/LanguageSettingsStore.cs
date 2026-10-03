using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Bough.App.Internals;

namespace Bough.App.Persistence
{
    public class LanguageSettingsStore
    {
        private readonly string _settingsPath;

        public LanguageSettingsStore()
        {
            _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bough", "language.json");
            SelectedLanguage = LoadLanguage();
        }

        public StringLanguage SelectedLanguage { get; private set; }

        public async Task SaveAsync(StringLanguage language)
        {
            if (Enum.IsDefined(language) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(language));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
            string value = JsonSerializer.Serialize(language.ToString());
            string temporaryPath = _settingsPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, value);
            File.Move(temporaryPath, _settingsPath, true);
            SelectedLanguage = language;
        }

        private StringLanguage LoadLanguage()
        {
            if (File.Exists(_settingsPath) == false)
            {
                return StringLanguage.Korean;
            }
            try
            {
                string value = JsonSerializer.Deserialize<string>(File.ReadAllText(_settingsPath));
                if (Enum.TryParse(value, true, out StringLanguage language) == false)
                {
                    return StringLanguage.Korean;
                }
                if (Enum.IsDefined(language) == false)
                {
                    return StringLanguage.Korean;
                }
                return language;
            }
            catch (JsonException)
            {
                return StringLanguage.Korean;
            }
            catch (IOException)
            {
                return StringLanguage.Korean;
            }
            catch (UnauthorizedAccessException)
            {
                return StringLanguage.Korean;
            }
        }
    }
}
