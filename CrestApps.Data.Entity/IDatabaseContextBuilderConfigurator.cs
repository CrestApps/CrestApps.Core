using Microsoft.EntityFrameworkCore;

namespace CrestApps.Data.Entity
{
    public interface IDatabaseContextBuilderConfigurator
    {
        void Configure(DbContextOptionsBuilder builder);
    }
}
