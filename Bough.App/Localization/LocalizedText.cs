using System;

namespace Bough.App.Localization
{
    public class LocalizedText
    {
        private readonly string _key;
        private readonly object[] _arguments;
        private readonly Exception _error;

        public LocalizedText(string key, params object[] arguments)
        {
            _key = key;
            _arguments = arguments;
        }

        public LocalizedText(Exception error)
        {
            _error = error;
        }

        public string GetText(StringHelper strings)
        {
            if (_error != null)
            {
                return new GitErrorLocalizer(strings).GetDisplayMessage(_error);
            }
            if (string.IsNullOrEmpty(_key))
            {
                return string.Empty;
            }
            object[] arguments = new object[_arguments.Length];
            for (int index = 0; index < _arguments.Length; index++)
            {
                if (_arguments[index] is LocalizedText text)
                {
                    arguments[index] = text.GetText(strings);
                }
                else
                {
                    arguments[index] = _arguments[index];
                }
            }
            return strings.Format(_key, arguments) ?? _key;
        }
    }
}
