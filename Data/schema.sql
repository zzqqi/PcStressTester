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
