using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Bough.Core.Git.Models;

namespace Bough.App.Converters
{
    public class WorktreeStatusIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not GitWorktreeFile file)
            {
                return AvaloniaProperty.UnsetValue;
            }

            string statusCode = file.StatusCode;
            if (parameter is string requestedStatus)
            {
                if (requestedStatus == "ShowLabel")
                {
                    if (file.IsPartiallyStaged == true)
                    {
                        return true;
                    }
                    return statusCode != "WorktreeStatusModified" &&
                        statusCode != "WorktreeStatusAdded" && statusCode != "WorktreeStatusDeleted";
                }
                return string.Equals(statusCode, requestedStatus, StringComparison.Ordinal);
            }

            string resourceKey;
            switch (statusCode)
            {
                case "WorktreeStatusModified":
                    resourceKey = "BoughIconFileModified";
                    break;
                case "WorktreeStatusAdded":
                    resourceKey = "BoughIconFileAdded";
                    break;
                case "WorktreeStatusDeleted":
                    resourceKey = "BoughIconFileDeleted";
                    break;
                default:
                    resourceKey = "BoughIconChanges";
                    break;
            }

            Application application = Application.Current;
            if (application == null)
            {
                return AvaloniaProperty.UnsetValue;
            }
            if (application.Resources.TryGetResource(resourceKey, null, out object icon) == false)
            {
                return AvaloniaProperty.UnsetValue;
            }
            return icon;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }
}
