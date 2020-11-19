using CrestApps.Data.Core.Abstraction;
using System;
using System.ComponentModel.DataAnnotations;

namespace CrestApps.Data.Models
{
    public class UserHistoricalPassword : IWriteModel, ITenantModel
    {
        public string Id { get; set; }

        public string UserId { get; set; }

        [MaxLength(512)]
        public string PasswordHash { get; set; }

        public DateTime CreatedAt { get; set; }

        [MaxLength(36)]
        public string TenantId { get; set; }

        public virtual User User { get; set; }
    }
}
