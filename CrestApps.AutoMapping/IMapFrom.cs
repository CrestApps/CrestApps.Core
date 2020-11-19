using AutoMapper;

namespace CrestApps.AutoMapping
{
    public interface IMapFrom<T> : IMap
    {
    }

    public interface IMapFrom : IMap
    {
        void Map(IMapperConfigurationExpression expression);
    }
}