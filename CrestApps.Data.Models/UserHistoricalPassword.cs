using CrestApps.Data.Core.Abstraction;
using System;
using System.ComponentModel.DataAnnotations;

namespace CrestApps.Data.Models
{
    public class UserHistoricalPassword : IWriteModel, ITenantModel
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        [MaxLength(512)]
        public string PasswordHash { get; set; }

        public DateTime CreatedAt { get; set; }

        public Guid TenantId { get; set; }

        public virtual User User { get; set; }
    }
}
