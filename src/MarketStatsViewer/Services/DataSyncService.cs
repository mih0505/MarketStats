using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MarketStatsViewer.Models;

namespace MarketStatsViewer.Services;

/// <summary>
/// Сервис синхронизации данных между CSV-файлами QUIK и локальной базой данных SQLite.
/// Поддерживает пакетный импорт исторических сессий и неблокирующее чтение дописываемых файлов
/// в реальном времени во время биржевой сессии.
/// </summary>
public class DataSyncService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly string _sessionsRoot;
    private Timer? _liveTimer;
    private readonly object _syncLock = new();
    private bool _isScanning = false;

    /// <summary>
    /// Событие, вызываемое при получении новых баров из файлов QUIK: (sessionId, secCode).
    /// </summary>
    public event Action<string, string>? BarsUpdated;

    /// <summary>
    /// Событие обновления текущей формирующейся минуты из тикового потока Trades/*.csv: (sessionId, secCode, liveBar).
    /// Срабатывает каждые 2 секунды, исключая задержку ожидания закрытия минуты.
    /// </summary>
    public event Action<string, string, MinuteBar>? LiveBarUpdated;

    /// <summary>
    /// Событие изменения статуса синхронизации (для отображения в UI).
    /// </summary>
    public event Action<string>? StatusChanged;

    private readonly Dictionary<string, long> _tradeFileOffsets = new();
    private readonly Dictionary<string, MinuteBar> _activeMinuteBars = new();

    /// <summary>
    /// Инициализирует сервис синхронизации.
    /// </summary>
    /// <param name="db">Сервис базы данных SQLite.</param>
    /// <param name="sessionsRoot">Корневая папка с сессиями (по умолчанию C:\QUIK_VTB\lua\MarketStats\Sessions).</param>
    public DataSyncService(DatabaseService db, string? sessionsRoot = null)
    {
        _db = db;
        _sessionsRoot = sessionsRoot ?? FindDefaultSessionsRoot();
    }

    private static string FindDefaultSessionsRoot()
    {
        // 1. Current directory / Sessions
        var candidate1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sessions");
        if (Directory.Exists(candidate1)) return candidate1;

        // 2. Parent directory / Sessions (when running from App/)
        var candidateParent = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Sessions"));
        if (Directory.Exists(candidateParent)) return candidateParent;

        // 3. Relative from project bin/Debug/net9.0-windows
        var candidate2 = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "Sessions"));
        if (Directory.Exists(candidate2)) return candidate2;

        // 4. Absolute standard path
        var candidate3 = @"C:\QUIK_VTB\lua\MarketStats\Sessions";
        if (Directory.Exists(candidate3)) return candidate3;

        return candidate3;
    }

    /// <summary>
    /// Полный путь к корневой директории сессий.
    /// </summary>
    public string SessionsRoot => _sessionsRoot;

    /// <summary>
    /// Сканирует диск и возвращает список всех существующих папок сессий в порядке убывания даты.
    /// </summary>
    public List<string> DiscoverSessionDirectories()
    {
        if (!Directory.Exists(_sessionsRoot))
            return new List<string>();

        return Directory.GetDirectories(_sessionsRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .OrderByDescending(name => name)
            .ToList()!;
    }

    /// <summary>
    /// Возвращает идентификатор самой свежей сессии QUIK на диске.
    /// </summary>
    public string? GetLatestSessionId()
    {
        return DiscoverSessionDirectories().FirstOrDefault();
    }

    /// <summary>
    /// Асинхронно сканирует и импортирует все исторические сессии в базу данных.
    /// </summary>
    /// <param name="progress">Интерфейс уведомления о прогрессе.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Общее количество загруженных баров.</returns>
    public async Task<int> SyncAllHistoryAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var sessions = DiscoverSessionDirectories();
            int totalNewBars = 0;
            int current = 0;

            progress?.Report($"Начало синхронизации: найдено сессий {sessions.Count}...");

            foreach (var session in sessions)
            {
                if (ct.IsCancellationRequested) break;
                current++;
                progress?.Report($"Обработка сессии [{current}/{sessions.Count}]: {session}...");

                var sessionDir = Path.Combine(_sessionsRoot, session);
                var minutesDir = Path.Combine(sessionDir, "Minutes");
                if (!Directory.Exists(minutesDir)) continue;

                var csvFiles = Directory.GetFiles(minutesDir, "*.csv");
                foreach (var file in csvFiles)
                {
                    int imported = IngestMinuteFile(file, session);
                    totalNewBars += imported;
                }
            }

            progress?.Report($"Синхронизация завершена. Загружено баров: {totalNewBars}.");
            return totalNewBars;
        }, ct);
    }

    /// <summary>
    /// Считывает только новые строки из файла Minutes/*.csv, начиная с последней сохраненной позиции байта.
    /// Использует неблокирующий режим FileShare.ReadWrite, что позволяет безопасно читать файл прямо во время записи скриптом QUIK.
    /// </summary>
    /// <param name="filePath">Путь к файлу минутных данных.</param>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <returns>Количество прочитанных и сохраненных в БД баров.</returns>
    public int IngestMinuteFile(string filePath, string sessionId)
    {
        if (!File.Exists(filePath)) return 0;

        lock (_syncLock)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var (lastOffset, lastLines) = _db.GetSyncState(filePath);

                // If file was truncated or replaced, reset offset
                if (fileInfo.Length < lastOffset)
                {
                    lastOffset = 0;
                    lastLines = 0;
                }

                if (fileInfo.Length == lastOffset)
                    return 0; // Nothing new

                var newBars = new List<MinuteBar>();
                long newOffset = lastOffset;
                int lineCount = lastLines;

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(lastOffset, SeekOrigin.Begin);
                    using var reader = new StreamReader(fs, Encoding.UTF8);

                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lineCount++;
                        // If starting from 0, skip header line
                        if (lineCount == 1 && line.StartsWith("SessionId", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var bar = CsvParser.ParseMinuteBar(line, sessionId);
                        if (bar != null)
                        {
                            newBars.Add(bar);
                        }
                    }

                    newOffset = fs.Position;
                }

                int savedCount = 0;
                if (newBars.Count > 0)
                {
                    savedCount = _db.InsertMinuteBarsBatch(newBars);
                }

                _db.SaveSyncState(filePath, newOffset, lineCount, fileInfo.LastWriteTimeUtc.ToString("o"));
                return savedCount;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Ошибка чтения файла {Path.GetFileName(filePath)}: {ex.Message}");
                return 0;
            }
        }
    }

    /// <summary>
    /// Запускает фоновый таймер опроса файлов текущей сессии на появление новых данных.
    /// </summary>
    /// <param name="interval">Интервал проверки (обычно 1.5–2 секунды).</param>
    public void StartLiveWatch(TimeSpan interval)
    {
        StopLiveWatch();
        _liveTimer = new Timer(OnLiveTimerTick, null, TimeSpan.Zero, interval);
        StatusChanged?.Invoke("Мониторинг реального времени запущен.");
    }

    /// <summary>
    /// Останавливает таймер опроса файлов.
    /// </summary>
    public void StopLiveWatch()
    {
        _liveTimer?.Dispose();
        _liveTimer = null;
        StatusChanged?.Invoke("Мониторинг реального времени остановлен.");
    }

    private void OnLiveTimerTick(object? state)
    {
        if (_isScanning) return;
        _isScanning = true;

        try
        {
            var latestSession = GetLatestSessionId();
            if (string.IsNullOrEmpty(latestSession)) return;

            var minutesDir = Path.Combine(_sessionsRoot, latestSession, "Minutes");
            if (!Directory.Exists(minutesDir)) return;

            var files = Directory.GetFiles(minutesDir, "*.csv");
            foreach (var file in files)
            {
                var secCode = Path.GetFileNameWithoutExtension(file);
                int newBars = IngestMinuteFile(file, latestSession);
                if (newBars > 0)
                {
                    BarsUpdated?.Invoke(latestSession, secCode);
                }
            }

            // 2. Опрос свежих тиков из Trades/*.csv для оперативного обновления текущей минуты
            var tradesDir = Path.Combine(_sessionsRoot, latestSession, "Trades");
            if (Directory.Exists(tradesDir))
            {
                var tradeFiles = Directory.GetFiles(tradesDir, "*.csv");
                foreach (var tradeFile in tradeFiles)
                {
                    var secCode = Path.GetFileNameWithoutExtension(tradeFile);
                    ProcessLiveTrades(latestSession, secCode);
                }
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Ошибка мониторинга: {ex.Message}");
        }
        finally
        {
            _isScanning = false;
        }
    }

    /// <summary>
    /// Считывает только новые тиковые сделки из Trades/[secCode].csv
    /// и агрегирует их в «живую» текущую минуту в оперативной памяти.
    /// </summary>
    public void ProcessLiveTrades(string latestSession, string secCode)
    {
        var tradesFile = Path.Combine(_sessionsRoot, latestSession, "Trades", $"{secCode}.csv");
        if (!File.Exists(tradesFile)) return;

        try
        {
            var fileInfo = new FileInfo(tradesFile);
            string key = $"{latestSession}_{secCode}";

            if (!_tradeFileOffsets.TryGetValue(key, out long offset))
            {
                // При первом запуске отступаем на последние ~60 КБ (хватает с запасом для сбора текущей минуты),
                // чтобы не разбирать десятки мегабайт истории сделок с открытия
                offset = Math.Max(0, fileInfo.Length - 60000);
            }

            if (fileInfo.Length <= offset) return;

            using (var fs = new FileStream(tradesFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(fs, Encoding.UTF8);

                // Если прыгнули в середину файла, отбрасываем первую неполную строку
                if (offset > 0)
                {
                    reader.ReadLine();
                }

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trade = CsvParser.ParseRawTrade(line, latestSession);
                    if (trade == null) continue;

                    if (!_activeMinuteBars.TryGetValue(key, out var liveBar) || liveBar.Time != trade.MinuteBucket)
                    {
                        liveBar = new MinuteBar
                        {
                            SessionId = latestSession,
                            SecCode = secCode,
                            Date = trade.Date,
                            Time = trade.MinuteBucket,
                            Open = trade.Price,
                            High = trade.Price,
                            Low = trade.Price,
                            Close = trade.Price,
                            Volume = trade.Qty,
                            TradesCount = 1,
                            Turnover = trade.Value,
                            AvgTradeSize = trade.Qty,
                            PriceChange = 0,
                            PriceRange = 0,
                            OpenInterestFirst = trade.OpenInterest,
                            OpenInterestLast = trade.OpenInterest,
                            OpenInterestChange = 0,
                            FirstTradeTime = trade.Time,
                            LastTradeTime = trade.Time,
                            DataStatus = "LIVE"
                        };
                        _activeMinuteBars[key] = liveBar;
                    }
                    else
                    {
                        if (trade.Price > liveBar.High) liveBar.High = trade.Price;
                        if (trade.Price < liveBar.Low) liveBar.Low = trade.Price;
                        liveBar.Close = trade.Price;
                        liveBar.Volume += trade.Qty;
                        liveBar.TradesCount++;
                        liveBar.Turnover += trade.Value;
                        liveBar.AvgTradeSize = liveBar.Volume / liveBar.TradesCount;
                        liveBar.PriceRange = liveBar.High - liveBar.Low;
                        liveBar.PriceChange = liveBar.Close - liveBar.Open;
                        liveBar.PriceChangePct = liveBar.Open > 0 ? (liveBar.PriceChange / liveBar.Open) * 100.0 : 0;
                        liveBar.LastTradeTime = trade.Time;

                        if (trade.OpenInterest.HasValue)
                        {
                            liveBar.OpenInterestLast = trade.OpenInterest;
                            if (liveBar.OpenInterestFirst.HasValue)
                            {
                                liveBar.OpenInterestChange = liveBar.OpenInterestLast - liveBar.OpenInterestFirst;
                            }
                        }

                        if (liveBar.PriceRange > 0)
                        {
                            liveBar.CLV = (2 * liveBar.Close - liveBar.High - liveBar.Low) / liveBar.PriceRange;
                            liveBar.Efficiency = Math.Abs(liveBar.PriceChange) / liveBar.PriceRange;
                        }
                    }
                }

                _tradeFileOffsets[key] = fs.Position;
            }

            if (_activeMinuteBars.TryGetValue(key, out var currentLiveBar))
            {
                LiveBarUpdated?.Invoke(latestSession, secCode, currentLiveBar);
            }
        }
        catch
        {
            // Неблокирующий перехват исключений при одновременной дозаписи
        }
    }

    public void Dispose()
    {
        StopLiveWatch();
    }
}
