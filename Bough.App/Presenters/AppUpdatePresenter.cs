using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Interfaces;
using Bough.App.Localization;
using Bough.App.ViewModels;
using Bough.Core.Updates;
using Bough.Core.Updates.Models;

namespace Bough.App.Presenters
{
    public class AppUpdatePresenter
    {
        private readonly ApplicationUpdateService _service;
        private readonly IApplicationUpdateRestart _restart;
        private readonly AppUpdateViewModel _screen;
        private ApplicationUpdateCheck _check;
        private VerifiedApplicationUpdate _download;
        private CancellationTokenSource _cancellation;
        private int _requestVersion;
        private int _viewVersion;
        private bool _active;
        private bool _restartPending;
        private bool _handedOff;

        public AppUpdatePresenter(ApplicationUpdateService service, IApplicationUpdateRestart restart, AppUpdateViewModel screen)
        {
            _service = service;
            _restart = restart;
            _screen = screen;
            _screen.CurrentVersion = service.CurrentVersion;
        }

        public void Activate()
        {
            if (_active)
            {
                return;
            }
            _active = true;
            _viewVersion++;
            RefreshAvailability();
        }

        public void Deactivate()
        {
            if (_active == false)
            {
                return;
            }
            _active = false;
            _viewVersion++;
            if (_screen.IsBusy)
            {
                if (_restartPending == false)
                {
                    _cancellation?.Cancel();
                    _screen.Status = new LocalizedText("AppUpdateCancelled");
                }
                else
                {
                    _screen.Status = null;
                }
            }
            else if (_download != null)
            {
                _screen.IsBusy = true;
                _ = ReleaseDetachedAsync(++_requestVersion);
            }
            RefreshAvailability();
        }

        public Task CheckAsync()
        {
            if (_screen.CanCheck == false)
            {
                return Task.CompletedTask;
            }
            return RunAsync(true);
        }

        public Task UpdateAsync()
        {
            if (_screen.CanUpdate == false)
            {
                return Task.CompletedTask;
            }
            return RunAsync(false);
        }

        public void Cancel()
        {
            if (_screen.CanCancel == false)
            {
                return;
            }
            _cancellation?.Cancel();
            _screen.CanCancel = false;
        }

