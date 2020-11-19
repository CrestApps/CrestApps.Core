using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Events.Abstraction
{
    public interface IListener<TEvent> where TEvent : class
    {
        /// <summary>
        /// Initilizes the listener
        /// </summary>
        /// <param name="_event"></param>
        void Initilize(TEvent _event);

        /// <summary>
        /// Handles the event
        /// </summary>
        /// <returns></returns>
        Task<bool> HandleAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// handels logging
        /// Typically is called when the HandleAsync method throw an exception
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        Task FailedAsync(Exception e, CancellationToken cancellationToken = default);

        /// <summary>
        /// This should be called when the HandleAsync returns true
        /// </summary>
        /// <returns></returns>
        Task SucceedAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// This shiould be called when the HandleAsync finishes and throws no exceptions
        /// </summary>
        /// <returns></returns>
        Task FinishedAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// This should always be called even when the HandleAsync method throws exeptions
        /// </summary>
        /// <returns></returns>
        Task CleanUpAsync(CancellationToken cancellationToken = default);
    }
}
