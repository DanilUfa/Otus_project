using Npgsql;

namespace VacationPlanner.Infrastructure;

/// <summary>
/// Провижининг: создаёт БД сервиса (vacation_audit / vacation_reporting), если её нет,
/// и гарантирует наличие таблицы истории миграций EF Core.
/// (Нюанс: на пустой, но уже существующей БД EF/Npgsql пропускает создание
/// __EFMigrationsHistory — без неё первый Migrate падает; здесь таблица создаётся явно.)
/// </summary>
public static class EnsureDatabase
{
    public static void EnsureCreated(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var dbName = builder.Database;
        if (string.IsNullOrWhiteSpace(dbName)) return;

        // 1) Создаём БД, если её нет (подключение к системной БД postgres).
        builder.Database = "postgres";
        using (var admin = new NpgsqlConnection(builder.ConnectionString))
        {
            admin.Open();
            using var probe = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @db", admin);
            probe.Parameters.AddWithValue("db", dbName);
            if (probe.ExecuteScalar() is null)
            {
                using var create = new NpgsqlCommand("CREATE DATABASE " + dbName, admin);
                create.ExecuteNonQuery();
            }
        }

        // 2) Таблица истории миграций создаётся идемпотно на любой БД с правами на CREATE.
        EnsureHistoryTable(builder, dbName);
    }

    private static void EnsureHistoryTable(NpgsqlConnectionStringBuilder builder, string dbName)
    {
        builder.Database = dbName;
        using var conn = new NpgsqlConnection(builder.ConnectionString);
        conn.Open();
        using var cmd = new NpgsqlCommand(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
            "\"MigrationId\" text NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, " +
            "\"ProductVersion\" text NOT NULL)", conn);
        cmd.ExecuteNonQuery();
    }
}
