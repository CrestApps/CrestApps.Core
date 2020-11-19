using CrestApps.Data.Core.Abstraction;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace CrestApps.Data.Models
{
    public class UserLogin : IdentityUserLogin<string>, IWriteModel, ITenantModel
    {
        public string Id { get; set; }

        [MaxLength(36)]
        public string TenantId { get; set; }
    }
}
