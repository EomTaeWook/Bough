using Bough.Core.Git.Models;

namespace Bough.App.ViewModels.Models
{
    public class HistoryPreviewRequest
    {
        public HistoryPreviewRequest()
        {
            IsClear = true;
        }

        public HistoryPreviewRequest(GitRepository repository, GitCommitInspection inspection, int inspectionVersion,
            string parent, string path, bool deleted, bool explicitPreview,
            HistoryInspectionFileItem selection, HistoryTreeItem treeSelection)
        {
            Repository = repository;
            Inspection = inspection;
            InspectionVersion = inspectionVersion;
            Parent = parent;
            Path = path;
            Revision = inspection.Hash;
            if (deleted)
            {
                Revision = parent;
            }
            IsExplicit = explicitPreview;
            Selection = selection;
            TreeSelection = treeSelection;
        }

        public bool IsClear { get; }
        public GitRepository Repository { get; }
        public GitCommitInspection Inspection { get; }
        public int InspectionVersion { get; }
        public string Parent { get; }
        public string Path { get; }
        public string Revision { get; }
        public bool IsExplicit { get; }
        public HistoryInspectionFileItem Selection { get; }
        public HistoryTreeItem TreeSelection { get; }
    }
}
