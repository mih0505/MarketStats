namespace MarketStatsViewer.Models;

/// <summary>
/// Ценовой уровень горизонтального профиля объема (Volume Profile).
/// Показывает распределение проторгованного объема по ценам за торговый день.
/// </summary>
public class VolumeProfileLevel
{
    /// <summary>
    /// Ценовой уровень (с учетом шага цены).
    /// </summary>
    public double Price { get; set; }

    /// <summary>
    /// Суммарный объем проторгованных контрактов на данном ценовом уровне.
    /// </summary>
    public double Volume { get; set; }

    /// <summary>
    /// Флаг уровня максимального объема дня — POC (Point of Control).
    /// Самый важный уровень институциональной поддержки/сопротивления.
    /// </summary>
    public bool IsPOC { get; set; }

    /// <summary>
    /// Доля объема относительно максимального уровня дня (0.0 .. 1.0) для отрисовки полосы.
    /// </summary>
    public double RelativeWidth { get; set; }
}
