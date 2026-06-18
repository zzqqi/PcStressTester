using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using PcStressTester.Models;

namespace PcStressTester.Services;

public sealed class DatabaseService
{
    private const string DatabaseFileName = "pc_stress_tester.db";
    private readonly string _databasePath;
    private readonly object _sync = new();

    public DatabaseService(string? databasePath = null)
    {
        SQLitePCL.Batteries_V2.Init();

        _databasePath = databasePath ?? ResolveDefaultDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        Initialize();
    }

    public string GetDatabasePath() => _databasePath;

    public long StartTestRun(string testType, int? targetLoadPercent = null)
    {
        const string sql = """
            INSERT INTO test_runs (test_type, started_at, target_load_percent, status)
            VALUES ($test_type, $started_at, $target_load_percent, $status);
            SELECT last_insert_rowid();
            """;

        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$test_type", testType);
            command.Parameters.AddWithValue("$started_at", ToDatabaseDateTime(DateTime.Now));
            AddNullable(command, "$target_load_percent", targetLoadPercent);
            command.Parameters.AddWithValue("$status", "Выполняется");
            return (long)command.ExecuteScalar()!;
        }
    }

    public void FinishTestRun(long testRunId, string status, string? summary = null)
    {
        const string sql = """
            UPDATE test_runs
            SET finished_at = $finished_at,
                status = $status,
                summary = $summary
            WHERE id = $id;
            """;

        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", testRunId);
            command.Parameters.AddWithValue("$finished_at", ToDatabaseDateTime(DateTime.Now));
            command.Parameters.AddWithValue("$status", status);
            AddNullable(command, "$summary", summary);
            command.ExecuteNonQuery();
        }
    }

    public void SaveSensorSnapshot(long? testRunId, TestLogEntry entry, IEnumerable<SensorInfo> sensors)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            const string snapshotSql = """
                INSERT INTO sensor_snapshots
                (
                    test_run_id,
                    captured_at,
                    cpu_temp,
                    cpu_load,
                    cpu_clock,
                    gpu_temp,
                    gpu_load,
                    ram_used_gb,
                    status
                )
                VALUES
                (
                    $test_run_id,
                    $captured_at,
                    $cpu_temp,
                    $cpu_load,
                    $cpu_clock,
                    $gpu_temp,
                    $gpu_load,
                    $ram_used_gb,
                    $status
                );
                SELECT last_insert_rowid();
                """;

            using var snapshotCommand = connection.CreateCommand();
            snapshotCommand.Transaction = transaction;
            snapshotCommand.CommandText = snapshotSql;
            AddNullable(snapshotCommand, "$test_run_id", testRunId);
            snapshotCommand.Parameters.AddWithValue("$captured_at", ToDatabaseDateTime(entry.Time));
            AddNullable(snapshotCommand, "$cpu_temp", entry.CpuTemp);
            AddNullable(snapshotCommand, "$cpu_load", entry.CpuLoad);
            AddNullable(snapshotCommand, "$cpu_clock", entry.CpuClock);
            AddNullable(snapshotCommand, "$gpu_temp", entry.GpuTemp);
            AddNullable(snapshotCommand, "$gpu_load", entry.GpuLoad);
            AddNullable(snapshotCommand, "$ram_used_gb", entry.RamUsedGb);
            snapshotCommand.Parameters.AddWithValue("$status", entry.Status);
            var snapshotId = (long)snapshotCommand.ExecuteScalar()!;

            const string sensorSql = """
                INSERT INTO sensor_values
                (
                    snapshot_id,
                    hardware,
                    sensor_name,
                    sensor_type,
                    sensor_value,
                    captured_at
                )
                VALUES
                (
                    $snapshot_id,
                    $hardware,
                    $sensor_name,
                    $sensor_type,
                    $sensor_value,
                    $captured_at
                );
                """;

            foreach (var sensor in sensors)
            {
                using var sensorCommand = connection.CreateCommand();
                sensorCommand.Transaction = transaction;
                sensorCommand.CommandText = sensorSql;
                sensorCommand.Parameters.AddWithValue("$snapshot_id", snapshotId);
                sensorCommand.Parameters.AddWithValue("$hardware", sensor.Hardware);
                sensorCommand.Parameters.AddWithValue("$sensor_name", sensor.Name);
                sensorCommand.Parameters.AddWithValue("$sensor_type", sensor.Type);
                AddNullable(sensorCommand, "$sensor_value", sensor.Value);
                sensorCommand.Parameters.AddWithValue("$captured_at", ToDatabaseDateTime(sensor.Timestamp));
                sensorCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public void SaveTestMetric(long testRunId, string metricName, double value, string? unit = null)
    {
        const string sql = """
            INSERT INTO test_metrics (test_run_id, metric_name, metric_value, unit)
            VALUES ($test_run_id, $metric_name, $metric_value, $unit);
            """;

        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$test_run_id", testRunId);
            command.Parameters.AddWithValue("$metric_name", metricName);
            command.Parameters.AddWithValue("$metric_value", value);
            AddNullable(command, "$unit", unit);
            command.ExecuteNonQuery();
        }
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();
        MigrateRussianDatabaseValues(connection);
    }

    private static void MigrateRussianDatabaseValues(SqliteConnection connection)
    {
        ReplaceValue(connection, "test_runs", "status", "Running", "Выполняется");
        ReplaceValue(connection, "test_runs", "status", "Completed", "Завершен");
        ReplaceValue(connection, "test_runs", "status", "Stopped", "Остановлен");
        ReplaceValue(connection, "sensor_snapshots", "status", "Running", "Выполняется");
        ReplaceValue(connection, "sensor_snapshots", "status", "Completed", "Завершен");
        ReplaceValue(connection, "sensor_snapshots", "status", "Stopped", "Остановлен");

        ReplaceValue(connection, "test_metrics", "metric_name", "Duration", "Длительность");
        ReplaceValue(connection, "test_metrics", "metric_name", "CpuMaxTemperature", "Максимальная температура CPU");
        ReplaceValue(connection, "test_metrics", "metric_name", "CpuMaxLoad", "Пиковая загрузка CPU");
        ReplaceValue(connection, "test_metrics", "metric_name", "CpuAverageLoad", "Средняя загрузка CPU");
        ReplaceValue(connection, "test_metrics", "metric_name", "WorkerCount", "Количество потоков");
        ReplaceValue(connection, "test_metrics", "unit", "seconds", "секунды");
    }

    private static void ReplaceValue(SqliteConnection connection, string table, string column, string oldValue, string newValue)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {table}
            SET {column} = $new_value
            WHERE {column} = $old_value;
            """;
        command.Parameters.AddWithValue("$new_value", newValue);
        command.Parameters.AddWithValue("$old_value", oldValue);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static string ResolveDefaultDatabasePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PcStressTester.csproj")))
                return Path.Combine(directory.FullName, "Data", DatabaseFileName);

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Data", DatabaseFileName);
    }

    private static void AddNullable(SqliteCommand command, string parameterName, object? value)
    {
        command.Parameters.AddWithValue(parameterName, value ?? DBNull.Value);
    }

    private static string ToDatabaseDateTime(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private const string SchemaSql = """
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS test_runs
        (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            test_type TEXT NOT NULL,
            started_at TEXT NOT NULL,
            finished_at TEXT NULL,
            target_load_percent INTEGER NULL,
            status TEXT NOT NULL,
            summary TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS sensor_snapshots
        (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            test_run_id INTEGER NULL,
            captured_at TEXT NOT NULL,
            cpu_temp REAL NULL,
            cpu_load REAL NULL,
            cpu_clock REAL NULL,
            gpu_temp REAL NULL,
            gpu_load REAL NULL,
            ram_used_gb REAL NULL,
            status TEXT NOT NULL,
            FOREIGN KEY (test_run_id) REFERENCES test_runs(id)
        );

        CREATE TABLE IF NOT EXISTS sensor_values
        (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            snapshot_id INTEGER NOT NULL,
            hardware TEXT NOT NULL,
            sensor_name TEXT NOT NULL,
            sensor_type TEXT NOT NULL,
            sensor_value REAL NULL,
            captured_at TEXT NOT NULL,
            FOREIGN KEY (snapshot_id) REFERENCES sensor_snapshots(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS test_metrics
        (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            test_run_id INTEGER NOT NULL,
            metric_name TEXT NOT NULL,
            metric_value REAL NOT NULL,
            unit TEXT NULL,
            FOREIGN KEY (test_run_id) REFERENCES test_runs(id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS idx_sensor_snapshots_test_run_id
            ON sensor_snapshots(test_run_id);

        CREATE INDEX IF NOT EXISTS idx_sensor_snapshots_captured_at
            ON sensor_snapshots(captured_at);

        CREATE INDEX IF NOT EXISTS idx_sensor_values_snapshot_id
            ON sensor_values(snapshot_id);

        CREATE VIEW IF NOT EXISTS "запуски_тестов" AS
        SELECT
            id AS "идентификатор",
            test_type AS "тип_теста",
            started_at AS "начало",
            finished_at AS "завершение",
            target_load_percent AS "целевая_нагрузка_процентов",
            status AS "статус",
            summary AS "итог"
        FROM test_runs;

        CREATE VIEW IF NOT EXISTS "снимки_датчиков" AS
        SELECT
            id AS "идентификатор",
            test_run_id AS "идентификатор_теста",
            captured_at AS "время_снимка",
            cpu_temp AS "температура_cpu",
            cpu_load AS "загрузка_cpu",
            cpu_clock AS "частота_cpu",
            gpu_temp AS "температура_gpu",
            gpu_load AS "загрузка_gpu",
            ram_used_gb AS "использовано_ram_гб",
            status AS "статус"
        FROM sensor_snapshots;

        CREATE VIEW IF NOT EXISTS "значения_датчиков" AS
        SELECT
            id AS "идентификатор",
            snapshot_id AS "идентификатор_снимка",
            hardware AS "устройство",
            sensor_name AS "датчик",
            sensor_type AS "тип_датчика",
            sensor_value AS "значение",
            captured_at AS "время_снимка"
        FROM sensor_values;

        CREATE VIEW IF NOT EXISTS "метрики_тестов" AS
        SELECT
            id AS "идентификатор",
            test_run_id AS "идентификатор_теста",
            metric_name AS "метрика",
            metric_value AS "значение",
            unit AS "единица_измерения"
        FROM test_metrics;
        """;
}
