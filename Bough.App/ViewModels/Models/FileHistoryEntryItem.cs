using System.Globalization;
using Bough.Core.Git.Models;

namespace Bough.App.ViewModels.Models
{
    public class FileHistoryEntryItem : ViewModelBase
    {
        private string _statusText;

        public FileHistoryEntryItem(GitFileHistoryEntry entry)
        {
            Entry = entry;
            _statusText = entry.Status;
        }

        public GitFileHistoryEntry Entry { get; }
        public string Author { get { return Entry.Author; } }
        public string Title { get { return Entry.Title; } }
        public string Date { get { return Entry.AuthoredAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture); } }
        public string ShortHash { get { return Entry.CommitHash.Substring(0, 8); } }
        public string Path { get { return Entry.Path; } }
        public string Status { get { return Entry.Status; } }
        public string StatusText { get { return _statusText; } }

        public void SetStatusText(string text)
        {
            SetProperty(ref _statusText, text, nameof(StatusText));
        }
        public string PathDescription
        {
            get
            {
                if (Entry.PreviousPath.Length > 0)
                {
                    return Entry.PreviousPath + " → " + Entry.Path;
                }
                return Entry.Path;
            }
        }
    }
}
