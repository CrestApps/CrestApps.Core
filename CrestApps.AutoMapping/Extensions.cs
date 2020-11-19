using AutoMapper;
using System;
using System.Linq;

namespace CrestApps.AutoMapping
{
    public static class Extensions
    {
        public static IMapperConfigurationExpression UseMapFrom(this IMapperConfigurationExpression expression, params Type[] scannableTypes)
        {
            if (scannableTypes == null)
            {
                return expression;
            }

            var types = scannableTypes.Where(x => typeof(IMap).IsAssignableFrom(x) && x.IsClass && !x.IsInterface && !x.IsAbstract)
                          .ToList();

            foreach (Type type in types)
            {
                Type genericType = type.GetInterfaces()
                                       .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IMapFrom<>));

                if (genericType != null)
                {
                    expression.CreateMap(genericType.GetGenericArguments()[0], type);
                }

                if (typeof(IMapFrom).IsAssignableFrom(type))
                {
                    try
                    {
                        IMapFrom config = (IMapFrom)Activator.CreateInstance(type);

                        config.Map(expression);
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"The class '{type.FullName}' implements '{nameof(IMapFrom)}'! It must have a parameterless constructor and should be a simple DTO. {e.Message}");
                    }
                }
            }

            return expression;
        }


        public static IMappingExpression<TSource, TDestination> IgnoreAllVirtualProperties<TSource, TDestination>(
                   this IMappingExpression<TSource, TDestination> expression)
        {
            var desType = typeof(TDestination);

            foreach (var property in desType.GetProperties()
                                            .Where(p => p.GetGetMethod().IsVirtual && !p.GetGetMethod().IsFinal))
            {
                expression.ForMember(property.Name, opt => opt.Ignore());
            }

            return expression;
        }
    }
}
