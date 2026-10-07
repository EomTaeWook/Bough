using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bough.App.Localization;
using Bough.App.ViewModels;
using Bough.App.ViewModels.Models;
using Bough.Core.Conflicts;
using Bough.Core.Conflicts.Models;
using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.Presenters
{
    public class ConflictLoadPresenter
    {
        private readonly GitRepositoryService _repositoryService;
        private readonly ConflictParser _parser;
        private readonly StringHelper _stringHelper;
        private readonly ConflictResolutionViewModel _model;

        public ConflictLoadPresenter(GitRepositoryService repositoryService, ConflictParser parser,
            StringHelper stringHelper, ConflictResolutionViewModel model)
        {
            _repositoryService = repositoryService;
            _parser = parser;
            _stringHelper = stringHelper;
            _model = model;
        }

        public async Task LoadAsync(ConflictFileItem file)
        {
            if (_model.IsBusy == true)
            {
                return;
            }
            int request = _model.ConflictRequestVersion;
            GitRepository repository = _model.CurrentRepository;
            _model.IsBusy = true;
            try
            {
                await LoadCoreAsync(file);
            }
            catch (Exception exception)
            {
                if (request == _model.ConflictRequestVersion)
                {
                    _model.SetLocalizedStatusMessage(new LocalizedText(exception));
                }
            }
            finally
            {
                if (ReferenceEquals(_model.CurrentRepository, repository) == true)
                {
                    _model.IsBusy = false;
                }
            }
        }

        internal async Task LoadCoreAsync(ConflictFileItem file)
        {
            if (_model.CurrentRepository == null)
            {
                return;
            }

            GitRepository repository = _model.CurrentRepository;
            int loadVersion = ++_model.ConflictLoadVersion;
            string draftText = _model.ResultText;
            bool isRebaseConflict = await _repositoryService.IsRebaseInProgressAsync(repository);
            if (loadVersion != _model.ConflictLoadVersion)
            {
                return;
            }
            if (ReferenceEquals(_model.CurrentRepository, repository) == false)
            {
                return;
            }
            if (_model.SelectedFile?.RelativePath != file.RelativePath)
            {
                return;
            }

            bool isRevertConflict = _model.IsRevertOperationInProgress;
            string currentChangeLabel = _stringHelper.GetString("CurrentChange");
            string incomingChangeLabel = _stringHelper.GetString("IncomingChange");
            string currentSourceLabel = repository.CurrentBranch;
            string incomingSourceLabel = incomingChangeLabel;
            if (isRebaseConflict)
            {
                currentChangeLabel = _stringHelper.GetString("RebaseTargetChange");
                incomingChangeLabel = _stringHelper.GetString("RebaseReplayChange");
                currentSourceLabel = _stringHelper.GetString("RebaseTargetSource");
                incomingSourceLabel = _stringHelper.GetString("RebaseReplaySource");
            }

            string incomingIndexSource = _stringHelper.GetString("ConflictIncomingIndexStage3");
            if (isRebaseConflict == false)
            {
                if (isRevertConflict)
                {
                    incomingChangeLabel = _stringHelper.GetString("RevertIncomingChange");
                    incomingSourceLabel = incomingChangeLabel;
                    incomingIndexSource = _stringHelper.GetString("RevertIncomingIndexSource");
                }
            }
            GitConflictFile conflict = await _repositoryService.LoadConflictAsync(repository, file.RelativePath,
                currentSourceLabel, incomingSourceLabel, incomingIndexSource);
            if (loadVersion != _model.ConflictLoadVersion)
            {
                return;
            }
            if (ReferenceEquals(_model.CurrentRepository, repository) == false)
            {
                return;
            }
            if (_model.SelectedFile?.RelativePath != file.RelativePath)
            {
                return;
            }
            if (conflict.WorkingText.Contains('\0') == true)
            {
                throw new GitException("ConflictBinaryFileCannotEdit", null, file.RelativePath);
            }

            (ConflictDocument Document, string InitialResult) parsed = await Task.Run(() =>
            {
                ConflictDocument document = _parser.Parse(conflict.WorkingText, currentChangeLabel, incomingChangeLabel);
                string initialResult = document.Render(new Dictionary<int, ResolutionChoiceType>());
                return (document, initialResult);
            });
            if (loadVersion != _model.ConflictLoadVersion)
            {
                return;
            }
            if (ReferenceEquals(_model.CurrentRepository, repository) == false)
            {
                return;
            }
            if (_model.SelectedFile?.RelativePath != file.RelativePath)
            {
                return;
            }
            if (draftText != _model.ResultText)
            {
                return;
            }
            if (parsed.Document.Hunks.Count == 0)
            {
                throw new Bough.Core.Conflicts.Exceptions.ConflictParseException("ConflictTextMarkersMissing", file.RelativePath);
            }

            _model.ApplyConflictDocument(conflict, parsed.Document, parsed.InitialResult,
                isRebaseConflict, currentSourceLabel, incomingSourceLabel, incomingIndexSource);
        }
    }
}
