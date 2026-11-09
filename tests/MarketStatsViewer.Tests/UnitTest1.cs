using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using MarketStatsViewer.Models;
using MarketStatsViewer.Services;

namespace MarketStatsViewer.Tests;

public class MarketStatsTests
{
    [Fact]
    public void CsvParser_ParsesValidMinuteLine()
    {
        string line = "20261026_085521;2026-10-26;07:00:00;SPBFUT;GDZ6;GD;4056.4;4062.1;4055.3;4058.4;683;156;4.37820513;223212503.94;360188;359700;-488;2;0.0493048;6.8;-0.08823529;0.29411765;10.04411765;07:00:00;07:00:57;OK";
        var bar = CsvParser.ParseMinuteBar(line);

        Assert.NotNull(bar);
        Assert.Equal("20261026_085521", bar.SessionId);
        Assert.Equal("2026-10-26", bar.Date);
        Assert.Equal("07:00:00", bar.Time);
        Assert.Equal("GDZ6", bar.SecCode);
        Assert.Equal(4056.4, bar.Open);
        Assert.Equal(4062.1, bar.High);
        Assert.Equal(4055.3, bar.Low);
        Assert.Equal(4058.4, bar.Close);
        Assert.Equal(683, bar.Volume);
        Assert.Equal(156, bar.TradesCount);
        Assert.Equal(-488, bar.OpenInterestChange);
        Assert.Equal(6.8, bar.PriceRange, 2);
    }

    [Fact]
    public void DatabaseService_CanInsertAndRetrieveBars()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(tempDb);
            var bars = new List<MinuteBar>
            {
                new MinuteBar
                {
                    SessionId = "20261026_085521",
                    Date = "2026-10-26",
                    Time = "07:00:00",
                    SecCode = "GDZ6",
                    Open = 4050, High = 4055, Low = 4048, Close = 4054,
                    Volume = 100, TradesCount = 20, Turnover = 500000,
                    OpenInterestFirst = 300000, OpenInterestLast = 300050, OpenInterestChange = 50,
                    PriceChange = 4, PriceRange = 7
                },
                new MinuteBar
                {
                    SessionId = "20261026_085521",
                    Date = "2026-10-26",
                    Time = "07:01:00",
                    SecCode = "GDZ6",
                    Open = 4054, High = 4058, Low = 4053, Close = 4057,
                    Volume = 150, TradesCount = 35, Turnover = 750000,
                    OpenInterestFirst = 300050, OpenInterestLast = 300100, OpenInterestChange = 50,
                    PriceChange = 3, PriceRange = 5
                }
            };

            int inserted = db.InsertMinuteBarsBatch(bars);
            Assert.Equal(2, inserted);

            var retrieved = db.GetSessionBars("20261026_085521", "GDZ6");
            Assert.Equal(2, retrieved.Count);
            Assert.Equal(4050, retrieved[0].Open);
            Assert.Equal(4057, retrieved[1].Close);
        }
        finally
        {
            SafeDeleteDb(tempDb);
        }
    }

    [Fact]
    public void MarketAnalyticsService_EvaluatesEffortVsResultCorrectly()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(tempDb);
            var analytics = new MarketAnalyticsService(db);

            // Case: High volume with very narrow net movement despite active trading -> Absorption
            var bars = new List<MinuteBar>
            {
                new MinuteBar { Time = "10:00:00", Open = 4000.0, High = 4001.0, Low = 3998.0, Close = 3999.0, Volume = 500, PriceRange = 3.0, CLV = -0.3, BenchmarkAvgVolume = 100 },
                new MinuteBar { Time = "10:01:00", Open = 3999.0, High = 4000.5, Low = 3998.2, Close = 3999.5, Volume = 600, PriceRange = 2.3, CLV = 0.1, BenchmarkAvgVolume = 100 },
                new MinuteBar { Time = "10:02:00", Open = 3999.5, High = 4000.5, Low = 3998.0, Close = 4000.1, Volume = 700, PriceRange = 2.5, CLV = 0.6, BenchmarkAvgVolume = 100 }
            };

            var benchmarks = new Dictionary<string, IntradayBenchmark>
            {
                ["10:00:00"] = new IntradayBenchmark { AvgVolume = 100 },
                ["10:01:00"] = new IntradayBenchmark { AvgVolume = 100 },
                ["10:02:00"] = new IntradayBenchmark { AvgVolume = 100 }
            };

            var result = analytics.EvaluateEffortVsResult(bars, benchmarks);
            Assert.True(result.EffortScore > 1.5);
            Assert.Equal(EffortResultType.AbsorptionBullish, result.Type);
        }
        finally
        {
            SafeDeleteDb(tempDb);
        }
    }

    [Fact]
    public void MarketAnalyticsService_GeneratesDualForecasts()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(tempDb);
            var analytics = new MarketAnalyticsService(db);

            var bars = new List<MinuteBar>();
            for (int i = 0; i < 20; i++)
            {
                bars.Add(new MinuteBar
                {
                    Time = $"10:{i:D2}:00",
                    Open = 4000 + i * 0.5,
                    High = 4001 + i * 0.5,
                    Low = 3999 + i * 0.5,
                    Close = 4000.8 + i * 0.5,
                    Volume = 100 + i * 10,
                    TradesCount = 20,
                    PriceRange = 2.0,
                    PriceChangePct = 0.05,
                    OpenInterestChange = 15,
                    VWAP = 4005,
                    RVol = 1.3,
                    CumRVol = 1.2
                });
            }

            var effort = analytics.EvaluateEffortVsResult(bars, new Dictionary<string, IntradayBenchmark>());
            var (shortF, medF) = analytics.GenerateDualForecast(bars, "GDZ6", "test_session", effort);

            Assert.NotNull(shortF);
            Assert.NotNull(medF);
            Assert.Equal("Горизонт 5–15 минут", shortF.HorizonTitle);
            Assert.Equal("Горизонт 30–60 минут", medF.HorizonTitle);
            Assert.True(shortF.ProbUp > 0);
            Assert.True(shortF.ProbDown > 0);
            Assert.True(medF.ProbUp > 0);
            Assert.True(medF.ProbDown > 0);
            Assert.NotEmpty(shortF.KeyFactors);
            Assert.NotEmpty(medF.KeyFactors);
        }
        finally
        {
            SafeDeleteDb(tempDb);
        }
    }

    [Fact]
    public async Task DataSyncService_CanSyncRealSessions()
    {
        var sessionsPath = @"C:\QUIK_VTB\lua\MarketStats\Sessions";
        if (!Directory.Exists(sessionsPath)) return;

        var tempDb = Path.Combine(Path.GetTempPath(), $"test_sync_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(tempDb);
            using var syncer = new DataSyncService(db, sessionsPath);

            var sessions = syncer.DiscoverSessionDirectories();
            Assert.NotEmpty(sessions);

            // Sync all sessions
            int imported = await syncer.SyncAllHistoryAsync();
            Assert.True(imported > 0, $"Expected imported bars > 0, got {imported}");

            var secCodes = db.GetAvailableSecCodes();
            Assert.Contains("GDZ6", secCodes);

            var benchmarks = db.GetHistoricalBenchmarks("GDZ6");
            Assert.NotEmpty(benchmarks);
        }
        finally
        {
            SafeDeleteDb(tempDb);
        }
    }

    private static void SafeDeleteDb(string dbPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
        catch
        {
        }
    }
}

