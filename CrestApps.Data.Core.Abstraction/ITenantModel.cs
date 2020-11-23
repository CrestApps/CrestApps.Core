using System;

namespace CrestApps.Data.Core.Abstraction
{
    public interface ITenantModel
    {
        Guid TenantId { get; set; }
    }
}
