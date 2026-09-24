using System;
using Bough.App.Localization;

namespace Bough.App.ViewModels
{
    internal enum HistoryReferenceKind
    {
        CurrentBranch,
        LocalBranch,
        RemoteBranch,
        Tag,
        Other
    }

    public class HistoryReferenceItem
    {
        private readonly StringHelper _stringHelper;
        private readonly HistoryReferenceKind _kind;

        public HistoryReferenceItem(string name, StringHelper stringHelper)
        {
            _stringHelper = stringHelper;
            if (name.StartsWith("HEAD -> refs/heads/", StringComparison.Ordinal))
            {
                Name = name.Substring("HEAD -> refs/heads/".Length);
                _kind = HistoryReferenceKind.CurrentBranch;
                Symbol = "✓";
            }
            else if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                Name = name.Substring("refs/heads/".Length);
                _kind = HistoryReferenceKind.LocalBranch;
                Symbol = "●";
            }
            else if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                Name = name.Substring("refs/remotes/".Length).Replace(" -> refs/remotes/", " → ");
                _kind = HistoryReferenceKind.RemoteBranch;
                Symbol = "⇄";
                IsSymbolicRemote = name.Contains("/HEAD -> ", StringComparison.Ordinal);
            }
            else if (name.StartsWith("tag: refs/tags/", StringComparison.Ordinal))
            {
                Name = name.Substring("tag: refs/tags/".Length);
                _kind = HistoryReferenceKind.Tag;
                Symbol = "◆";
            }
            else if (name.StartsWith("HEAD -> ", StringComparison.Ordinal))
            {
                Name = name.Substring("HEAD -> ".Length);
                _kind = HistoryReferenceKind.CurrentBranch;
                Symbol = "✓";
            }
            else if (name.StartsWith("tag: ", StringComparison.Ordinal))
            {
                Name = name.Substring("tag: ".Length);
                _kind = HistoryReferenceKind.Tag;
                Symbol = "◆";
            }
            else
            {
                Name = name;
                _kind = HistoryReferenceKind.Other;
                Symbol = "●";
            }

        }

        public string Name { get; }
        public string Kind
        {
            get
            {
                switch (_kind)
                {
                    case HistoryReferenceKind.CurrentBranch: return _stringHelper.GetString("HistoryReferenceCurrentBranch");
                    case HistoryReferenceKind.LocalBranch: return _stringHelper.GetString("HistoryReferenceLocalBranch");
                    case HistoryReferenceKind.RemoteBranch: return _stringHelper.GetString("HistoryReferenceRemoteBranch");
                    case HistoryReferenceKind.Tag: return _stringHelper.GetString("HistoryReferenceTag");
                    default: return _stringHelper.GetString("HistoryReferenceGeneric");
                }
            }
        }
        public string Symbol { get; }
        public string ToolTipText { get { return $"{Kind}: {Name}"; } }
        public bool IsSymbolicRemote { get; }
        public int InlinePriority
        {
            get
            {
                if (_kind == HistoryReferenceKind.CurrentBranch) return 0;
                if (_kind == HistoryReferenceKind.LocalBranch) return 1;
                if (_kind == HistoryReferenceKind.RemoteBranch) return 2;
                if (_kind == HistoryReferenceKind.Tag) return 3;
                return 4;
            }
        }
        public bool IsCurrent { get { return _kind == HistoryReferenceKind.CurrentBranch; } }
        public bool IsLocal { get { return _kind == HistoryReferenceKind.LocalBranch; } }
        public bool IsRemote { get { return _kind == HistoryReferenceKind.RemoteBranch; } }
        public bool IsTag { get { return _kind == HistoryReferenceKind.Tag; } }
    }
}
