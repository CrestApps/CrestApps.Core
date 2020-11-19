using CrestApps.Events.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace CrestApps.Core.WebPresentation
{
    public static class ServiceCollectionExtensions
    {

        public static IServiceCollection AddListenter<TEvent, TListener>(this IServiceCollection services)
            where TEvent : class
            where TListener : IListener<TEvent>
        {
            services.AddScoped(typeof(IListener<TEvent>), typeof(TListener));

            return services;
        }


        public static IServiceCollection AddListenter<TListener>(this IServiceCollection services)
        {
            Type t = typeof(TListener);

            Type contract = t.GetInterfaces().FirstOrDefault(x => x.IsGenericType &&
            (
                   x.GetGenericTypeDefinition() == typeof(IListener<>)
            ));

            if (contract != null)
            {
                services.AddScoped(contract, t);
            }


            return services;
        }
    }
}
