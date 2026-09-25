using Bough.App.Localization;

namespace Bough.App.ViewModels.Models
{
    public class HistoryLabels
    {
        private readonly StringHelper _strings;

        public HistoryLabels(StringHelper strings)
        {
            _strings = strings;
        }

        public string Heading { get { return _strings.GetString("HistoryHeading"); } }
        public string ScopeAll { get { return _strings.GetString("HistoryScopeAll"); } }
        public string ScopeAllTooltip { get { return _strings.GetString("HistoryScopeAllTooltip"); } }
        public string ScopeAllAccessible { get { return _strings.GetString("HistoryScopeAllAccessible"); } }
        public string ScopeCurrent { get { return _strings.GetString("HistoryScopeCurrent"); } }
        public string ScopeCurrentTooltip { get { return _strings.GetString("HistoryScopeCurrentTooltip"); } }
        public string ScopeCurrentAccessible { get { return _strings.GetString("HistoryScopeCurrentAccessible"); } }
        public string ExternalPhotos { get { return _strings.GetString("HistoryExternalPhotos"); } }
        public string ExternalPhotosTooltip { get { return _strings.GetString("HistoryExternalPhotosTooltip"); } }
        public string ExternalPhotosAccessible { get { return _strings.GetString("HistoryExternalPhotosAccessible"); } }
        public string CopySha { get { return _strings.GetString("HistoryCopySha"); } }
        public string CreateBranchHere { get { return _strings.GetString("HistoryCreateBranchHere"); } }
        public string CreateTagHere { get { return _strings.GetString("HistoryCreateTagHere"); } }
        public string CheckoutCommit { get { return _strings.GetString("HistoryCheckoutCommit"); } }
        public string ResetCommit { get { return _strings.GetString("HistoryResetCommit"); } }
        public string MoreReferencesTooltip { get { return _strings.GetString("HistoryMoreReferencesTooltip"); } }
        public string AllReferencesHeading { get { return _strings.GetString("HistoryAllReferencesHeading"); } }
        public string Empty { get { return _strings.GetString("HistoryEmpty"); } }
        public string LoadingCommits { get { return _strings.GetString("HistoryLoadingCommits"); } }
        public string Retry { get { return _strings.GetString("HistoryRetry"); } }
        public string ResizeHistory { get { return _strings.GetString("HistoryResizeHistory"); } }
        public string SelectCommit { get { return _strings.GetString("HistorySelectCommit"); } }
        public string TabCommit { get { return _strings.GetString("HistoryTabCommit"); } }
        public string TabChanges { get { return _strings.GetString("HistoryTabChanges"); } }
        public string TabFileTree { get { return _strings.GetString("HistoryTabFileTree"); } }
        public string ParentTooltip { get { return _strings.GetString("HistoryParentTooltip"); } }
        public string Copy { get { return _strings.GetString("HistoryCopy"); } }
        public string ExpandAll { get { return _strings.GetString("HistoryExpandAll"); } }
        public string CollapseAll { get { return _strings.GetString("HistoryCollapseAll"); } }
        public string NoChanges { get { return _strings.GetString("HistoryNoChanges"); } }
        public string Open { get { return _strings.GetString("HistoryOpen"); } }
        public string ShowExplorer { get { return _strings.GetString("HistoryShowExplorer"); } }
        public string FileHistory { get { return _strings.GetString("HistoryFileHistory"); } }
        public string ShowTree { get { return _strings.GetString("HistoryShowTree"); } }
        public string SaveAs { get { return _strings.GetString("HistorySaveAs"); } }
        public string CopyPath { get { return _strings.GetString("HistoryCopyPath"); } }
        public string LoadingDiff { get { return _strings.GetString("HistoryLoadingDiff"); } }
        public string SearchChangedPaths { get { return _strings.GetString("HistorySearchChangedPaths"); } }
        public string ResizeChanges { get { return _strings.GetString("HistoryResizeChanges"); } }
        public string SearchSnapshotPaths { get { return _strings.GetString("HistorySearchSnapshotPaths"); } }
        public string ResizeTree { get { return _strings.GetString("HistoryResizeTree"); } }
        public string CloseAuxiliary { get { return _strings.GetString("HistoryCloseAuxiliary"); } }
    }
}
