using Bough.App.Internals;

namespace Bough.App.Localization
{
    public class StringLanguageSelection
    {
        public StringLanguageSelection(StringLanguage language)
        {
            Language = language;
        }

        public StringLanguage Language { get; }
    }
}
