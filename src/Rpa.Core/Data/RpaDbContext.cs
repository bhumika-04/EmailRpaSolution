using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rpa.Core.Models;
using System.Text.Json;

namespace Rpa.Core.Data;

public class RpaDbContext : DbContext
{
    public DbSet<Job> Jobs { get; set; }

    public RpaDbContext(DbContextOptions<RpaDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.SenderEmail);
            entity.HasIndex(e => new { e.Status, e.Priority });

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.ExtractedCredentials)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                    v => v == null ? null : JsonSerializer.Deserialize<string>(v, (JsonSerializerOptions)null!)
                );

            entity.Property(e => e.JobCardDetails)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                    v => v == null ? null : JsonSerializer.Deserialize<string>(v, (JsonSerializerOptions)null!)
                );

            entity.Property(e => e.ProcessingResult)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                    v => v == null ? null : JsonSerializer.Deserialize<string>(v, (JsonSerializerOptions)null!)
                );

            entity.Property(e => e.Metadata)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                    v => v == null ? null : JsonSerializer.Deserialize<string>(v, (JsonSerializerOptions)null!)
                );
        });
    }
}

public static class DbContextExtensions
{
    public static void ConfigureDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RpaDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly("Rpa.Core")
            )
        );
    }
}