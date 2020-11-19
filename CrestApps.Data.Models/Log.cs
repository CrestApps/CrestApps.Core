using CrestApps.Data.Core.Abstraction;
using CrestApps.Data.Models.Enums;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CrestApps.Data.Models
{
    public class Log : IWriteModel, ITenantModel
    {
        public int Id { get; set; }

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

        public int? UserId { get; set; }

        public DateTime CreatedAt { get; set; }

        [MaxLength(36)]
        public string TenantId { get; set; }

        public virtual User User { get; set; }

        public Log()
        {
            CreatedAt = DateTime.UtcNow;
        }
    }
}
