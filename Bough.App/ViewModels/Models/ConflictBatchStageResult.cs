using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Bough.App.Localization;
using Bough.App.Internals;
using Bough.Core.Git.Models;

namespace Bough.App.ViewModels.Models
{
    public class ConflictBatchStageResult
    {
        public ConflictBatchStageResult(GitRepository repository, IEnumerable<ConflictStageResult> files,
            IDictionary<string, LocalizedText> excluded, Exception refreshException = null)
        {
            Repository = repository;
            Files = Array.AsReadOnly(files.ToArray());
            Excluded = new ReadOnlyDictionary<string, LocalizedText>(new Dictionary<string, LocalizedText>(excluded));
            RefreshException = refreshException;
        }

        public GitRepository Repository { get; }
        public IReadOnlyList<ConflictStageResult> Files { get; }
        public IReadOnlyDictionary<string, LocalizedText> Excluded { get; }
        public Exception RefreshException { get; }
        public int StagedCount { get { return Files.Count(file => file.WasStaged); } }
        public int FailedCount { get { return Files.Count(file => file.WasStaged == false); } }
        public bool HasIssues { get { return FailedCount > 0 || Excluded.Count > 0 || RefreshException != null || Files.Any(file => file.Outcome == ConflictStageOutcome.RefreshFailed); } }
    }
}