namespace MarketStatsViewer.Models;

/// <summary>
/// Статистический бенчмарк для конкретной минуты торгового дня.
/// Рассчитывается на основе истории предыдущих сессий для сравнения текущей активности с нормой.
/// </summary>
public class IntradayBenchmark
{
    /// <summary>
    /// Время минуты (формат "HH:mm:ss").
    /// </summary>
    public string Time { get; set; } = string.Empty;

    /// <summary>
    /// Количество торговых сессий в базе, попавших в расчет для этой минуты.
    /// </summary>
    public int SampleDaysCount { get; set; }

    /// <summary>
    /// Исторический средний объем контрактов за эту минуту.
    /// </summary>
    public double AvgVolume { get; set; }

    /// <summary>
    /// Историческое среднее число сделок за эту минуту.
    /// </summary>
    public double AvgTrades { get; set; }

    /// <summary>
    /// Исторический средний размер сделки (контрактов на сделку) в эту минуту.
    /// </summary>
    public double AvgTradeSize { get; set; }

    /// <summary>
    /// Исторический средний денежный оборот (в рублях) за эту минуту.
    /// </summary>
    public double AvgTurnover { get; set; }

    /// <summary>
    /// Исторический средний диапазон колебания цены (High - Low) за эту минуту.
    /// </summary>
    public double AvgRange { get; set; }

    /// <summary>
    /// Исторический средний накопленный объем контрактов от начала сессии до этой минуты.
    /// Используется для расчета кумулятивного показателя RVol (Relative Volume дня).
    /// </summary>
    public double AvgCumulativeVolume { get; set; }

    /// <summary>
    /// Историческое среднее накопленное число сделок от начала сессии до этой минуты.
    /// </summary>
    public double AvgCumulativeTrades { get; set; }

    /// <summary>
    /// Исторический средний накопленный оборот (в рублях) от начала сессии до этой минуты.
    /// </summary>
    public double AvgCumulativeTurnover { get; set; }

    /// <summary>
    /// Исторический средний накопленный размах цены от начала сессии до этой минуты.
    /// </summary>
    public double AvgCumulativeRange { get; set; }
}

/// <summary>
/// Общие статистические показатели за полный торговый день для инструмента.
/// Позволяет сопоставлять показатели текущего дня с типичным полным торговым днем.
/// </summary>
public class DailyBenchmark
{
    /// <summary>
    /// Код инструмента (например, "GDZ6").
    /// </summary>
    public string SecCode { get; set; } = string.Empty;

    /// <summary>
    /// Количество уникальных торговых сессий/дней в истории.
    /// </summary>
    public int TotalDaysCount { get; set; }

    /// <summary>
    /// Средний суммарный объем контрактов за всю торговую сессию.
    /// </summary>
    public double AvgFullDayVolume { get; set; }

    /// <summary>
    /// Среднее суммарное количество сделок за всю сессию.
    /// </summary>
    public double AvgFullDayTrades { get; set; }

    /// <summary>
    /// Средний денежный оборот за всю сессию в рублях.
    /// </summary>
    public double AvgFullDayTurnover { get; set; }

    /// <summary>
    /// Типичный средний размер одной сделки (контрактов на сделку) в истории.
    /// </summary>
    public double TypicalAvgTradeSize { get; set; }

    /// <summary>
    /// Средний диапазон дня (High дня минус Low дня).
    /// </summary>
    public double AvgDayRange { get; set; }
}
