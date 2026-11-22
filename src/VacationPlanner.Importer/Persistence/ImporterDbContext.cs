using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VacationPlanner.Importer.Models;

namespace VacationPlanner.Importer.Persistence;

internal sealed class ImporterDbContext(DbContextOptions<ImporterDbContext> options) : DbContext(options)
{
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportError> ImportErrors => Set<ImportError>();
    public DbSet<Dispatch> Dispatches => Set<Dispatch>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var job = model.Entity<ImportJob>();
        job.Property(x => x.Status).HasConversion<string>();
        job.HasIndex(x => new { x.Status, x.HeartbeatAt });
        job.HasIndex(x => x.StartedAt);

        var error = model.Entity<ImportError>();
        error.HasIndex(x => new { x.ImportJobId, x.RowNumber }).IsUnique();
        error.HasOne<ImportJob>().WithMany().HasForeignKey(x => x.ImportJobId);
        error.Property(x => x.Message).HasMaxLength(300);
        error.Property(x => x.ErrorCode).HasMaxLength(50);

        var dispatch = model.Entity<Dispatch>();
        dispatch.HasKey(x => x.RequestId);
        dispatch.Property(x => x.RequestId).ValueGeneratedNever();
        dispatch.HasOne<ImportJob>().WithMany().HasForeignKey(x => x.ImportJobId);
    }
}

internal sealed class DesignFactory : IDesignTimeDbContextFactory<ImporterDbContext>
{
    public ImporterDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<ImporterDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Importer")
                ?? "Host=localhost;Database=vacation_importer").Options);
}
