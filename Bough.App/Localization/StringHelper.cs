using System;
using System.Globalization;
using DataContainer.Generated;
using Dignus.DependencyInjection.Attributes;
using Dignus.Log;
using Bough.App.Internals;

namespace Bough.App.Localization
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class StringHelper
    {
        public StringHelper(StringLanguageSelection languageSelection)
        {
            Language = languageSelection.Language;
        }

        public StringLanguage Language { get; set; }

        public string GetString(int id)
        {
            StringTemplate template = TemplateContainer<StringTemplate>.Find(id);
            if (template.Invalid() == true)
            {
                LogHelper.Error($"Invalid string template. id:{id}.");
                return null;
            }

            return GetString(template);
        }

        public string GetString(string name)
        {
            StringTemplate template = TemplateContainer<StringTemplate>.Find(name);
            if (template.Invalid() == true)
            {
                LogHelper.Error($"Invalid string template. name:{name}.");
                return null;
            }

            return GetString(template);
        }

        public string Format(string name, params object[] arguments)
        {
            string template = GetString(name);
            if (template == null)
            {
                return null;
            }

            return string.Format(CultureInfo.CurrentCulture, template, arguments);
        }

        public string GetString(StringTemplate template)
        {
            if (template == null)
            {
                LogHelper.Fatal("String template is null.");
                return null;
            }

            if (template.Invalid() == true)
            {
                LogHelper.Error($"Invalid string template. id:{template.Id}.");
                return null;
            }

            if (Language == StringLanguage.Korean)
            {
                return template.Kor;
            }

            if (Language == StringLanguage.English)
            {
                return template.Eng;
            }

            LogHelper.Error($"Invalid string language. language:{Language}.");
            return null;
        }
    }
}
