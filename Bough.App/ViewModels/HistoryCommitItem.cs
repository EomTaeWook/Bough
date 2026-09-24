using System;
using System.Collections.Generic;
using System.Linq;
using Bough.App.Localization;
using Bough.Core.Git;

namespace Bough.App.ViewModels
{
    public class HistoryCommitItem : ViewModelBase
    {
        private double _graphWidth;
        private bool _externalPhotosEnabled;
        private string _gitHubRemoteUrl = string.Empty;
        private IReadOnlyList<string> _referenceNames;
        private IReadOnlyList<HistoryReferenceItem> _references;
        private IReadOnlyList<HistoryReferenceItem> _inlineReferences;
        private readonly StringHelper _stringHelper;

        public HistoryCommitItem(GitHistoryCommit commit, HistoryGraphRow graph, double graphWidth, StringHelper stringHelper)
        {
            Commit = commit;
            Graph = graph;
            _graphWidth = graphWidth;
            _stringHelper = stringHelper;
            SetReferences(commit.References);
        }

        private void SetReferences(IReadOnlyList<string> names)
        {
            _referenceNames = names;
            _references = names.Select(name => new HistoryReferenceItem(name, _stringHelper)).ToList().AsReadOnly();
            List<HistoryReferenceItem> inline = _references.Where(reference => reference.IsSymbolicRemote == false)
                .OrderBy(reference => reference.InlinePriority).Take(2).ToList();
            if (inline.Count == 0 && _references.Count > 0)
            {
                inline.Add(_references[0]);
            }
            _inlineReferences = inline.AsReadOnly();
            OnPropertyChanged(nameof(References));
            OnPropertyChanged(nameof(InlineReferences));
            OnPropertyChanged(nameof(HasMoreReferences));
            OnPropertyChanged(nameof(MoreReferencesText));
        }

        public void AddTagReference(string tagName)
        {
            string reference = $"tag: refs/tags/{tagName}";
            if (_referenceNames.Contains(reference, StringComparer.Ordinal))
            {
                return;
            }
            List<string> names = _referenceNames.ToList();
            names.Add(reference);
            SetReferences(names.AsReadOnly());
        }

        public GitHistoryCommit Commit { get; }
        public HistoryGraphRow Graph { get; }
        public double GraphWidth { get { return _graphWidth; } set { SetProperty(ref _graphWidth, value); } }
        public bool ExternalPhotosEnabled { get { return _externalPhotosEnabled; } set { SetProperty(ref _externalPhotosEnabled, value); } }
        public string GitHubRemoteUrl { get { return _gitHubRemoteUrl; } set { SetProperty(ref _gitHubRemoteUrl, value); } }
        public IReadOnlyList<HistoryReferenceItem> References { get { return _references; } }
        public IReadOnlyList<HistoryReferenceItem> InlineReferences { get { return _inlineReferences; } }
        public bool HasMoreReferences { get { return References.Count > InlineReferences.Count; } }
        public string MoreReferencesText { get { return $"+{References.Count - InlineReferences.Count}"; } }
        public string Hash { get { return Commit.Hash; } }
        public string ShortHash { get { return Commit.Hash.Substring(0, 8); } }
        public string MessageFirstLine { get { return Commit.Subject; } }
        public string Author { get { return Commit.Author; } }
        public string AuthorEmail { get { return Commit.AuthorEmail; } }
        public string AuthorDisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Author) == false)
                {
                    return Author;
                }
                return _stringHelper.GetString("HistoryUnknownAuthor");
            }
        }
        public string DateText { get { return Commit.AuthoredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); } }
        public string DetailDateText { get { return Commit.AuthoredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz"); } }
        public string ToolTipText { get { return $"{MessageFirstLine}\n{AuthorDisplayName} · {DetailDateText}\n{Hash}"; } }
    }
}
