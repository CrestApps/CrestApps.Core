using CrestApps.Data.Core.Abstraction;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace CrestApps.Data.Models
{
    public class UserClaim : IdentityUserClaim<string>, IWriteModel, ITenantModel
    {
        [MaxLength(36)]
        public string TenantId { get; set; }
    }
}
