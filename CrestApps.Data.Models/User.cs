using CrestApps.Data.Core.Abstraction;
using CrestApps.Foundation;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace CrestApps.Data.Models
{
    public class User : IdentityUser<string>, IUser, IWriteModel, ITenantModel
    {
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string LastName { get; set; }

        public int LockoutCount { get; set; }
        public string TimeZoneName { get; set; }

        [MaxLength(36)]
        public string TenantId { get; set; }
    }
}
