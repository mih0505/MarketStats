using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using MarketStatsViewer.Models;

namespace MarketStatsViewer.Services;

/// <summary>
/// Сервис локальной базы данных SQLite.
/// Отвечает за хранение минутных баров, индексацию, выборку исторических аналогов
/// и расчет статистических нормативов (бенчмарков) по времени суток.
/// </summary>
public class DatabaseService : IDisposable
{
    private readonly string _connectionString;
    private static readonly CultureInfo InvCulture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Инициализирует сервис базы данных. По умолчанию создает файл market_stats.db в папке приложения.
    /// </summary>
    /// <param name="dbPath">Пользовательский путь к файлу БД (опционально, для тестов).</param>
    public DatabaseService(string? dbPath = null)
    {
        var path = dbPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "market_stats.db");
        _connectionString = $"Data Source={path};Mode=ReadWriteCreate;Cache=Shared";
        Initialize();
    }

    /// <summary>
    /// Создает необходимые таблицы и индексы в SQLite, если они еще не существуют,
    /// и переводит БД в высокопроизводительный режим WAL (Write-Ahead Logging).
    /// </summary>
    public void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS minute_bars (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                sec_code TEXT NOT NULL,
                date TEXT NOT NULL,
                time TEXT NOT NULL,
                open REAL,
                high REAL,
                low REAL,
                close REAL,
                volume REAL,
                trades_count INTEGER,
                avg_trade_size REAL,
                turnover REAL,
                oi_first REAL,
                oi_last REAL,
                oi_change REAL,
                price_change REAL,
                price_change_pct REAL,
                price_range REAL,
                clv REAL,
                efficiency REAL,
                volume_per_step REAL,
                first_trade_time TEXT,
                last_trade_time TEXT,
                data_status TEXT,
                UNIQUE(session_id, sec_code, date, time)
            );

            CREATE INDEX IF NOT EXISTS idx_bars_sec_session ON minute_bars(sec_code, session_id);
            CREATE INDEX IF NOT EXISTS idx_bars_sec_time ON minute_bars(sec_code, time);
            CREATE INDEX IF NOT EXISTS idx_bars_sec_date_time ON minute_bars(sec_code, date, time);

            CREATE TABLE IF NOT EXISTS sync_state (
                file_path TEXT PRIMARY KEY,
                last_byte_offset INTEGER,
                last_line_count INTEGER,
                last_modified TEXT
            );
        ";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Выполняет пакетную вставку минутных баров в базу данных в рамках одной быстрой транзакции.
    /// Если бар с таким временем уже существует, он обновляется (upsert).
    /// </summary>
    /// <param name="bars">Коллекция баров для записи.</param>
    /// <returns>Количество успешно сохраненных записей.</returns>
    public int InsertMinuteBarsBatch(IEnumerable<MinuteBar> bars)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO minute_bars (
                session_id, sec_code, date, time,
                open, high, low, close, volume, trades_count, avg_trade_size, turnover,
                oi_first, oi_last, oi_change, price_change, price_change_pct, price_range,
                clv, efficiency, volume_per_step, first_trade_time, last_trade_time, data_status
            ) VALUES (
                $session_id, $sec_code, $date, $time,
                $open, $high, $low, $close, $volume, $trades_count, $avg_trade_size, $turnover,
                $oi_first, $oi_last, $oi_change, $price_change, $price_change_pct, $price_range,
                $clv, $efficiency, $volume_per_step, $first_trade_time, $last_trade_time, $data_status
            )
            ON CONFLICT(session_id, sec_code, date, time) DO UPDATE SET
                open = excluded.open,
                high = excluded.high,
                low = excluded.low,
                close = excluded.close,
                volume = excluded.volume,
                trades_count = excluded.trades_count,
                avg_trade_size = excluded.avg_trade_size,
                turnover = excluded.turnover,
                oi_first = excluded.oi_first,
                oi_last = excluded.oi_last,
                oi_change = excluded.oi_change,
                price_change = excluded.price_change,
                price_change_pct = excluded.price_change_pct,
                price_range = excluded.price_range,
                clv = excluded.clv,
                efficiency = excluded.efficiency,
                volume_per_step = excluded.volume_per_step,
                first_trade_time = excluded.first_trade_time,
                last_trade_time = excluded.last_trade_time,
                data_status = excluded.data_status;
        ";

        var pSession = cmd.Parameters.Add("$session_id", SqliteType.Text);
        var pSec = cmd.Parameters.Add("$sec_code", SqliteType.Text);
        var pDate = cmd.Parameters.Add("$date", SqliteType.Text);
        var pTime = cmd.Parameters.Add("$time", SqliteType.Text);

        var pOpen = cmd.Parameters.Add("$open", SqliteType.Real);
        var pHigh = cmd.Parameters.Add("$high", SqliteType.Real);
        var pLow = cmd.Parameters.Add("$low", SqliteType.Real);
        var pClose = cmd.Parameters.Add("$close", SqliteType.Real);
        var pVolume = cmd.Parameters.Add("$volume", SqliteType.Real);
        var pTrades = cmd.Parameters.Add("$trades_count", SqliteType.Integer);
        var pAvgSize = cmd.Parameters.Add("$avg_trade_size", SqliteType.Real);
        var pTurnover = cmd.Parameters.Add("$turnover", SqliteType.Real);

        var pOiFirst = cmd.Parameters.Add("$oi_first", SqliteType.Real);
        var pOiLast = cmd.Parameters.Add("$oi_last", SqliteType.Real);
        var pOiChange = cmd.Parameters.Add("$oi_change", SqliteType.Real);

        var pChange = cmd.Parameters.Add("$price_change", SqliteType.Real);
        var pChangePct = cmd.Parameters.Add("$price_change_pct", SqliteType.Real);
        var pRange = cmd.Parameters.Add("$price_range", SqliteType.Real);

        var pClv = cmd.Parameters.Add("$clv", SqliteType.Real);
        var pEff = cmd.Parameters.Add("$efficiency", SqliteType.Real);
        var pStep = cmd.Parameters.Add("$volume_per_step", SqliteType.Real);

        var pFirst = cmd.Parameters.Add("$first_trade_time", SqliteType.Text);
        var pLast = cmd.Parameters.Add("$last_trade_time", SqliteType.Text);
        var pStatus = cmd.Parameters.Add("$data_status", SqliteType.Text);

        int count = 0;
        foreach (var bar in bars)
        {
            pSession.Value = bar.SessionId;
            pSec.Value = bar.SecCode;
            pDate.Value = bar.Date;
            pTime.Value = bar.Time;

            pOpen.Value = bar.Open;
            pHigh.Value = bar.High;
            pLow.Value = bar.Low;
            pClose.Value = bar.Close;
            pVolume.Value = bar.Volume;
            pTrades.Value = bar.TradesCount;
            pAvgSize.Value = bar.AvgTradeSize;
            pTurnover.Value = bar.Turnover;

            pOiFirst.Value = bar.OpenInterestFirst.HasValue ? bar.OpenInterestFirst.Value : DBNull.Value;
            pOiLast.Value = bar.OpenInterestLast.HasValue ? bar.OpenInterestLast.Value : DBNull.Value;
            pOiChange.Value = bar.OpenInterestChange.HasValue ? bar.OpenInterestChange.Value : DBNull.Value;

            pChange.Value = bar.PriceChange;
            pChangePct.Value = bar.PriceChangePct;
            pRange.Value = bar.PriceRange;

            pClv.Value = bar.CLV.HasValue ? bar.CLV.Value : DBNull.Value;
            pEff.Value = bar.Efficiency.HasValue ? bar.Efficiency.Value : DBNull.Value;
            pStep.Value = bar.VolumePerPriceStep.HasValue ? bar.VolumePerPriceStep.Value : DBNull.Value;

            pFirst.Value = bar.FirstTradeTime;
            pLast.Value = bar.LastTradeTime;
            pStatus.Value = bar.DataStatus;

            cmd.ExecuteNonQuery();
            count++;
        }

        tx.Commit();
        return count;
    }

    /// <summary>
    /// Извлекает из базы данных все минутные бары для указанной сессии и инструмента,
    /// отсортированные по возрастанию времени.
    /// </summary>
    /// <param name="sessionId">Идентификатор сессии QUIK.</param>
    /// <param name="secCode">Код инструмента (например, "GDZ6").</param>
    public List<MinuteBar> GetSessionBars(string sessionId, string secCode)
    {
        var list = new List<MinuteBar>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT session_id, sec_code, date, time,
                   open, high, low, close, volume, trades_count, avg_trade_size, turnover,
                   oi_first, oi_last, oi_change, price_change, price_change_pct, price_range,
                   clv, efficiency, volume_per_step, first_trade_time, last_trade_time, data_status
            FROM minute_bars
            WHERE session_id = $session_id AND sec_code = $sec_code
            ORDER BY date ASC, time ASC;
        ";
        cmd.Parameters.AddWithValue("$session_id", sessionId);
        cmd.Parameters.AddWithValue("$sec_code", secCode);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var bar = ReadMinuteBar(reader);
            list.Add(bar);
        }

        return list;
    }

    /// <summary>
    /// Рассчитывает исторические нормативы для каждой минуты дня по выбранному инструменту
    /// на основе всех прошлых торговых сессий (исключая текущую, чтобы не искажать выборку).
    /// </summary>
    /// <param name="secCode">Код инструмента.</param>
    /// <param name="excludeSessionId">Текущая активная сессия, которую нужно исключить из исторической базы.</param>
    /// <returns>Словарь [время HH:mm:ss -> бенчмарк минуты].</returns>
    public Dictionary<string, IntradayBenchmark> GetHistoricalBenchmarks(string secCode, string excludeSessionId = "")
    {
        var dict = new Dictionary<string, IntradayBenchmark>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT time,
                   COUNT(DISTINCT session_id) AS sample_days,
                   AVG(volume) AS avg_volume,
                   AVG(trades_count) AS avg_trades,
                   AVG(avg_trade_size) AS avg_trade_size,
                   AVG(turnover) AS avg_turnover,
                   AVG(price_range) AS avg_range
            FROM minute_bars
            WHERE sec_code = $sec_code AND session_id != $exclude_session
            GROUP BY time
            ORDER BY time ASC;
        ";
        cmd.Parameters.AddWithValue("$sec_code", secCode);
        cmd.Parameters.AddWithValue("$exclude_session", excludeSessionId);

        double cumVol = 0;
        double cumTrades = 0;
        double cumTurnover = 0;
        double cumRange = 0;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var time = reader.GetString(0);
            var samples = reader.GetInt32(1);
            var avgVol = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2);
            var avgTrades = reader.IsDBNull(3) ? 0.0 : reader.GetDouble(3);
            var avgSize = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4);
            var avgTurnover = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5);
            var avgRange = reader.IsDBNull(6) ? 0.0 : reader.GetDouble(6);

            cumVol += avgVol;
            cumTrades += avgTrades;
            cumTurnover += avgTurnover;
            cumRange += avgRange;

            dict[time] = new IntradayBenchmark
            {
                Time = time,
                SampleDaysCount = samples,
                AvgVolume = avgVol,
                AvgTrades = avgTrades,
                AvgTradeSize = avgSize,
                AvgTurnover = avgTurnover,
                AvgRange = avgRange,
                AvgCumulativeVolume = cumVol,
                AvgCumulativeTrades = cumTrades,
                AvgCumulativeTurnover = cumTurnover,
                AvgCumulativeRange = cumRange
            };
        }

        return dict;
    }

    /// <summary>
    /// Рассчитывает общие статистические показатели за полный торговый день (среднесуточный объем, сделки, средний чек).
    /// Используется для сравнительного анализа: "сегодня проторговано X vs обычный день Y".
    /// </summary>
    /// <param name="secCode">Код инструмента.</param>
    /// <param name="excludeSessionId">Исключаемая текущая сессия.</param>
    /// <returns>Экземпляр DailyBenchmark с усредненными дневными показателями.</returns>
    public DailyBenchmark GetDailyBenchmark(string secCode, string excludeSessionId = "")
    {
        var benchmark = new DailyBenchmark { SecCode = secCode };
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT session_id,
                   SUM(volume) AS day_vol,
                   SUM(trades_count) AS day_trades,
                   SUM(turnover) AS day_turnover,
                   MAX(high) - MIN(low) AS day_range
            FROM minute_bars
            WHERE sec_code = $sec_code AND session_id != $exclude_session
            GROUP BY session_id
            HAVING COUNT(*) >= 20;
        ";
        cmd.Parameters.AddWithValue("$sec_code", secCode);
        cmd.Parameters.AddWithValue("$exclude_session", excludeSessionId);

        var dayVols = new List<double>();
        var dayTrades = new List<double>();
        var dayTurnovers = new List<double>();
        var dayRanges = new List<double>();

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            dayVols.Add(reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1));
            dayTrades.Add(reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2));
            dayTurnovers.Add(reader.IsDBNull(3) ? 0.0 : reader.GetDouble(3));
            dayRanges.Add(reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4));
        }

        if (dayVols.Count > 0)
        {
            benchmark.TotalDaysCount = dayVols.Count;
            benchmark.AvgFullDayVolume = Math.Round(dayVols.Average(), 0);
            benchmark.AvgFullDayTrades = Math.Round(dayTrades.Average(), 0);
            benchmark.AvgFullDayTurnover = Math.Round(dayTurnovers.Average(), 0);
            benchmark.AvgDayRange = Math.Round(dayRanges.Average(), 1);
            benchmark.TypicalAvgTradeSize = benchmark.AvgFullDayTrades > 0
                ? Math.Round(benchmark.AvgFullDayVolume / benchmark.AvgFullDayTrades, 2)
                : 1.0;
        }

        return benchmark;
    }

    /// <summary>
    /// Возвращает список всех уникальных кодов инструментов, имеющихся в базе данных.
    /// </summary>
    public List<string> GetAvailableSecCodes()
    {
        var list = new List<string>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT sec_code FROM minute_bars ORDER BY sec_code ASC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public List<string> GetAvailableSessions(string secCode = "")
    {
        var list = new List<string>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        if (string.IsNullOrEmpty(secCode))
        {
            cmd.CommandText = "SELECT DISTINCT session_id FROM minute_bars ORDER BY session_id DESC;";
        }
        else
        {
            cmd.CommandText = "SELECT DISTINCT session_id FROM minute_bars WHERE sec_code = $sec_code ORDER BY session_id DESC;";
            cmd.Parameters.AddWithValue("$sec_code", secCode);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public int GetTotalBarsCount()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM minute_bars;";
        var res = cmd.ExecuteScalar();
        return res != null ? Convert.ToInt32(res) : 0;
    }

    public (long offset, int lines) GetSyncState(string filePath)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_byte_offset, last_line_count FROM sync_state WHERE file_path = $path;";
        cmd.Parameters.AddWithValue("$path", filePath);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var offset = reader.GetInt64(0);
            var lines = reader.GetInt32(1);
            return (offset, lines);
        }
        return (0, 0);
    }

    public void SaveSyncState(string filePath, long offset, int lines, string modified)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sync_state(file_path, last_byte_offset, last_line_count, last_modified)
            VALUES($path, $offset, $lines, $mod)
            ON CONFLICT(file_path) DO UPDATE SET
                last_byte_offset = excluded.last_byte_offset,
                last_line_count = excluded.last_line_count,
                last_modified = excluded.last_modified;
        ";
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$offset", offset);
        cmd.Parameters.AddWithValue("$lines", lines);
        cmd.Parameters.AddWithValue("$mod", modified);
        cmd.ExecuteNonQuery();
    }

    private static MinuteBar ReadMinuteBar(SqliteDataReader reader)
    {
        return new MinuteBar
        {
            SessionId = reader.GetString(0),
            SecCode = reader.GetString(1),
            Date = reader.GetString(2),
            Time = reader.GetString(3),
            Open = reader.GetDouble(4),
            High = reader.GetDouble(5),
            Low = reader.GetDouble(6),
            Close = reader.GetDouble(7),
            Volume = reader.GetDouble(8),
            TradesCount = reader.GetInt32(9),
            AvgTradeSize = reader.GetDouble(10),
            Turnover = reader.GetDouble(11),
            OpenInterestFirst = reader.IsDBNull(12) ? null : reader.GetDouble(12),
            OpenInterestLast = reader.IsDBNull(13) ? null : reader.GetDouble(13),
            OpenInterestChange = reader.IsDBNull(14) ? null : reader.GetDouble(14),
            PriceChange = reader.GetDouble(15),
            PriceChangePct = reader.GetDouble(16),
            PriceRange = reader.GetDouble(17),
            CLV = reader.IsDBNull(18) ? null : reader.GetDouble(18),
            Efficiency = reader.IsDBNull(19) ? null : reader.GetDouble(19),
            VolumePerPriceStep = reader.IsDBNull(20) ? null : reader.GetDouble(20),
            FirstTradeTime = reader.IsDBNull(21) ? "" : reader.GetString(21),
            LastTradeTime = reader.IsDBNull(22) ? "" : reader.GetString(22),
            DataStatus = reader.IsDBNull(23) ? "OK" : reader.GetString(23)
        };
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
    }
}
