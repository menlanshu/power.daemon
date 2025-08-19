using Microsoft.EntityFrameworkCore;
using PowerDaemon.Shared.Models;

namespace PowerDaemon.Central.Data;

public class PowerDaemonContext : DbContext
{
    public PowerDaemonContext(DbContextOptions<PowerDaemonContext> options) : base(options)
    {
    }

    public DbSet<Server> Servers { get; set; }
    public DbSet<Service> Services { get; set; }
    public DbSet<ServiceType> ServiceTypes { get; set; }
    public DbSet<Deployment> Deployments { get; set; }
    public DbSet<Metric> Metrics { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Server configuration
        modelBuilder.Entity<Server>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Hostname).IsUnique();
            entity.HasIndex(e => e.AgentStatus);
            entity.HasIndex(e => e.LastHeartbeat);
            entity.HasIndex(e => e.OsType);
            
            entity.Property(e => e.OsType).HasConversion<string>();
            entity.Property(e => e.AgentStatus).HasConversion<string>();
        });

        // Service configuration
        modelBuilder.Entity<Service>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ServerId, e.Name });
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.IsActive);
            
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.StartupType).HasConversion<string>();
            
            entity.HasOne(e => e.Server)
                  .WithMany(e => e.Services)
                  .HasForeignKey(e => e.ServerId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.HasOne(e => e.ServiceType)
                  .WithMany(e => e.Services)
                  .HasForeignKey(e => e.ServiceTypeId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ServiceType configuration
        modelBuilder.Entity<ServiceType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Deployment configuration
        modelBuilder.Entity<Deployment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.ServiceId, e.Version });
            
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.DeploymentStrategy).HasConversion<string>();
            
            entity.HasOne(e => e.Service)
                  .WithMany(e => e.Deployments)
                  .HasForeignKey(e => e.ServiceId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.HasOne(e => e.RollbackDeployment)
                  .WithMany()
                  .HasForeignKey(e => e.RollbackDeploymentId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // Metric configuration
        modelBuilder.Entity<Metric>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ServerId, e.Timestamp });
            entity.HasIndex(e => new { e.ServiceId, e.Timestamp });
            entity.HasIndex(e => new { e.MetricType, e.MetricName, e.Timestamp });
            
            entity.HasOne(e => e.Server)
                  .WithMany()
                  .HasForeignKey(e => e.ServerId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.HasOne(e => e.Service)
                  .WithMany(e => e.Metrics)
                  .HasForeignKey(e => e.ServiceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Seed initial service types
        modelBuilder.Entity<ServiceType>().HasData(
            new ServiceType
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "TypeA",
                Description = "Service Type A",
                DefaultPortRange = "8000-8099",
                CreatedAt = DateTime.UtcNow
            },
            new ServiceType
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "TypeB",
                Description = "Service Type B",
                DefaultPortRange = "8100-8199",
                CreatedAt = DateTime.UtcNow
            },
            new ServiceType
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "TypeC",
                Description = "Service Type C",
                DefaultPortRange = "8200-8299",
                CreatedAt = DateTime.UtcNow
            },
            new ServiceType
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "TypeD",
                Description = "Service Type D",
                DefaultPortRange = "8300-8399",
                CreatedAt = DateTime.UtcNow
            }
        );
    }
}