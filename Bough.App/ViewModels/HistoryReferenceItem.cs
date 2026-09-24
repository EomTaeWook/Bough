using System;

namespace Bough.App.ViewModels
{
    public class HistoryReferenceItem
    {
        public HistoryReferenceItem(string name)
        {
            if (name.StartsWith("HEAD -> refs/heads/", StringComparison.Ordinal))
            {
                Name = name.Substring("HEAD -> refs/heads/".Length);
                Kind = "Current branch";
                Symbol = "✓";
            }
            else if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                Name = name.Substring("refs/heads/".Length);
                Kind = "Local branch";
                Symbol = "●";
            }
            else if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                Name = name.Substring("refs/remotes/".Length).Replace(" -> refs/remotes/", " → ");
                Kind = "Remote branch";
                Symbol = "⇄";
                IsSymbolicRemote = name.Contains("/HEAD -> ", StringComparison.Ordinal);
            }
            else if (name.StartsWith("tag: refs/tags/", StringComparison.Ordinal))
            {
                Name = name.Substring("tag: refs/tags/".Length);
                Kind = "Tag";
                Symbol = "◆";
            }
            else if (name.StartsWith("HEAD -> ", StringComparison.Ordinal))
            {
                Name = name.Substring("HEAD -> ".Length);
                Kind = "Current branch";
                Symbol = "✓";
            }
            else if (name.StartsWith("tag: ", StringComparison.Ordinal))
            {
                Name = name.Substring("tag: ".Length);
                Kind = "Tag";
                Symbol = "◆";
            }
            else
            {
                Name = name;
                Kind = "Reference";
                Symbol = "●";
            }

            ToolTipText = $"{Kind}: {Name}";
        }

        public string Name { get; }
        public string Kind { get; }
        public string Symbol { get; }
        public string ToolTipText { get; }
        public bool IsSymbolicRemote { get; }
        public int InlinePriority
        {
            get
            {
                if (Kind == "Current branch") return 0;
                if (Kind == "Local branch") return 1;
                if (Kind == "Remote branch") return 2;
                if (Kind == "Tag") return 3;
                return 4;
            }
        }
        public bool IsCurrent { get { return Kind == "Current branch"; } }
        public bool IsLocal { get { return Kind == "Local branch"; } }
        public bool IsRemote { get { return Kind == "Remote branch"; } }
        public bool IsTag { get { return Kind == "Tag"; } }
    }
}
