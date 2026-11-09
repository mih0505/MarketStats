using System.Collections.Generic;

namespace MarketStatsViewer.Models;

/// <summary>
/// Прогнозная оценка вероятности и направления движения цены для конкретного временного горизонта (5-15 мин или 30-60 мин).
/// Сочетает статистический поиск исторических аналогов в SQLite и микроструктурный скоринг (VSA, ОИ, VWAP).
/// </summary>
public class HorizonForecast
{
    /// <summary>
    /// Заголовок временного горизонта ("Горизонт 5–15 минут" или "Горизонт 30–60 минут").
    /// </summary>
    public string HorizonTitle { get; set; } = string.Empty;

    /// <summary>
    /// Поясняющий подзаголовок горизонта (например, "Ближайший импульс / микроструктура").
    /// </summary>
    public string HorizonSubtitle { get; set; } = string.Empty;

    /// <summary>
    /// Итоговое преобладающее направление ("ВВЕРХ ▲", "ВНИЗ ▼" или "НЕЙТРАЛЬНО ▬").
    /// </summary>
    public string Direction { get; set; } = "НЕЙТРАЛЬНО";

    /// <summary>
    /// Цвет бейджа направления (зеленый #38A169, красный #E53E3E, нейтральный серый #718096).
    /// </summary>
    public string DirectionColor { get; set; } = "#718096";

    /// <summary>
    /// Вероятность движения цены вверх в процентах (0–100%).
    /// </summary>
    public double ProbUp { get; set; } = 33.3;

    /// <summary>
    /// Вероятность движения цены вниз в процентах (0–100%).
    /// </summary>
    public double ProbDown { get; set; } = 33.3;

    /// <summary>
    /// Вероятность удержания цены во флэте/диапазоне в процентах (0–100%).
    /// </summary>
    public double ProbNeutral { get; set; } = 33.4;

    /// <summary>
    /// Ожидаемый размах колебания цены в пунктах на этом горизонте.
    /// </summary>
    public double ExpectedPointsMove { get; set; }

    /// <summary>
    /// Ожидаемый размах колебания цены в процентах от текущей цены.
    /// </summary>
    public double ExpectedPctMove { get; set; }

    /// <summary>
    /// Сводное описание режима рынка для данного горизонта.
    /// </summary>
    public string RegimeSummary { get; set; } = string.Empty;

    /// <summary>
    /// Список ключевых рыночных факторов, определивших оценку (импульс, ОИ, VWAP, VSA).
    /// </summary>
    public List<string> KeyFactors { get; set; } = new();

    /// <summary>
    /// Количество похожих исторических ситуаций в базе данных, на которых базируется расчет.
    /// </summary>
    public int HistoricalMatchesCount { get; set; }

    /// <summary>
    /// Текстовое пояснение степени достоверности и источника выборки.
    /// </summary>
    public string ConfidenceText { get; set; } = "Аналитический ориентир";

    /// <summary>
    /// Форматированная строка вероятности роста ("XX.X%").
    /// </summary>
    public string FormattedProbUp => $"{ProbUp:F1}%";

    /// <summary>
    /// Форматированная строка вероятности падения ("XX.X%").
    /// </summary>
    public string FormattedProbDown => $"{ProbDown:F1}%";

    /// <summary>
    /// Форматированная строка вероятности флэта ("XX.X%").
    /// </summary>
    public string FormattedProbNeutral => $"{ProbNeutral:F1}%";

    /// <summary>
    /// Форматированная строка ожидаемого движения (например, "±2.5 п. (0.06%)").
    /// </summary>
    public string FormattedExpectedMove => ExpectedPointsMove > 0 
        ? $"±{ExpectedPointsMove:F1} п. ({ExpectedPctMove:F2}%)" 
        : "—";
}
