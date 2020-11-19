using CrestApps.Common.Helpers;
using CrestApps.Data.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System;

namespace CrestApps.Data.Entity
{
    public class ApplicationContext : IdentityDbContext<User, Role, string, UserClaim, UserRole, UserLogin, RoleClaim, UserToken>
    {
        private readonly IDatabaseContextBuilderConfigurator Configurator;

        public virtual DbSet<UserHistoricalPassword> UserHistoricalPasswords { get; set; }

        public virtual DbSet<Log> Logs { get; set; }


        public ApplicationContext(DbContextOptions options, IDatabaseContextBuilderConfigurator configurator)
            : base(options)
        {
            Configurator = configurator;
        }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
            {
                string tableName = Str.TrimStart(entityType.GetTableName(), "AspNet").ToSnakeCase();

                entityType.SetTableName(tableName);

                SetPropertyConventions(modelBuilder, entityType);
            };
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            Configurator.Configure(optionsBuilder);

            base.OnConfiguring(optionsBuilder);
        }

        protected void SetPropertyConventions(ModelBuilder modelBuilder, IMutableEntityType entityType)
        {
            foreach (IMutableProperty property in entityType.GetProperties())
            {
                if (property.PropertyInfo == null)
                {
                    continue;
                }

                Type propertyType = property.PropertyInfo.PropertyType;

                var prop = modelBuilder.Entity(entityType.ClrType)
                            .Property(propertyType, property.Name);

                if (propertyType.IsTrueEnum())
                {
                    // At this point we know that the property is an enum.
                    // Add the EnumToStringConverter converter to the property so that
                    // the value is stored in the database as a string instead of number 

                    prop.HasConversion<string>();
                }
            }
        }

    }
}
