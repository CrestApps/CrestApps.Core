namespace CrestApps.Data.Core.Abstraction
{
    public interface IIdAsPrimaryKey<TKeyType>
        where TKeyType : struct
    {
        TKeyType Id { get; set; }
    }

    public interface IIdAsPrimaryKey : IIdAsPrimaryKey<int>
    {
    }
}
