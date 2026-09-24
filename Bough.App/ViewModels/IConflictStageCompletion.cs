using System.Threading.Tasks;
using Bough.Core.Git;

namespace Bough.App.ViewModels
{
    public interface IConflictStageCompletion
    {
        Task CompleteConflictStageAsync(GitRepository repository, string stagedPath);
        Task RefreshConflictStateAsync(GitRepository repository);
    }
}
