using System.IO;

namespace Bough.App.ViewModels
{
    public class RepositoryItem : ViewModelBase
    {
        private bool _isActive;

        public RepositoryItem(string rootPath)
        {
            RootPath = rootPath;
            Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(rootPath));
            if (string.IsNullOrEmpty(Name) == true)
            {
                Name = rootPath;
            }
        }

        public string RootPath { get; }

        public string Name { get; }

        public bool IsActive
        {
            get { return _isActive; }
            set { SetProperty(ref _isActive, value); }
        }
    }
}
