using System;
using System.Collections.ObjectModel;
using Bough.App.Internals;

namespace Bough.App.ViewModels
{
    public class ReferenceTreeNode : ViewModelBase
    {
        private readonly Action<ReferenceTreeNode, bool> _expansionChanged;
        private bool _isExpanded;

        public ReferenceTreeNode(string key, string label, string icon, string toolTipText, ReferenceTreeNodeKind kind, object target, bool isCurrent, bool isBranchSection, bool isExpanded, Action<ReferenceTreeNode, bool> expansionChanged, string actionText = null, string currentBranchText = null)
        {
            Key = key;
            Label = label;
            Icon = icon;
            ToolTipText = toolTipText;
            Kind = kind;
            Target = target;
            IsCurrent = isCurrent;
            IsBranchSection = isBranchSection;
            ActionText = actionText;
            CurrentBranchText = currentBranchText;
            AccessibleLabel = label;
            if (isCurrent == true && currentBranchText != null)
            {
                AccessibleLabel = $"{label} ({currentBranchText})";
            }
            _isExpanded = isExpanded;
            _expansionChanged = expansionChanged;
            Children = [];
        }

        public string Key { get; }
        public string Label { get; }
        public string Icon { get; }
        public string ToolTipText { get; }
        public ReferenceTreeNodeKind Kind { get; }
        public object Target { get; }
        public bool IsCurrent { get; }
        public bool IsSection { get { return Kind == ReferenceTreeNodeKind.Section; } }
        public bool IsBranchSection { get; }
        public bool IsTagSection { get { return Kind == ReferenceTreeNodeKind.Section && Key == "tags"; } }
        public bool HasSectionAction { get { return IsBranchSection || IsTagSection; } }
        public string ActionText { get; }
        public string CurrentBranchText { get; }
        public string AccessibleLabel { get; }
        public bool IsStashSection { get { return Kind == ReferenceTreeNodeKind.Section && Key == "stashes"; } }
        public bool IsEmpty { get { return Kind == ReferenceTreeNodeKind.Empty; } }
        public ObservableCollection<ReferenceTreeNode> Children { get; }

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (SetProperty(ref _isExpanded, value) == true)
                {
                    _expansionChanged?.Invoke(this, value);
                }
            }
        }
    }
}
