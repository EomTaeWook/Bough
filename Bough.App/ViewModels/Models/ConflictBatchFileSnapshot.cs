using System.Collections.Generic;
using Bough.Core.Conflicts.Models;
using Bough.Core.Git.Models;
using Bough.Core.Internals;

namespace Bough.App.ViewModels.Models
{
    public class ConflictBatchFileSnapshot
    {
        public ConflictBatchFileSnapshot(GitConflictFile conflict, ConflictDocument document,
            IReadOnlyDictionary<int, ResolutionChoiceType> choices, string resultText, string renderedText, bool deleteFile)
        {
            Conflict = conflict;
            Document = document;
            Choices = new Dictionary<int, ResolutionChoiceType>(choices);
            ResultText = resultText;
            RenderedText = renderedText;
            DeleteFile = deleteFile;
        }

        public GitConflictFile Conflict { get; }
        public ConflictDocument Document { get; }
        public IReadOnlyDictionary<int, ResolutionChoiceType> Choices { get; }
        public string ResultText { get; }
        public string RenderedText { get; }
        public bool DeleteFile { get; }
    }
}