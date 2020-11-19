namespace CrestApps.Foundation
{
    public interface IRegisterSingleton : IRegisterToContainer
    {
    }

    public interface IRegisterSingleton<T> : IRegisterSingleton
    {
    }
}
