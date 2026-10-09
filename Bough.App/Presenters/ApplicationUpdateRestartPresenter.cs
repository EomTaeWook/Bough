using System;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.Interfaces;
using Bough.Core.Updates;
using Bough.Core.Updates.Models;

namespace Bough.App.Presenters
{
    public class ApplicationUpdateRestartPresenter : IApplicationUpdateRestart
    {
        private readonly ApplicationUpdateInstaller _installer;
        private readonly CancellationTokenSource _windowLifetime = new();
        private MainWindow _window;
        private ApplicationUpdateInstallPlan _pendingPlan;
        private Task<ApplicationUpdateInstallPlan> _preparation;
        private bool _requestPending;
        private bool _shutdownAccepted;

        public ApplicationUpdateRestartPresenter(ApplicationUpdateInstaller installer)
        {
            _installer = installer;
        }

        internal void BindWindow(MainWindow window)
        {
            if (_window != null)
            {
                throw new InvalidOperationException(nameof(BindWindow));
            }
            _window = window;
            _window.Closed += OnWindowClosed;
        }

        public async Task<bool> RequestRestartAsync(VerifiedApplicationUpdate update, CancellationToken cancellationToken = default)
        {
            if (_window == null)
            {
                throw new ApplicationUpdateException("AppUpdateCoreCloseUnavailable", null, Array.Empty<object>());
            }
            if (_windowLifetime.IsCancellationRequested)
            {
                throw new ApplicationUpdateException("AppUpdateCoreCloseUnavailable", null, Array.Empty<object>());
            }
            if (_requestPending)
            {
                throw new ApplicationUpdateException("AppUpdateCorePrepareFailed", null, Array.Empty<object>());
            }
            _window.EnsureUpdateCanClose();
            _requestPending = true;
            ApplicationUpdateInstallPlan plan = null;
            using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _windowLifetime.Token);
            try
            {
                _preparation = _installer.PrepareAsync(update, lifetime.Token);
                plan = await _preparation;
                _pendingPlan = plan;
                lifetime.Token.ThrowIfCancellationRequested();
                await _installer.StartHelperAsync(plan, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                _window.EnsureUpdateCanClose();
                _pendingPlan = plan;
                bool accepted = await _window.RequestUpdateCloseAsync(lifetime.Token).ConfigureAwait(false);
                if (accepted == false)
                {
                    _pendingPlan = null;
                    await _installer.CancelAsync(plan).ConfigureAwait(false);
                    return false;
                }
                // Program authorizes the fixed helper only after normal Avalonia shutdown.
                // No installed file is replaced while this application is still running.
                return true;
            }
            catch
            {
                if (_shutdownAccepted)
                {
                    // Once Closed confirms normal shutdown, the fixed plan owns the download.
                    // A late UI lifetime cancellation is not a failed handoff.
                    return true;
                }
                _pendingPlan = null;
                await _installer.CancelAsync(plan).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _requestPending = false;
                if (_shutdownAccepted == false)
                {
                    _preparation = null;
                }
            }
        }

        internal async Task CompleteShutdownAsync()
        {
            try
            {
                ApplicationUpdateInstallPlan plan = _pendingPlan;
                if (plan == null)
                {
                    if (_preparation != null)
                    {
                        try
                        {
                            // UI may have closed before its await continuation received the prepared plan.
                            plan = await _preparation.ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            return;
                        }
                    }
                }
                if (plan == null)
                {
                    return;
                }
                if (_shutdownAccepted == false)
                {
                    await _installer.CancelAsync(plan).ConfigureAwait(false);
                    return;
                }
                await _installer.AuthorizeAfterShutdownAsync(plan).ConfigureAwait(false);
            }
            finally
            {
                _windowLifetime.Dispose();
            }
        }
        private void OnWindowClosed(object sender, EventArgs eventArgs)
        {
            _window.Closed -= OnWindowClosed;
            _shutdownAccepted = _window.UpdateCloseApproved;
            if (_shutdownAccepted == false)
            {
                _windowLifetime.Cancel();
            }
        }
    }
}