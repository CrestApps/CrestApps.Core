using CrestApps.Data.Core.Abstraction;
using Microsoft.AspNetCore.Identity;
using System;

namespace CrestApps.Data.Models
{
    public class Role : IdentityRole<Guid>, IWriteModel, ITenantModel
    {
        public Guid TenantId { get; set; }
    }
}
