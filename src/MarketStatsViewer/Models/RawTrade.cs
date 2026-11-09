using System;

namespace MarketStatsViewer.Models;

/// <summary>
/// Представляет единичную совершенную тиковую сделку из файла Trades/*.csv.
/// </summary>
public class RawTrade
{
    /// <summary>
    /// Идентификатор торговой сессии QUIK.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Уникальный номер сделки на бирже.
    /// </summary>
    public long TradeNum { get; set; }

    /// <summary>
    /// Дата сделки ("YYYY-MM-DD").
    /// </summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// Время сделки ("HH:mm:ss").
    /// </summary>
    public string Time { get; set; } = string.Empty;

    /// <summary>
    /// Доли секунды (микросекунды/миллисекунды).
    /// </summary>
    public string Subsecond { get; set; } = string.Empty;

    /// <summary>
    /// Код инструмента (например, "GDZ6").
    /// </summary>
    public string SecCode { get; set; } = string.Empty;

    /// <summary>
    /// Цена совершения сделки.
    /// </summary>
    public double Price { get; set; }

    /// <summary>
    /// Объем сделки в контрактах.
    /// </summary>
    public double Qty { get; set; }

    /// <summary>
    /// Объем сделки в рублях.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Битовые флаги сделки из QUIK (1026 = покупка по рынку / Take Ask, 1025 = продажа по рынку / Hit Bid).
    /// </summary>
    public int Flags { get; set; }

    /// <summary>
    /// Значение открытого интереса на момент совершения этой сделки.
    /// </summary>
    public double? OpenInterest { get; set; }

    /// <summary>
    /// Определяет направление инициатора сделки по флагам QUIK.
    /// </summary>
    public bool IsBuy => (Flags & 0x2) != 0;

    /// <summary>
    /// Определяет продажу по рынку.
    /// </summary>
    public bool IsSell => (Flags & 0x1) != 0;

    /// <summary>
    /// Минутная корзина времени в формате "HH:mm:00".
    /// </summary>
    public string MinuteBucket => Time.Length >= 5 ? $"{Time[..5]}:00" : Time;
}