        private async Task RunAsync(bool checking)
        {
            int requestVersion = ++_requestVersion;
            int viewVersion = _viewVersion;
            CancellationTokenSource cancellation = new();
            _cancellation = cancellation;
            bool restarting = false;
            _screen.IsBusy = true;
            _screen.Error = null;
            _screen.DownloadedBytes = 0;
            _screen.TotalBytes = 0;
            _screen.IsDownloading = false;
            RefreshAvailability();
            await Task.Yield();
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (IsCurrent(requestVersion, viewVersion) == false)
                {
                    return;
                }
                if (checking)
                {
                    _check = null;
                    _screen.LatestVersion = string.Empty;
                    _screen.ReleaseUrl = string.Empty;
                    _screen.Status = new LocalizedText("AppUpdateChecking");
                    await DiscardDownloadAsync();
                    cancellation.Token.ThrowIfCancellationRequested();
                    ApplicationUpdateCheck result = await _service.CheckAsync(cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (IsCurrent(requestVersion, viewVersion) == false)
                    {
                        return;
                    }
                    ApplyCheck(result);
                    return;
                }

                if (_download == null)
                {
                    ApplicationUpdateRelease release = _check.Release;
                    _screen.Status = new LocalizedText("AppUpdateAvailable", release.Version);
                    _screen.IsDownloading = true;
                    _screen.TotalBytes = release.Size;
                    long lastProgress = 0;
                    Progress<ApplicationUpdateProgress> progress = new(value =>
                    {
                        if (IsCurrent(requestVersion, viewVersion) == false)
                        {
                            return;
                        }
                        if (cancellation.IsCancellationRequested)
                        {
                            return;
                        }
                        if (_screen.IsDownloading == false)
                        {
                            return;
                        }
                        long timestamp = Stopwatch.GetTimestamp();
                        if (value.BytesReceived != value.TotalBytes)
                        {
                            if (Stopwatch.GetElapsedTime(lastProgress, timestamp).TotalMilliseconds < 100)
                            {
                                return;
                            }
                        }
                        lastProgress = timestamp;
                        _screen.DownloadedBytes = value.BytesReceived;
                        _screen.TotalBytes = value.TotalBytes;
                    });
                    _download = await _service.DownloadAsync(release, progress, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (IsCurrent(requestVersion, viewVersion) == false)
                    {
                        return;
                    }
                    _screen.Status = new LocalizedText("AppUpdateReady", release.Version);
                }

                restarting = true;
                _restartPending = true;
                bool accepted;
                try
                {
                    _screen.IsDownloading = false;
                    _screen.Status = new LocalizedText("AppUpdateRestartPreparing");
                    if (IsCurrent(requestVersion, viewVersion) == false)
                    {
                        return;
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                    accepted = await _restart.RequestRestartAsync(_download, cancellation.Token);
                }
                finally
                {
                    _restartPending = false;
                }
                if (accepted)
                {
                    _download = null;
                    _handedOff = true;
                }
                if (IsCurrent(requestVersion, viewVersion) == false)
                {
                    return;
                }
                if (accepted)
                {
                    _screen.Status = new LocalizedText("AppUpdateRestartScheduled");
                }
                else
                {
                    _screen.Status = new LocalizedText("AppUpdateRestartCancelled");
                }
            }
            catch (OperationCanceledException)
            {
                if (IsCurrent(requestVersion, viewVersion))
                {
                    _screen.Status = new LocalizedText("AppUpdateCancelled");
                }
            }
            catch (Exception exception)
            {
                if (IsCurrent(requestVersion, viewVersion))
                {
                    string key = "AppUpdateDownloadFailed";
                    string code = "AppUpdateCoreDownloadUnavailable";
                    if (checking)
                    {
                        key = "AppUpdateCheckFailed";
                        code = "AppUpdateCoreQueryUnavailable";
                    }
                    else if (restarting)
                    {
                        key = "AppUpdateRestartFailed";
                        code = "AppUpdateCorePrepareFailed";
                    }
                    ApplicationUpdateException error = GetSafeError(exception, code);
                    _screen.Status = null;
                    _screen.Error = new LocalizedText(key, new LocalizedText(error));
                    if (error.ReleasePage != null)
                    {
                        _screen.ReleaseUrl = error.ReleasePage.AbsoluteUri;
                    }
                }
            }
            finally
            {
                if (IsCurrent(requestVersion, viewVersion) == false)
                {
                    try
                    {
                        await DiscardDownloadAsync();
                    }
                    catch (Exception)
                    {
                        _check = null;
                    }
                }
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                }
                cancellation.Dispose();
                if (requestVersion == _requestVersion)
                {
                    _screen.IsDownloading = false;
                    _screen.IsBusy = false;
                    RefreshAvailability();
                }
            }
        }

        private void ApplyCheck(ApplicationUpdateCheck result)
        {
            _check = result;
            _screen.CurrentVersion = _service.CurrentVersion;
            _screen.LatestVersion = result.Release?.Version ?? string.Empty;
            _screen.ReleaseUrl = result.Release?.ReleasePage.AbsoluteUri ?? string.Empty;
            if (result.HasStableRelease == false)
            {
                _screen.Status = new LocalizedText("AppUpdateNoOfficialRelease");
                return;
            }
            if (result.HasUpdate == false)
            {
                _screen.Status = new LocalizedText("AppUpdateUpToDate");
                return;
            }
            _screen.Status = new LocalizedText("AppUpdateAvailable", result.Release.Version);
            if (result.InstallBlockedCode != null)
            {
                _screen.Error = new LocalizedText(new ApplicationUpdateException(result.InstallBlockedCode, null, Array.Empty<object>()));
            }
        }

        private bool IsCurrent(int requestVersion, int viewVersion)
        {
            if (_active == false)
            {
                return false;
            }
            if (requestVersion != _requestVersion)
            {
                return false;
            }
            if (viewVersion != _viewVersion)
            {
                return false;
            }
            return true;
        }

        private void RefreshAvailability()
        {
            _screen.CanCheck = false;
            _screen.CanUpdate = false;
            _screen.CanCancel = false;
            if (_active == false)
            {
                return;
            }
            if (_handedOff)
            {
                return;
            }
            if (_screen.IsBusy)
            {
                if (_cancellation != null)
                {
                    _screen.CanCancel = _cancellation.IsCancellationRequested == false;
                }
                return;
            }
            _screen.CanCheck = true;
            if (_check == null)
            {
                return;
            }
            _screen.CanUpdate = _check.CanInstall;
        }

        private async Task DiscardDownloadAsync()
        {
            if (_restartPending)
            {
                return;
            }
            if (_handedOff)
            {
                return;
            }
            VerifiedApplicationUpdate download = _download;
            if (download == null)
            {
                return;
            }
            await _service.DiscardAsync(download);
            if (ReferenceEquals(_download, download))
            {
                _download = null;
            }
        }

        private async Task ReleaseDetachedAsync(int requestVersion)
        {
            await Task.Yield();
            try
            {
                await DiscardDownloadAsync();
            }
            catch (Exception)
            {
                _check = null;
            }
            finally
            {
                if (requestVersion == _requestVersion)
                {
                    _screen.IsBusy = false;
                    RefreshAvailability();
                }
            }
        }

        private static ApplicationUpdateException GetSafeError(Exception exception, string fallbackCode)
        {
            if (exception is ApplicationUpdateException error)
            {
                return error;
            }
            return new ApplicationUpdateException(fallbackCode, exception, Array.Empty<object>());
        }
    }
}
