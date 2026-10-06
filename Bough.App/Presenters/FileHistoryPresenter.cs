using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Presenters
{
    public class FileHistoryPresenter : IDisposable
    {
        private readonly GitCommitInspectionService _service;
        private readonly FileHistoryViewModel _model;
        private CancellationTokenSource _listCancellation;
        private CancellationTokenSource _diffCancellation;
        private int _listVersion;
        private int _diffVersion;
        private bool _closed;

        public FileHistoryPresenter(GitCommitInspectionService service, FileHistoryViewModel model)
        {
            _service = service;
            _model = model;
            model.SelectionChanged += OnSelectionChanged;
        }

        public FileHistoryViewModel Model { get { return _model; } }

        public async Task LoadAsync()
        {
            if (_closed)
            {
                return;
            }
            int version = ++_listVersion;
            _listCancellation?.Cancel();
            using CancellationTokenSource cancellation = new();
            _listCancellation = cancellation;
            _model.SelectedEntry = null;
            _model.Entries.Clear();
            _model.ListError = null;
            _model.IsListCanceled = false;
            _model.IsLoadingList = true;
            try
            {
                GitFileHistoryPage page = await _service.GetFileHistoryAsync(_model.Repository, _model.RevisionHash, _model.FilePath, 100, cancellation.Token);
                if (_closed)
                {
                    return;
                }
                if (version != _listVersion)
                {
                    return;
                }
                foreach (GitFileHistoryEntry entry in page.Entries)
                {
                    _model.Entries.Add(new FileHistoryEntryItem(entry));
                }
                if (_model.Entries.Count > 0)
                {
                    _model.SelectedEntry = _model.Entries[0];
                }
            }
            catch (OperationCanceledException)
            {
                if (_closed)
                {
                    return;
                }
                if (version != _listVersion)
                {
                    return;
                }
                _model.IsListCanceled = true;
            }
            catch (Exception exception)
            {
                if (_closed)
                {
                    return;
                }
                if (version != _listVersion)
                {
                    return;
                }
                _model.ListError = exception;
            }
            finally
            {
                if (ReferenceEquals(_listCancellation, cancellation))
                {
                    _listCancellation = null;
                }
            }
            if (_closed)
            {
                return;
            }
            if (version != _listVersion)
            {
                return;
            }
            _model.IsLoadingList = false;
        }

        private void OnSelectionChanged(FileHistoryEntryItem item)
        {
            _ = LoadDiffAsync(item);
        }

        private async Task LoadDiffAsync(FileHistoryEntryItem item)
        {
            if (_closed)
            {
                return;
            }
            int version = ++_diffVersion;
            _diffCancellation?.Cancel();
            _model.DiffLines.Clear();
            _model.Diff = null;
            _model.DiffError = null;
            _model.IsDiffCanceled = false;
            _model.IsLoadingDiff = false;
            if (item == null)
            {
                return;
            }
            using CancellationTokenSource cancellation = new();
            _diffCancellation = cancellation;
            _model.IsLoadingDiff = true;
            try
            {
                GitFileHistoryEntry entry = item.Entry;
                GitCommitChangedFile file = new(entry.Status, entry.Path, entry.PreviousPath);
                GitCommitFileDiff diff = await _service.GetFileDiffAsync(_model.Repository, entry.CommitHash, null, file, cancellation.Token);
                if (_closed)
                {
                    return;
                }
                if (version != _diffVersion)
                {
                    return;
                }
                if (ReferenceEquals(item, _model.SelectedEntry) == false)
                {
                    return;
                }
                foreach (GitUnifiedDiffHunk hunk in diff.Hunks)
                {
                    if (_model.DiffLines.Count > 0)
                    {
                        _model.DiffLines.Add(new HistoryDiffLineItem(string.Empty, string.Empty, string.Empty, 'H'));
                    }
                    foreach (GitUnifiedDiffLine line in hunk.Lines)
                    {
                        string oldNumber = string.Empty;
                        string newNumber = string.Empty;
                        if (line.OldLineNumber > 0)
                        {
                            oldNumber = line.OldLineNumber.ToString(CultureInfo.InvariantCulture);
                        }
                        if (line.NewLineNumber > 0)
                        {
                            newNumber = line.NewLineNumber.ToString(CultureInfo.InvariantCulture);
                        }
                        _model.DiffLines.Add(new HistoryDiffLineItem(oldNumber, newNumber, line.Text, line.Kind));
                    }
                }
                _model.Diff = diff;
            }
            catch (OperationCanceledException)
            {
                if (_closed)
                {
                    return;
                }
                if (version != _diffVersion)
                {
                    return;
                }
                if (ReferenceEquals(item, _model.SelectedEntry) == false)
                {
                    return;
                }
                _model.IsDiffCanceled = true;
            }
            catch (Exception exception)
            {
                if (_closed)
                {
                    return;
                }
                if (version != _diffVersion)
                {
                    return;
                }
                if (ReferenceEquals(item, _model.SelectedEntry) == false)
                {
                    return;
                }
                _model.DiffError = exception;
            }
            finally
            {
                if (ReferenceEquals(_diffCancellation, cancellation))
                {
                    _diffCancellation = null;
                }
            }
            if (_closed)
            {
                return;
            }
            if (version != _diffVersion)
            {
                return;
            }
            if (ReferenceEquals(item, _model.SelectedEntry) == false)
            {
                return;
            }
            _model.IsLoadingDiff = false;
        }

        public void CancelLoading()
        {
            if (_closed)
            {
                return;
            }
            _listVersion++;
            _diffVersion++;
            _listCancellation?.Cancel();
            _diffCancellation?.Cancel();
            if (_model.IsLoadingList)
            {
                _model.IsListCanceled = true;
            }
            if (_model.IsLoadingDiff)
            {
                _model.IsDiffCanceled = true;
            }
            _model.IsLoadingList = false;
            _model.IsLoadingDiff = false;
        }

        public void Dispose()
        {
            if (_closed)
            {
                return;
            }
            _closed = true;
            _listVersion++;
            _diffVersion++;
            _model.SelectionChanged -= OnSelectionChanged;
            _listCancellation?.Cancel();
            _diffCancellation?.Cancel();
        }
    }
}
