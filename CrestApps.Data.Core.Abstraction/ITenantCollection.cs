using System.Collections.Generic;

namespace CrestApps.Data.Core.Abstraction
{
    public interface ITenantCollection<T> : ICollection<T>
        where T : ITenantModel
    {
    }
}
