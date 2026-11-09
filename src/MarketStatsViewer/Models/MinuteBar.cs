using System;

namespace MarketStatsViewer.Models;

/// <summary>
/// Представляет одноминутный торговый бар со статистическими и микроструктурными показателями.
/// Формируется скриптом Stats.lua в QUIK и дополняется аналитическими вычислениями в C#.
/// </summary>
public class MinuteBar
{
    /// <summary>
    /// Идентификатор сессии сбора данных в формате "YYYYMMDD_HHMMSS".
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Торговая дата бара ("YYYY-MM-DD").
    /// </summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// Биржевое время начала минутной свечи ("HH:mm:ss").
    /// </summary>
    public string Time { get; set; } = string.Empty;

    /// <summary>
    /// Код класса инструмента (для срочного рынка Московской биржи обычно "SPBFUT").
    /// </summary>
    public string ClassCode { get; set; } = string.Empty;

    /// <summary>
    /// Код инструмента (например, "GDZ6", "BRX6", "SiZ6").
    /// </summary>
    public string SecCode { get; set; } = string.Empty;

    /// <summary>
    /// Код группы инструмента (например, "GD" для золота, "BR" для нефти).
    /// </summary>
    public string GroupCode { get; set; } = string.Empty;

    /// <summary>
    /// Цена открытия минуты.
    /// </summary>
    public double Open { get; set; }

    /// <summary>
    /// Максимальная цена за минуту.
    /// </summary>
    public double High { get; set; }

    /// <summary>
    /// Минимальная цена за минуту.
    /// </summary>
    public double Low { get; set; }

    /// <summary>
    /// Цена закрытия (последняя сделка в минуте).
    /// </summary>
    public double Close { get; set; }

    /// <summary>
    /// Проторгованный объем в контрактах за минуту.
    /// </summary>
    public double Volume { get; set; }

    /// <summary>
    /// Количество совершенных сделок за минуту.
    /// </summary>
    public int TradesCount { get; set; }

    /// <summary>
    /// Средний размер одной сделки за минуту (Volume / TradesCount).
    /// </summary>
    public double AvgTradeSize { get; set; }

    /// <summary>
    /// Денежный оборот за минуту в рублях.
    /// </summary>
    public double Turnover { get; set; }

    /// <summary>
    /// Значение открытого интереса на первой сделке минуты.
    /// </summary>
    public double? OpenInterestFirst { get; set; }

    /// <summary>
    /// Значение открытого интереса на последней сделке минуты.
    /// </summary>
    public double? OpenInterestLast { get; set; }

    /// <summary>
    /// Изменение открытого интереса за минуту (OpenInterestLast - OpenInterestFirst).
    /// </summary>
    public double? OpenInterestChange { get; set; }

    /// <summary>
    /// Изменение цены за минуту в пунктах (Close - Open).
    /// </summary>
    public double PriceChange { get; set; }

    /// <summary>
    /// Процентное изменение цены за минуту.
    /// </summary>
    public double PriceChangePct { get; set; }

    /// <summary>
    /// Размах колебания цены за минуту (High - Low).
    /// </summary>
    public double PriceRange { get; set; }

    /// <summary>
    /// Close Location Value: относительное положение закрытия бара в диапазоне от -1 (на самом Low) до +1 (на самом High).
    /// Показывает, кто выиграл борьбу к концу минуты: покупатели (> 0) или продавцы (< 0).
    /// </summary>
    public double? CLV { get; set; }

    /// <summary>
    /// Эффективность движения: |Close - Open| / (High - Low). Показывает, насколько движение было направленным, а не пилообразным.
    /// </summary>
    public double? Efficiency { get; set; }

    /// <summary>
    /// Плотность проторговки: объем контрактов на один шаг цены.
    /// </summary>
    public double? VolumePerPriceStep { get; set; }

    /// <summary>
    /// Время первой сделки в минуте ("HH:mm:ss").
    /// </summary>
    public string FirstTradeTime { get; set; } = string.Empty;

    /// <summary>
    /// Время последней сделки в минуте ("HH:mm:ss").
    /// </summary>
    public string LastTradeTime { get; set; } = string.Empty;

    /// <summary>
    /// Статус целостности данных ("OK" или "GAP" при разрывах связи).
    /// </summary>
    public string DataStatus { get; set; } = "OK";

    // --- Расчетные накопительные свойства (вычисляются в C#) ---

    /// <summary>
    /// Сокращенное время ("HH:mm") для заголовков и графиков.
    /// </summary>
    public string ShortTime => Time.Length >= 5 ? Time[..5] : Time;

    /// <summary>
    /// Накопленный объем контрактов с начала текущей торговой сессии до этого бара включительно.
    /// </summary>
    public double CumulativeVolume { get; set; }

    /// <summary>
    /// Накопленное количество сделок с начала текущей сессии.
    /// </summary>
    public int CumulativeTrades { get; set; }

    /// <summary>
    /// Накопленный оборот в рублях с начала сессии.
    /// </summary>
    public double CumulativeTurnover { get; set; }

    /// <summary>
    /// Накопленное изменение открытого интереса с начала сессии.
    /// </summary>
    public double CumulativeOiChange { get; set; }

    /// <summary>
    /// Дневная средневзвешенная по объему цена (Volume Weighted Average Price) до этой минуты.
    /// Важнейший институциональный уровень справедливой стоимости.
    /// </summary>
    public double VWAP { get; set; }

    // --- Сравнение с исторической нормой времени ---

    /// <summary>
    /// Исторический средний объем для этой конкретной минуты дня.
    /// </summary>
    public double? BenchmarkAvgVolume { get; set; }

    /// <summary>
    /// RVol (Relative Volume): отношение объема текущей минуты к исторической норме для этого времени (например, 2.5x).
    /// </summary>
    public double RVol { get; set; } = 1.0;

    /// <summary>
    /// Кумулятивный RVol: отношение накопленного объема за день к норме накопленного объема на это время.
    /// </summary>
    public double CumRVol { get; set; } = 1.0;

    /// <summary>
    /// Текстовая метка классификации VSA ("Поглощение", "Импульс", "Баланс" и т.д.).
    /// </summary>
    public string EffortResultTag { get; set; } = string.Empty;

    /// <summary>
    /// Цвет бейджа VSA для таблицы.
    /// </summary>
    public string EffortResultColor { get; set; } = "#888888";
}
