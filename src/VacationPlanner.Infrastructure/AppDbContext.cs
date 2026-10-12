using Microsoft.EntityFrameworkCore;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Infrastructure;

/// <summary>
/// Контекст write-бounded-контекста (api / scheduler): сотрудники и заявки.
/// У каждого сервиса своя БД: у audit — AuditDbContext, у reporting — ReportingDbContext.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.HasOne(u => u.Manager)
                .WithMany()
                .HasForeignKey(u => u.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.HasIndex(m => new { m.PublishedAt, m.Failed, m.CreatedAt });
            e.Property(m => m.Type).HasMaxLength(64);
        });

        modelBuilder.Entity<LeaveRequest>(e =>
        {
            e.HasOne(lr => lr.User)
                .WithMany(u => u.LeaveRequests)
                .HasForeignKey(lr => lr.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(lr => new { lr.UserId, lr.StartDate });
            e.Property(lr => lr.Reason).HasMaxLength(500);
        });
    }
}
