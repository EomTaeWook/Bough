using System.Collections.ObjectModel;
using Bough.App.Localization;
using Bough.Core.Git;

namespace Bough.App.ViewModels
{
    public class HistoryTreeItem : ViewModelBase
    {
        private bool _isExpanded;
        private bool _isLoaded;
        private bool _isLoading;
        private readonly StringHelper _stringHelper;

        public HistoryTreeItem(GitCommitTreeEntry entry, StringHelper stringHelper)
        {
            Entry = entry;
            _stringHelper = stringHelper;
            Children = [];
            if (entry.IsDirectory == true)
            {
                Children.Add(new HistoryTreeItem(new GitCommitTreeEntry($"{entry.Path}/[loading]", "100644", "placeholder", string.Empty), stringHelper));
            }
        }

        public GitCommitTreeEntry Entry { get; }
        public ObservableCollection<HistoryTreeItem> Children { get; }
        public string Path { get { return Entry.Path; } }
        public string ToolTipPath { get { if (IsPlaceholder) return Name; return Path; } }
        public string Name
        {
            get
            {
                if (IsPlaceholder) return _stringHelper.GetString("HistoryTreeLoading");
                return System.IO.Path.GetFileName(Entry.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            }
        }
        public string Kind
        {
            get
            {
                if (IsPlaceholder) return string.Empty;
                if (Entry.IsDirectory == true) return _stringHelper.GetString("HistoryTreeFolder");
                if (Entry.IsGitlink == true) return _stringHelper.GetString("HistoryTreeSubmodule");
                return _stringHelper.GetString("HistoryTreeFile");
            }
        }
        public bool IsDirectory { get { return Entry.IsDirectory; } }
        public bool IsPlaceholder { get { return Entry.ObjectType == "placeholder"; } }
        public bool IsFile { get { return Entry.IsDirectory == false; } }
        public bool IsExpanded { get { return _isExpanded; } set { SetProperty(ref _isExpanded, value); } }
        public bool IsLoaded { get { return _isLoaded; } set { SetProperty(ref _isLoaded, value); } }
        public bool IsLoading { get { return _isLoading; } set { SetProperty(ref _isLoading, value); } }
    }
}
