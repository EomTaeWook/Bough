using System.Collections.ObjectModel;
using System.IO;

namespace Bough.App.ViewModels
{
    public class RepositoryGroup
    {
        public RepositoryGroup(string parentPath)
        {
            ParentPath = parentPath;
            Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(parentPath));
            if (string.IsNullOrEmpty(Name) == true)
            {
                Name = parentPath;
            }

            Repositories = [];
        }

        public string ParentPath { get; }

        public string Name { get; }

        public ObservableCollection<RepositoryItem> Repositories { get; }
    }
}
