using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Bough.App.Localization;
using Bough.App.Controls;

namespace Bough.App.Views
{
    public class AuthorPhotoSourceLabelConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Count < 2)
            {
                return string.Empty;
            }
            if (values[1] is not StringHelper strings)
            {
                return string.Empty;
            }
            if (values[0] is not AuthorPhotoSource source)
            {
                return strings.GetString("AuthorPhotoLocalIcon");
            }

            switch (source)
            {
                case AuthorPhotoSource.GitHubCommit:
                    return strings.GetString("AuthorPhotoGitHubCommit");
                case AuthorPhotoSource.GitHubLinkedAccount:
                    return strings.GetString("AuthorPhotoGitHubLinkedAccount");
                case AuthorPhotoSource.Gravatar:
                    return strings.GetString("AuthorPhotoGravatar");
                default:
                    return strings.GetString("AuthorPhotoLocalIcon");
            }
        }
    }
}
