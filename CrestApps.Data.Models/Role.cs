using CrestApps.Data.Core.Abstraction;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace CrestApps.Data.Models
{
    public class Role : IdentityRole<string>, IWriteModel, ITenantModel
    {
        [MaxLength(36)]
        public string TenantId { get; set; }
    }
}
