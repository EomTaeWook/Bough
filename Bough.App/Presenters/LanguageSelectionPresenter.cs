using System.Threading;
using System.Threading.Tasks;
using Bough.App.Internals;
using Bough.App.Localization;
using Bough.App.Persistence;
using Dignus.DependencyInjection.Attributes;

namespace Bough.App.Presenters
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class LanguageSelectionPresenter
    {
        private readonly LanguageSettingsStore _settings;
        private readonly StringHelper _strings;
        private readonly SemaphoreSlim _changeGate = new(1, 1);

        public LanguageSelectionPresenter(LanguageSettingsStore settings, StringHelper strings)
        {
            _settings = settings;
            _strings = strings;
        }

        public async Task SelectAsync(StringLanguage language)
        {
            await _changeGate.WaitAsync();
            try
            {
                if (_strings.Language == language)
                {
                    return;
                }
                await _settings.SaveAsync(language);
                _strings.Language = language;
            }
            finally
            {
                _changeGate.Release();
            }
        }
    }
}
