using System.Threading.Tasks;
using Bough.App.Interfaces;
using Bough.App.Localization;
using Bough.App.Presenters;
using Bough.Core.Updates;

namespace Bough.App.ViewModels
{
    public class AppUpdateViewModel : ViewModelBase
    {
        private readonly AppUpdatePresenter _presenter;
        private string _currentVersion = string.Empty;
        private string _latestVersion = string.Empty;
        private string _releaseUrl = string.Empty;
        private LocalizedText _status = new("AppUpdateNotChecked");
        private LocalizedText _error;
        private long _downloadedBytes;
        private long _totalBytes;
        private bool _isDownloading;
        private bool _isBusy;
        private bool _canCheck;
        private bool _canUpdate;
        private bool _canCancel;

        public AppUpdateViewModel(StringHelper strings, ApplicationUpdateService service, IApplicationUpdateRestart restart)
        {
            Strings = strings;
            _presenter = new AppUpdatePresenter(service, restart, this);
        }

        internal StringHelper Strings { get; }
        public string CurrentVersion { get { return _currentVersion; } internal set { SetProperty(ref _currentVersion, value); } }
        public string LatestVersion { get { return _latestVersion; } internal set { SetProperty(ref _latestVersion, value); } }
        public string ReleaseUrl { get { return _releaseUrl; } internal set { SetProperty(ref _releaseUrl, value); } }
        public LocalizedText Status { get { return _status; } internal set { SetProperty(ref _status, value); } }
        public LocalizedText Error { get { return _error; } internal set { SetProperty(ref _error, value); } }
        public long DownloadedBytes { get { return _downloadedBytes; } internal set { SetProperty(ref _downloadedBytes, value); } }
        public long TotalBytes { get { return _totalBytes; } internal set { SetProperty(ref _totalBytes, value); } }
        public bool IsDownloading { get { return _isDownloading; } internal set { SetProperty(ref _isDownloading, value); } }
        public bool IsBusy { get { return _isBusy; } internal set { SetProperty(ref _isBusy, value); } }
        public bool CanCheck { get { return _canCheck; } internal set { SetProperty(ref _canCheck, value); } }
        public bool CanUpdate { get { return _canUpdate; } internal set { SetProperty(ref _canUpdate, value); } }
        public bool CanCancel { get { return _canCancel; } internal set { SetProperty(ref _canCancel, value); } }

        public void Activate() { _presenter.Activate(); }
        public void Deactivate() { _presenter.Deactivate(); }
        public Task CheckAsync() { return _presenter.CheckAsync(); }
        public Task UpdateAsync() { return _presenter.UpdateAsync(); }
        public void Cancel() { _presenter.Cancel(); }
    }
}
