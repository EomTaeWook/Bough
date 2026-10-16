using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Bough.App.Internals;
using Bough.App.Localization;
using Bough.Core.Git.Models;

namespace Bough.App.Converters
{
    public class WorktreeStatusTextConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Count < 3)
            {
                return AvaloniaProperty.UnsetValue;
            }
            if (values[0] is not GitWorktreeFile file)
            {
                return AvaloniaProperty.UnsetValue;
            }
            if (values[1] is not StringHelper strings)
            {
                return AvaloniaProperty.UnsetValue;
            }
            if (values[2] is not StringLanguage)
            {
                return AvaloniaProperty.UnsetValue;
            }
            string status = strings.GetString(file.StatusCode);
            if (file.StatusCode == "WorktreeStatusSubmodule")
            {
                List<string> parts = [status];
                if (file.SubmoduleStatus[1] == 'C')
                {
                    parts.Add(strings.GetString("ReferenceSubmoduleDifferentCommit"));
                }
                if (file.SubmoduleStatus[2] == 'M')
                {
                    parts.Add(strings.GetString("WorktreeStatusModified"));
                }
                if (file.SubmoduleStatus[3] == 'U')
                {
                    parts.Add(strings.GetString("WorktreeStatusUntracked"));
                }
                status = string.Join(" · ", parts);
            }
            if (file.IsPartiallyStaged)
            {
                status = strings.Format("WorktreeStatusPartiallyStaged", status);
            }
            return status;
        }
    }
}
