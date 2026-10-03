using System.Threading.Tasks;
using Bough.Core.Git;
using Bough.Core.Git.Models;

namespace Bough.App.Interfaces
{
    public interface IConflictStageCompletion
    {
        Task CompleteConflictStageAsync(GitRepository repository, string stagedPath);
        Task RefreshConflictStateAsync(GitRepository repository);
    }
}
