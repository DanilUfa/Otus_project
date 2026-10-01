using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VacationPlanner.Infrastructure;

// Дизайн-тайм фабрики нужны dotnet-ef: стартовый проект (Web) не регистрирует
// контексты audit/reporting, поэтому EF создаёт их через фабрику.

public class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=vacation_audit;Username=vac;Password=vac")
            .Options;
        return new AuditDbContext(options);
    }
}

public class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=vacation_reporting;Username=vac;Password=vac")
            .Options;
        return new ReportingDbContext(options);
    }
}
