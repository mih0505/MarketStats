namespace MarketStatsViewer.Models;

/// <summary>
/// Типы рыночных состояний по закону Вайкоффа «Усилие против Результата» (VSA - Volume Spread Analysis).
/// </summary>
public enum EffortResultType
{
    /// <summary>
    /// Нормальное соотношение объема и размаха цен в пределах статистической нормы.
    /// </summary>
    Normal,

    /// <summary>
    /// Бычье поглощение: огромный объем продаж встречает скрытый лимитный спрос; цена удерживается и закрывается вверху.
    /// </summary>
    AbsorptionBullish,

    /// <summary>
    /// Медвежье поглощение: крупный объем покупок не может сдвинуть цену вверх из-за скрытого лимитного предложения.
    /// </summary>
    AbsorptionBearish,

    /// <summary>
    /// Подтвержденный импульсный пробой: высокий объем поддержан широким размахом цены в направлении пробоя.
    /// </summary>
    BreakoutConfirmed,

    /// <summary>
    /// Кульминация объема (истощение): сверханомальный объем на экстремуме с откатной тенью, признак разворота.
    /// </summary>
    ClimaxExhaustion,

    /// <summary>
    /// Движение на тонком рынке: цена движется при объемах ниже нормы; высокий риск ложного движения.
    /// </summary>
    LowVolumeDrift,

    /// <summary>
    /// Консолидация / затишье: пониженный объем и узкий диапазон; набор энергии перед новым движением.
    /// </summary>
    Consolidation
}

/// <summary>
/// Результат оценки текущей рыночной ситуации по закону «Усилие против Результата».
/// </summary>
public class EffortResultAnalysis
{
    /// <summary>
    /// Классификация текущего паттерна VSA.
    /// </summary>
    public EffortResultType Type { get; set; } = EffortResultType.Normal;

    /// <summary>
    /// Название паттерна для заголовка бейджа (например, "Бычье поглощение продаж").
    /// </summary>
    public string Title { get; set; } = "Нормальный баланс";

    /// <summary>
    /// Краткая суть рыночной расстановки сил понятным языком.
    /// </summary>
    public string Summary { get; set; } = "Усилие и результат соответствуют типичным значениям.";

    /// <summary>
    /// Дополнительные числовые подробности (изменение ОИ, волатильность за окно анализа).
    /// </summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>
    /// Hex-код цвета плашки (зеленый, красный, оранжевый, серый).
    /// </summary>
    public string BadgeColor { get; set; } = "#718096";

    /// <summary>
    /// Оценка приложенного усилия (отношение объема последних минут к норме времени RVol).
    /// </summary>
    public double EffortScore { get; set; }

    /// <summary>
    /// Оценка полученного результата (размах движения цены в пунктах).
    /// </summary>
    public double ResultScore { get; set; }

    /// <summary>
    /// Коэффициент отдачи (Результат / Усилие).
    /// </summary>
    public double EfficiencyRatio { get; set; }

    /// <summary>
    /// Практическая торговая рекомендация для трейдера на основе VSA.
    /// </summary>
    public string Recommendation { get; set; } = "Следовать за трендом или выжидать.";
}
