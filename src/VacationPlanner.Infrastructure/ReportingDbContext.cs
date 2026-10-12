using Microsoft.EntityFrameworkCore;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Infrastructure;

/// <summary>
/// Контекст bounded-контекста reporting (CQRS read-сторона):
/// собственная БД с денормализованной read-моделью. Имена приходят вместе с событиями,
/// поэтому сервис не обращается к БД api.
/// </summary>
public class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public DbSet<ReadLeave> ReadLeaves => Set<ReadLeave>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReadLeave>(e =>
        {
            e.ToTable("ReadLeaves");
            e.HasIndex(r => r.RequestId).IsUnique(); // идемпотентность redelivery
            e.HasIndex(r => new { r.UserId, r.Status });
            e.HasIndex(r => new { r.StartDate, r.Status });
            e.Property(r => r.UserName).HasMaxLength(120);
        });
    }
}
