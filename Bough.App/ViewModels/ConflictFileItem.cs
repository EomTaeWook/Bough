using System.IO;
using Bough.App.Localization;

namespace Bough.App.ViewModels
{
    public class ConflictFileItem
    {
        private readonly StringHelper _stringHelper;

        public ConflictFileItem(string relativePath, StringHelper stringHelper)
        {
            RelativePath = relativePath;
            _stringHelper = stringHelper;
        }

        public string RelativePath { get; }

        public string FileName
        {
            get
            {
                return Path.GetFileName(RelativePath);
            }
        }

        public string DirectoryName
        {
            get
            {
                string directory = Path.GetDirectoryName(RelativePath);
                if (string.IsNullOrWhiteSpace(directory) == true)
                {
                    return _stringHelper.GetString("RepositoryRoot");
                }

                return directory;
            }
        }
    }
}
