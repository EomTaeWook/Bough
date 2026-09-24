using System.Threading.Tasks;

namespace Bough.App.ViewModels
{
    public interface IStashMutationCompletion
    {
        Task CompleteStashSaveAsync(StashMutationResult result);

        Task CompleteStashApplyAsync(StashMutationResult result);

        Task CompleteStashPopAsync(StashMutationResult result);

        Task CompleteStashDropAsync(StashMutationResult result);
    }
}
