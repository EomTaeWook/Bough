using System.Threading;
using System.Threading.Tasks;
using Bough.Core.Updates.Models;

namespace Bough.App.Interfaces
{
    public interface IApplicationUpdateRestart
    {
        Task<bool> RequestRestartAsync(VerifiedApplicationUpdate update, CancellationToken cancellationToken = default);
    }
}