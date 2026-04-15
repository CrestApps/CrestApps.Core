using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.Areas.Admin.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace CrestApps.Core.Blazor.Web.Services;

public class BlazorAppDbContext : DbContext
{
    public BlazorAppDbContext(DbContextOptions<BlazorAppDbContext> options) : base(options) { }

    public DbSet<Article> Articles { get; set; }
    public DbSet<AIChatSessionEvent> SessionEvents { get; set; }
    public DbSet<AIChatSessionExtractedDataRecord> ExtractedDataRecords { get; set; }
    public DbSet<AICompletionUsageRecord> UsageRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Article>(entity =>
        {
            entity.ToTable("BA_Articles");
            entity.HasKey(e => e.ItemId);
            entity.Ignore(e => e.Properties);
        });

        modelBuilder.Entity<AIChatSessionEvent>(entity =>
        {
            entity.ToTable("BA_SessionEvents");
            entity.HasKey(e => e.SessionId);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.ProfileId);
            entity.HasIndex(e => e.SessionStartedUtc);
            entity.Ignore(e => e.Properties);
            entity.Property(e => e.ConversionGoalResults).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<List<ConversionGoalResult>>(v, (JsonSerializerOptions)null),
                new ValueComparer<List<ConversionGoalResult>>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions)null),
                    v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions)null).GetHashCode(),
                    v => JsonSerializer.Deserialize<List<ConversionGoalResult>>(JsonSerializer.Serialize(v, (JsonSerializerOptions)null), (JsonSerializerOptions)null)));
        });

        modelBuilder.Entity<AIChatSessionExtractedDataRecord>(entity =>
        {
            entity.ToTable("BA_ExtractedData");
            entity.HasKey(e => e.ItemId);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.ProfileId);
            entity.Ignore(e => e.Properties);
            entity.Property(e => e.Values).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<Dictionary<string, List<string>>>(v, (JsonSerializerOptions)null),
                new ValueComparer<Dictionary<string, List<string>>>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions)null),
                    v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions)null).GetHashCode(),
                    v => JsonSerializer.Deserialize<Dictionary<string, List<string>>>(JsonSerializer.Serialize(v, (JsonSerializerOptions)null), (JsonSerializerOptions)null)));
        });

        modelBuilder.Entity<AICompletionUsageRecord>(entity =>
        {
            entity.ToTable("BA_UsageRecords");
            entity.Property<string>("Id");
            entity.HasKey("Id");
            entity.HasIndex(e => e.CreatedUtc);
            entity.HasIndex(e => e.SessionId);
            entity.Ignore(e => e.Properties);
        });
    }
}
