using System;
using System.Globalization;
using System.Linq;
using Bough.Core.Git;
using DataContainer.Generated;
using Dignus.DependencyInjection.Attributes;

namespace Bough.App.Localization
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class GitErrorLocalizer
    {
        private readonly StringHelper _stringHelper;

        public GitErrorLocalizer(StringHelper stringHelper)
        {
            _stringHelper = stringHelper;
        }

        public string GetDisplayMessage(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (exception is not GitException gitException)
            {
                return exception.Message;
            }
            if (string.IsNullOrWhiteSpace(gitException.ErrorCode))
            {
                return gitException.Message;
            }

            string fallback = GetEnglishFallback(gitException.ErrorCode);
            string template = _stringHelper.GetString(gitException.ErrorCode);
            if (string.IsNullOrWhiteSpace(template))
            {
                template = fallback;
            }

            object[] arguments = gitException.Arguments.ToArray();
            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, arguments);
            }
            catch (FormatException)
            {
                return string.Format(CultureInfo.InvariantCulture, fallback, arguments);
            }
        }

        private static string GetEnglishFallback(string errorCode)
        {
            StringTemplate template = TemplateContainer<StringTemplate>.Find(errorCode);
            if (template != null)
            {
                if (template.Invalid() == false)
                {
                    if (string.IsNullOrWhiteSpace(template.Eng) == false)
                    {
                        return template.Eng;
                    }
                }
            }

            if (errorCode == GitException.ProcessStartFailedCode)
            {
                return "Could not start Git: {0}";
            }
            if (errorCode == GitException.ExitWithoutErrorMessageCode)
            {
                return "Git exited without an error message (code {0}).";
            }

            return "Git operation failed.";
        }
    }
}
