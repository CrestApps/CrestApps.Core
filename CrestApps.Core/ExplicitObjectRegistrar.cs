using CrestApps.Events.Abstraction;
using CrestApps.Foundation;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace CrestApps.Core
{
    public static class ExplicitObjectRegistrar
    {
        public static IServiceCollection RegisterListener<T>(this IServiceCollection service)
        {
            service.AddScoped(typeof(IListener<>), typeof(T));

            return service;
        }

        public static IServiceCollection RegisterDefinedObjects(this IServiceCollection services, params Type[] scannableTypes)
        {
            if (scannableTypes == null)
            {
                return services;
            }

            var registerableTypes = scannableTypes.Where(obj => typeof(IRegisterToContainer).IsAssignableFrom(obj)).ToList();

            foreach (Type type in registerableTypes)
            {
                if (typeof(IRegisterSingleton).IsAssignableFrom(type))
                {
                    AddSingleton(services, type);

                    continue;
                }

                if (
                       typeof(IRegisterStore).IsAssignableFrom(type)
                    || typeof(IRegisterMapper).IsAssignableFrom(type)
                    || typeof(IRegisterPresenter).IsAssignableFrom(type)
                    )
                {
                    AddScoped(services, type);

                    continue;
                }

                AddTransient(services, type);
            }

            return services;
        }


        private static void AddSingleton(IServiceCollection services, Type obj)
        {
            Type contract = obj.GetInterfaces().FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IRegisterSingleton<>));

            if (contract != null)
            {
                services.AddSingleton(contract.GetGenericArguments()[0], obj);

                return;
            }

            services.AddSingleton(obj);
        }

        private static void AddScoped(IServiceCollection services, Type obj)
        {

            Type contract = obj.GetInterfaces().FirstOrDefault(x => x.IsGenericType &&
            (
                   x.GetGenericTypeDefinition() == typeof(IRegisterStore<>)
                || x.GetGenericTypeDefinition() == typeof(IRegisterMapper<>)
                || x.GetGenericTypeDefinition() == typeof(IRegisterPresenter<>)
            ));



            if (contract != null)
            {
                services.AddScoped(contract.GetGenericArguments()[0], obj);

                return;
            }

            services.AddScoped(obj);
        }

        private static void AddTransient(IServiceCollection services, Type obj)
        {
            Type contract = obj.GetInterfaces().FirstOrDefault(x => x.IsGenericType &&
            (
                    x.GetGenericTypeDefinition() == typeof(IRegisterToContainer<>)
            ));

            if (contract != null)
            {
                services.AddTransient(contract.GetGenericArguments()[0], obj);

                return;
            }

            services.AddTransient(obj);
        }
    }
}
