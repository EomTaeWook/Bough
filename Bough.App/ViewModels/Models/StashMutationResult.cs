using Bough.Core.Git;
using Bough.Core.Git.Models;
using Bough.App.Internals;
using Bough.App.Localization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Bough.App.ViewModels.Models
{
    public class StashMutationResult
    {
        public StashMutationResult(GitRepository repository, StashMutationKind kind, bool succeeded, bool worktreeMayHaveChanged, bool stashesMayHaveChanged, GitWorktreeStatus worktreeStatus, string errorText, IReadOnlyList<LocalizedText> errors = null)
        {
            Repository = repository;
            Kind = kind;
            Succeeded = succeeded;
            WorktreeMayHaveChanged = worktreeMayHaveChanged;
            StashesMayHaveChanged = stashesMayHaveChanged;
            WorktreeStatus = worktreeStatus;
            ErrorText = errorText;
            Errors = Array.Empty<LocalizedText>();
            if (errors != null)
            {
                Errors = Array.AsReadOnly(errors.ToArray());
            }
        }

        public GitRepository Repository { get; }
        public StashMutationKind Kind { get; }
        public bool Succeeded { get; }
        public bool WorktreeMayHaveChanged { get; }
        public bool StashesMayHaveChanged { get; }
        public GitWorktreeStatus WorktreeStatus { get; }
        public string ErrorText { get; }
        public IReadOnlyList<LocalizedText> Errors { get; }
        public bool HasError
        {
            get
            {
                if (Errors.Count > 0)
                {
                    return true;
                }
                return string.IsNullOrEmpty(ErrorText) == false;
            }
        }

        public string GetErrorText(StringHelper strings)
        {
            if (Errors.Count == 0)
            {
                return ErrorText;
            }
            return string.Join(Environment.NewLine, Errors.Select(error => error.GetText(strings)));
        }
    }
}
