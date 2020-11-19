using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Events.Abstraction
{
    public abstract class AnnounerBase : IAnnouncer
    {
        public async Task AnnounceAsync<T>(T _event, CancellationToken cancellationToken = default) where T : class
        {
            IEnumerable<IListener<T>> listeners = await GetListenersAsync<T>() ?? Enumerable.Empty<IListener<T>>();

            foreach (IListener<T> listener in listeners)
            {
                try
                {
                    listener.Initilize(_event);

                    if (await listener.HandleAsync(cancellationToken))
                    {
                        await listener.SucceedAsync(cancellationToken);
                    }

                    await listener.FinishedAsync(cancellationToken);
                }
                catch (Exception e)
                {
                    await listener.FailedAsync(e, cancellationToken);
                }
                finally
                {
                    await listener.CleanUpAsync(cancellationToken);
                }
            }
        }

        public abstract Task<IEnumerable<IListener<T>>> GetListenersAsync<T>() where T : class;
    }
}
