using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using Bough.Core.Git;
using Bough.App.Infrastructure.Updates;
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
            if (exception is ApplicationUpdateException updateException)
            {
                if (string.IsNullOrWhiteSpace(updateException.ErrorCode) == true)
                {
                    return FormatErrorCode("AppUpdateCorePrepareFailed", Array.Empty<object>());
                }
                return FormatErrorCode(updateException.ErrorCode, updateException.Arguments);
            }
            if (exception is UnauthorizedAccessException || exception is SecurityException)
            {
                return _stringHelper.GetString("GitFileAccessDenied");
            }
            if (exception is IOException)
            {
                return _stringHelper.GetString("GitFileOperationFailed");
            }
            if (exception is OperationCanceledException)
            {
                return _stringHelper.GetString("GitOperationCanceled");
            }
            if (exception is not GitException gitException)
            {
                return exception.Message;
            }
            if (string.IsNullOrWhiteSpace(gitException.ErrorCode))
            {
                return gitException.Message;
            }

            return FormatErrorCode(gitException.ErrorCode, gitException.Arguments);
        }

        private string FormatErrorCode(string errorCode, IReadOnlyList<object> values)
        {
            string fallback = GetEnglishFallback(errorCode);
            string template = _stringHelper.GetString(errorCode);
            if (string.IsNullOrWhiteSpace(template))
            {
                template = fallback;
            }

            object[] arguments = values.ToArray();
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

            if (errorCode.StartsWith("AppUpdate", StringComparison.Ordinal) == true)
            {
                return errorCode;
            }
            return "Git operation failed.";
        }
    }
}
