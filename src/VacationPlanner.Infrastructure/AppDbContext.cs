using Microsoft.EntityFrameworkCore;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ReadLeave> ReadLeaves => Set<ReadLeave>();

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

        modelBuilder.Entity<LeaveRequest>(e =>
        {
            e.HasOne(lr => lr.User)
                .WithMany(u => u.LeaveRequests)
                .HasForeignKey(lr => lr.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(lr => new { lr.UserId, lr.StartDate });
            e.Property(lr => lr.Reason).HasMaxLength(500);
        });

        // --- Аудит (bounded-контекст audit) ---
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasIndex(a => a.RequestId);
            e.HasIndex(a => a.CreatedAt);
            e.Property(a => a.Action).HasMaxLength(32);
            e.Property(a => a.Detail).HasMaxLength(128);
        });

        // --- Read-модель календаря (bounded-контекст reporting) ---
        modelBuilder.Entity<ReadLeave>(e =>
        {
            e.ToTable("ReadLeaves");
            e.HasIndex(r => new { r.UserId, r.Status });
            e.HasIndex(r => new { r.StartDate, r.Status });
            e.Property(r => r.UserName).HasMaxLength(120);
        });
    }
}
