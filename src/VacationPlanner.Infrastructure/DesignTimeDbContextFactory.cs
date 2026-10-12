using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace VacationPlanner.Infrastructure;

/// <summary>
/// Design-time фабрики для dotnet-ef (все три контекста в одном файле).
/// Позволяют генерировать миграции, не запуская веб-приложение: EF использует
/// фабрику и не выполняет Program (стартовый проект не регистрирует audit/reporting).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                   ?? "Host=localhost;Database=vacation;Username=vac;Password=vac";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new AppDbContext(options);
    }
}

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
