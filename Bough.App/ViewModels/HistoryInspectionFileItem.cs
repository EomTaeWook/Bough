using Bough.App.Localization;
using Bough.Core.Git;
using System.Collections.ObjectModel;
using Bough.App.ViewModels.Models;
using Bough.Core.Git.Models;

namespace Bough.App.ViewModels
{
    public class HistoryInspectionFileItem : ViewModelBase
    {
        private bool _isExpanded;
        private bool _isLoaded;
        private bool _isLoading;
        private string _diffText;
        private string _diffReason;
        private readonly StringHelper _stringHelper;

        public HistoryInspectionFileItem(GitCommitChangedFile file, StringHelper stringHelper)
        {
            File = file;
            _stringHelper = stringHelper;
            _diffText = string.Empty;
            _diffReason = string.Empty;
            DiffLines = [];
        }

        public GitCommitChangedFile File { get; }
        public ObservableCollection<HistoryDiffLineItem> DiffLines { get; }
        public string Path { get { return File.Path; } }
        public string PreviousPath { get { return File.PreviousPath; } }
        public string Status { get { return File.Status; } }
        public string StatusText
        {
            get
            {
                switch (File.StatusCode)
                {
                    case 'A': return _stringHelper.GetString("HistoryStatusAdded");
                    case 'M': return _stringHelper.GetString("HistoryStatusModified");
                    case 'D': return _stringHelper.GetString("HistoryStatusDeleted");
                    case 'R': return _stringHelper.GetString("HistoryStatusRenamed");
                    case 'C': return _stringHelper.GetString("HistoryStatusCopied");
                    case 'T': return _stringHelper.GetString("HistoryStatusTypeChanged");
                    default: return File.Status;
                }
            }
        }
        public bool IsAdded { get { return File.StatusCode == 'A'; } }
        public bool IsDeleted { get { return File.StatusCode == 'D'; } }
        public bool IsRenamed { get { return File.StatusCode == 'R'; } }
        public bool IsCopied { get { return File.StatusCode == 'C'; } }
        public string PathDescription
        {
            get
            {
                if (PreviousPath.Length > 0)
                {
                    return $"{PreviousPath} → {Path}";
                }
                return Path;
            }
        }
        public bool IsExpanded { get { return _isExpanded; } set { SetProperty(ref _isExpanded, value); } }
        public bool IsLoaded { get { return _isLoaded; } set { SetProperty(ref _isLoaded, value); } }
        public bool IsLoading { get { return _isLoading; } set { SetProperty(ref _isLoading, value); } }
        public string DiffText { get { return _diffText; } set { SetProperty(ref _diffText, value); } }
        public string DiffReason { get { return _diffReason; } set { SetProperty(ref _diffReason, value); } }
        public bool HasRawDiff { get { return DiffLines.Count == 0 && DiffText.Length > 0; } }

        public void OnDiffLinesChanged()
        {
            OnPropertyChanged(nameof(HasRawDiff));
        }
    }
}
