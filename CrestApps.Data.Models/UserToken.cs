using CrestApps.Data.Core.Abstraction;
using Microsoft.AspNetCore.Identity;
using System;

namespace CrestApps.Data.Models
{
    public class UserToken : IdentityUserToken<Guid>, IWriteModel, ITenantModel
    {
        public Guid Id { get; set; }


        public Guid TenantId { get; set; }
    }
}
