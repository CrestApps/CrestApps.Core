using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Events.Abstraction
{
    public interface IAnnouncer
    {
        Task AnnounceAsync<T>(T _event, CancellationToken cancellationToken = default) where T : class;
    }
}
