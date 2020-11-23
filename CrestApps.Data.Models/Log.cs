using CrestApps.Data.Core.Abstraction;
using CrestApps.Data.Models.Enums;
using System;
using System.ComponentModel.DataAnnotations;

namespace CrestApps.Data.Models
{
    public class Log : IWriteModel, ITenantModel
    {
        public Guid Id { get; set; }

        [MaxLength(50)]

        public LogType Type { get; set; }

        [MaxLength(500)]
        public string Message { get; set; }

        [MaxLength(255)]
        public string IpAddress { get; set; }

        [MaxLength(2500)]
        public string AgentInfo { get; set; }

        [MaxLength(255)]
        public string Result { get; set; }

        [MaxLength(8000)]
        public string Info { get; set; }

        public Guid? UserId { get; set; }

        public DateTime CreatedAt { get; set; }

        public Guid TenantId { get; set; }

        public virtual User User { get; set; }

        public Log()
        {
            CreatedAt = DateTime.UtcNow;
        }
    }
}
