using CrestApps.Data.Core.Abstraction;
using Microsoft.AspNetCore.Identity;
using System;

namespace CrestApps.Data.Models
{
    public class RoleClaim : IdentityRoleClaim<Guid>, ITenantModel
    {
        public Guid TenantId { get; set; }
    }
}
