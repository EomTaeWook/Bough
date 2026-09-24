using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Bough.App.Controls;

namespace Bough.App.Views
{
    public class AuthorPhotoSourceLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not AuthorPhotoSource source)
            {
                return "로컬 아이콘";
            }

            switch (source)
            {
                case AuthorPhotoSource.GitHubCommit:
                    return "GitHub 프로필 (커밋 작성자 확인)";
                case AuthorPhotoSource.GitHubLinkedAccount:
                    return "GitHub 프로필 (사용자가 지정한 계정)";
                case AuthorPhotoSource.Gravatar:
                    return "Gravatar";
                default:
                    return "로컬 아이콘";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
