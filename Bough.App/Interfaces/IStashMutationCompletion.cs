using System.Threading.Tasks;
using Bough.App.ViewModels.Models;

namespace Bough.App.Interfaces
{
    public interface IStashMutationCompletion
    {
        Task<StashMutationResult> CompleteStashSaveAsync(StashMutationResult result);

        Task CompleteStashApplyAsync(StashMutationResult result);

        Task CompleteStashPopAsync(StashMutationResult result);

        Task CompleteStashDropAsync(StashMutationResult result);
    }
}
