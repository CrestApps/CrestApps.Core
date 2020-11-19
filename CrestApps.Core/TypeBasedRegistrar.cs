using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CrestApps.AutoMapping;

namespace CrestApps.Core
{
    public static class TypeBasedRegistrar
    {
        public static IServiceCollection AddTypesUsingReflection(this IServiceCollection services)
        {
            Assembly[] assemblies = GetAssemblies();

            List<Type> types = new List<Type>();

            foreach (Assembly assembly in assemblies)
            {
                try
                {
                    types.AddRange(assembly.GetTypes().Where(x => x.IsClass && !x.IsInterface));
                }
                catch (Exception)
                {
                }
            }

            Type[] myTypes = types.ToArray();

            services.RegisterDefinedObjects(myTypes);

            List<string> autoMappingAssemblyNames = new List<string>()
            {
                "CrestApps.AutoMapping",
                "CrestApps.Core.WebPresentation",
            };

            Assembly[] autoMappingAssemblies = assemblies.Where(x => autoMappingAssemblyNames.Contains(x.GetName().Name)).ToArray();
            services.AddAutoMapper((expression) =>
            {
                expression.UseMapFrom(myTypes);
            }, autoMappingAssemblies);

            return services;
        }

        private static Assembly[] GetAssemblies()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            return assemblies;
        }

    }
}
