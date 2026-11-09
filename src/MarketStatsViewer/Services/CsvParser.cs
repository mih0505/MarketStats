using System;
using System.Globalization;
using MarketStatsViewer.Models;

namespace MarketStatsViewer.Services;

/// <summary>
/// Высокопроизводительный парсер CSV-файлов минутных баров, генерируемых скриптом Stats.lua.
/// Корректно обрабатывает пустые поля, числовые форматы с точкой (InvariantCulture) и экранирование.
/// </summary>
public static class CsvParser
{
    private static readonly CultureInfo InvCulture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Разбирает одну текстовую строку CSV в объект MinuteBar.
    /// Пропускает заголовочные строки и возвращает null при поврежденных данных.
    /// </summary>
    /// <param name="line">Строка из файла Minutes/*.csv.</param>
    /// <param name="defaultSessionId">Идентификатор сессии по умолчанию (если отсутствует в строке).</param>
    /// <returns>Заполненный MinuteBar или null.</returns>
    public static MinuteBar? ParseMinuteBar(string line, string defaultSessionId = "")
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        // Skip header
        if (line.StartsWith("SessionId", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = line.Split(';');
        if (parts.Length < 14)
            return null;

        try
        {
            var bar = new MinuteBar
            {
                SessionId = GetString(parts, 0, defaultSessionId),
                Date = GetString(parts, 1),
                Time = GetString(parts, 2),
                ClassCode = GetString(parts, 3),
                SecCode = GetString(parts, 4),
                GroupCode = GetString(parts, 5),
                Open = ParseDouble(parts, 6) ?? 0.0,
                High = ParseDouble(parts, 7) ?? 0.0,
                Low = ParseDouble(parts, 8) ?? 0.0,
                Close = ParseDouble(parts, 9) ?? 0.0,
                Volume = ParseDouble(parts, 10) ?? 0.0,
                TradesCount = ParseInt(parts, 11) ?? 0,
                AvgTradeSize = ParseDouble(parts, 12) ?? 0.0,
                Turnover = ParseDouble(parts, 13) ?? 0.0,
                OpenInterestFirst = ParseDouble(parts, 14),
                OpenInterestLast = ParseDouble(parts, 15),
                OpenInterestChange = ParseDouble(parts, 16),
                PriceChange = ParseDouble(parts, 17) ?? 0.0,
                PriceChangePct = ParseDouble(parts, 18) ?? 0.0,
                PriceRange = ParseDouble(parts, 19) ?? 0.0,
                CLV = ParseDouble(parts, 20),
                Efficiency = ParseDouble(parts, 21),
                VolumePerPriceStep = ParseDouble(parts, 22),
                FirstTradeTime = GetString(parts, 23),
                LastTradeTime = GetString(parts, 24),
                DataStatus = GetString(parts, 25, "OK")
            };

            // Calculate derived range if missing or 0
            if (bar.PriceRange == 0 && bar.High > bar.Low)
            {
                bar.PriceRange = bar.High - bar.Low;
            }

            return bar;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Разбирает одну текстовую строку из файла Trades/*.csv в объект RawTrade.
    /// </summary>
    /// <param name="line">Строка со сделкой.</param>
    /// <param name="defaultSessionId">Идентификатор сессии.</param>
    /// <returns>Заполненный объект RawTrade или null.</returns>
    public static RawTrade? ParseRawTrade(string line, string defaultSessionId = "")
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        if (line.StartsWith("SessionId", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = line.Split(';');
        if (parts.Length < 10)
            return null;

        try
        {
            var trade = new RawTrade
            {
                SessionId = GetString(parts, 0, defaultSessionId),
                TradeNum = ParseLong(parts, 1) ?? 0,
                Date = GetString(parts, 2),
                Time = GetString(parts, 3),
                Subsecond = GetString(parts, 4),
                SecCode = GetString(parts, 6),
                Price = ParseDouble(parts, 8) ?? 0.0,
                Qty = ParseDouble(parts, 9) ?? 0.0,
                Value = ParseDouble(parts, 10) ?? 0.0,
                Flags = ParseInt(parts, 11) ?? 0,
                OpenInterest = ParseDouble(parts, 12)
            };

            if (trade.Price <= 0 || trade.Qty <= 0)
                return null;

            return trade;
        }
        catch
        {
            return null;
        }
    }

    private static long? ParseLong(string[] parts, int index)
    {
        if (index >= parts.Length) return null;
        var s = parts[index].Trim().Trim('"');
        if (string.IsNullOrEmpty(s)) return null;

        if (long.TryParse(s, NumberStyles.Integer, InvCulture, out var val))
            return val;

        return null;
    }

    private static string GetString(string[] parts, int index, string def = "")
    {
        if (index >= parts.Length) return def;
        var s = parts[index].Trim().Trim('"');
        return string.IsNullOrEmpty(s) ? def : s;
    }

    private static double? ParseDouble(string[] parts, int index)
    {
        if (index >= parts.Length) return null;
        var s = parts[index].Trim().Trim('"');
        if (string.IsNullOrEmpty(s)) return null;

        if (double.TryParse(s, NumberStyles.Float, InvCulture, out var val))
            return val;

        // Try comma if culture quirks occur
        if (double.TryParse(s.Replace(',', '.'), NumberStyles.Float, InvCulture, out val))
            return val;

        return null;
    }

    private static int? ParseInt(string[] parts, int index)
    {
        if (index >= parts.Length) return null;
        var s = parts[index].Trim().Trim('"');
        if (string.IsNullOrEmpty(s)) return null;

        if (int.TryParse(s, NumberStyles.Integer, InvCulture, out var val))
            return val;

        if (double.TryParse(s, NumberStyles.Float, InvCulture, out var dVal))
            return (int)dVal;

        return null;
    }
}
