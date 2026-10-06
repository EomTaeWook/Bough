using System;
using System.Collections.ObjectModel;
using Bough.App.ViewModels.Models;
using Bough.Core.Git.Models;

namespace Bough.App.ViewModels
{
    public class FileHistoryViewModel : ViewModelBase
    {
        private FileHistoryEntryItem _selectedEntry;
        private bool _isLoadingList = true;
        private bool _isLoadingDiff;
        private bool _isListCanceled;
        private bool _isDiffCanceled;
        private Exception _listError;
        private Exception _diffError;
        private GitCommitFileDiff _diff;

        public FileHistoryViewModel(GitRepository repository, string revisionHash, string path)
        {
            Repository = repository;
            RevisionHash = revisionHash;
            FilePath = path;
        }

        public GitRepository Repository { get; }
        public string RevisionHash { get; }
        public string FilePath { get; }
        public ObservableCollection<FileHistoryEntryItem> Entries { get; } = [];
        public ObservableCollection<HistoryDiffLineItem> DiffLines { get; } = [];
        public event Action<FileHistoryEntryItem> SelectionChanged;

        public FileHistoryEntryItem SelectedEntry
        {
            get { return _selectedEntry; }
            set
            {
                if (SetProperty(ref _selectedEntry, value))
                {
                    SelectionChanged?.Invoke(value);
                }
            }
        }

        public bool IsLoadingList { get { return _isLoadingList; } internal set { SetProperty(ref _isLoadingList, value); } }
        public bool IsLoadingDiff { get { return _isLoadingDiff; } internal set { SetProperty(ref _isLoadingDiff, value); } }
        public bool IsListCanceled { get { return _isListCanceled; } internal set { SetProperty(ref _isListCanceled, value); } }
        public bool IsDiffCanceled { get { return _isDiffCanceled; } internal set { SetProperty(ref _isDiffCanceled, value); } }
        public Exception ListError { get { return _listError; } internal set { SetProperty(ref _listError, value); } }
        public Exception DiffError { get { return _diffError; } internal set { SetProperty(ref _diffError, value); } }
        public GitCommitFileDiff Diff { get { return _diff; } internal set { SetProperty(ref _diff, value); } }
    }
}
