using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Events.Abstraction
{
    public abstract class Listener<TEvent> : IListener<TEvent>
        where TEvent : class
    {
        protected TEvent Event { get; private set; }

        public virtual void Initilize(TEvent _event)
        {
            Event = _event ?? throw new ArgumentNullException(nameof(_event));
        }

        public virtual Task<bool> HandleAsync(CancellationToken cancellationToken = default)
        {

            return Task.FromResult(true);
        }

        public virtual Task FailedAsync(Exception e, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public virtual Task SucceedAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public virtual Task FinishedAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public virtual Task CleanUpAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
