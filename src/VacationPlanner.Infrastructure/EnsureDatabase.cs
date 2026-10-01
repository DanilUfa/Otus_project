using Npgsql;

namespace VacationPlanner.Infrastructure;

/// <summary>
/// Провижининг: если базы данных сервиса ещё нет на сервере — создаём её.
/// (В продакшене это делал бы мигратор/терраформ; здесь — минимальный bootstrap.)
/// </summary>
public static class EnsureDatabase
{
    public static void EnsureCreated(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var dbName = builder.Database;
        if (string.IsNullOrWhiteSpace(dbName)) return;

        builder.Database = "postgres"; // системная БД, всегда существует
        using var conn = new NpgsqlConnection(builder.ConnectionString);
        conn.Open();

        using var probe = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @db", conn);
        probe.Parameters.AddWithValue("db", dbName);
        if (probe.ExecuteScalar() is not null) return;

        // Имена БД фиксированы в конфигурации (vacation_audit / vacation_reporting) — безопасно.
        using var create = new NpgsqlCommand("CREATE DATABASE " + dbName, conn);
        create.ExecuteNonQuery();
    }
}
