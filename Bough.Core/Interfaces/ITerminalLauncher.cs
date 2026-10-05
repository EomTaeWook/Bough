using Bough.Core.Git.Models;

namespace Bough.Core.Interfaces
{
    public interface ITerminalLauncher
    {
        void Open(GitRepository repository);
    }
}
