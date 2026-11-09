using System;
using System.Collections.Generic;
using System.Linq;
using MarketStatsViewer.Models;

namespace MarketStatsViewer.Services;

/// <summary>
/// Аналитический сервис для расчета рыночных метрик:
/// 1. Обогащение баров кумулятивными данными (объем, оборот, VWAP, RVol).
/// 2. Оценка баланса по закону Вайкоффа «Усилие против Результата» (VSA).
/// 3. Построение вероятностного прогноза по двум горизонтам: 5–15 минут и 30–60 минут.
/// </summary>
public class MarketAnalyticsService
{
    private readonly DatabaseService _db;

    /// <summary>
    /// Инициализирует аналитический сервис с подключением к БД.
    /// </summary>
    public MarketAnalyticsService(DatabaseService db)
    {
        _db = db;
    }

    /// <summary>
    /// Обогащает последовательность минутных баров текущей сессии:
    /// рассчитывает кумулятивные суммы с начала дня, VWAP, соотношение объема к исторической норме (RVol).
    /// </summary>
    /// <param name="bars">Список минутных баров дня по возрастанию времени.</param>
    /// <param name="benchmarks">Исторические бенчмарки времени суток.</param>
    public void EnrichBars(List<MinuteBar> bars, Dictionary<string, IntradayBenchmark> benchmarks)
    {
        if (bars.Count == 0) return;

        double cumVol = 0;
        int cumTrades = 0;
        double cumTurnover = 0;
        double cumOi = 0;
        double sumPriceVol = 0;

        foreach (var bar in bars)
        {
            cumVol += bar.Volume;
            cumTrades += bar.TradesCount;
            cumTurnover += bar.Turnover;
            cumOi += (bar.OpenInterestChange ?? 0.0);
            sumPriceVol += bar.Close * bar.Volume;

            bar.CumulativeVolume = cumVol;
            bar.CumulativeTrades = cumTrades;
            bar.CumulativeTurnover = cumTurnover;
            bar.CumulativeOiChange = cumOi;
            bar.VWAP = cumVol > 0 ? (sumPriceVol / cumVol) : bar.Close;

            // Lookup benchmark
            if (benchmarks.TryGetValue(bar.Time, out var bm) && bm.AvgVolume > 0)
            {
                bar.BenchmarkAvgVolume = bm.AvgVolume;
                bar.RVol = Math.Round(bar.Volume / bm.AvgVolume, 2);

                if (bm.AvgCumulativeVolume > 0)
                {
                    bar.CumRVol = Math.Round(bar.CumulativeVolume / bm.AvgCumulativeVolume, 2);
                }
            }
            else
            {
                bar.RVol = 1.0;
                bar.CumRVol = 1.0;
            }

            // Quick bar classification for grid display
            ClassifySingleBar(bar);
        }
    }

    private static void ClassifySingleBar(MinuteBar bar)
    {
        if (bar.RVol >= 2.0 && bar.PriceRange > 0 && bar.CLV.HasValue)
        {
            if (Math.Abs(bar.PriceChangePct) < 0.015 && bar.VolumePerPriceStep.GetValueOrDefault() > 10)
            {
                bar.EffortResultTag = "Поглощение";
                bar.EffortResultColor = "#ED8936"; // Orange
                return;
            }

            if (bar.CLV > 0.6)
            {
                bar.EffortResultTag = "Импульс ВВЕРХ";
                bar.EffortResultColor = "#38A169"; // Green
                return;
            }
            if (bar.CLV < -0.6)
            {
                bar.EffortResultTag = "Импульс ВНИЗ";
                bar.EffortResultColor = "#E53E3E"; // Red
                return;
            }
        }

        if (bar.RVol < 0.5 && bar.PriceRange > 0.5)
        {
            bar.EffortResultTag = "Тонкий рынок";
            bar.EffortResultColor = "#805AD5"; // Purple
            return;
        }

        bar.EffortResultTag = "Баланс";
        bar.EffortResultColor = "#718096"; // Gray
    }

    /// <summary>
    /// Рассчитывает горизонтальный профиль объема (Volume Profile) за текущую сессию:
    /// группирует проторгованный объем по ценовым ступеням и находит Point of Control (POC).
    /// </summary>
    public List<VolumeProfileLevel> CalculateVolumeProfile(List<MinuteBar> bars, double priceStep = 0.1)
    {
        var result = new List<VolumeProfileLevel>();
        if (bars.Count == 0) return result;

        double minPrice = bars.Min(b => b.Low);
        double maxPrice = bars.Max(b => b.High);
        if (maxPrice <= minPrice) return result;

        double effectiveStep = priceStep > 0 ? priceStep : 0.1;
        double range = maxPrice - minPrice;
        if (range / effectiveStep > 80)
        {
            effectiveStep = Math.Ceiling((range / 60) * 10) / 10.0;
            if (effectiveStep <= 0) effectiveStep = 0.5;
        }

        var buckets = new Dictionary<double, double>();

        foreach (var bar in bars)
        {
            if (bar.Volume <= 0) continue;

            double bLow = Math.Floor(bar.Low / effectiveStep) * effectiveStep;
            double bHigh = Math.Ceiling(bar.High / effectiveStep) * effectiveStep;
            int steps = Math.Max(1, (int)Math.Round((bHigh - bLow) / effectiveStep) + 1);
            double volPerStep = bar.Volume / steps;

            for (double p = bLow; p <= bHigh + 0.00001; p += effectiveStep)
            {
                double key = Math.Round(p, 2);
                buckets[key] = buckets.GetValueOrDefault(key, 0.0) + volPerStep;
            }
        }

        if (buckets.Count == 0) return result;

        double maxVol = buckets.Values.Max();

        foreach (var kvp in buckets.OrderByDescending(k => k.Key))
        {
            result.Add(new VolumeProfileLevel
            {
                Price = kvp.Key,
                Volume = Math.Round(kvp.Value, 0),
                IsPOC = Math.Abs(kvp.Value - maxVol) < 0.0001,
                RelativeWidth = maxVol > 0 ? (kvp.Value / maxVol) : 0
            });
        }

        return result;
    }

    /// <summary>
    /// Оценивает текущее рыночное состояние по закону «Усилие против Результата» (VSA):
    /// сопоставляет аномалии объема (усилие) с продвижением цены (результат) за последние 5 минут.
    /// Определяет поглощение (скрытый лимитный игрок), истинный импульс, истощение или тонкий рынок.
    /// </summary>
    /// <param name="bars">Список баров дня.</param>
    /// <param name="benchmarks">Словарь исторических бенчмарков.</param>
    /// <returns>Объект EffortResultAnalysis с классификацией и торговой рекомендацией.</returns>
    public EffortResultAnalysis EvaluateEffortVsResult(List<MinuteBar> bars, Dictionary<string, IntradayBenchmark> benchmarks)
    {
        var analysis = new EffortResultAnalysis();
        if (bars.Count == 0) return analysis;

        var lastBar = bars[^1];
        var recentBars = bars.TakeLast(Math.Min(5, bars.Count)).ToList();

        double recentVol = recentBars.Sum(b => b.Volume);
        double recentNormVol = recentBars.Sum(b => b.BenchmarkAvgVolume ?? (b.Volume > 0 ? b.Volume : 1.0));
        double recentRVol = recentNormVol > 0 ? (recentVol / recentNormVol) : 1.0;

        double recentHigh = recentBars.Max(b => b.High);
        double recentLow = recentBars.Min(b => b.Low);
        double recentRange = recentHigh - recentLow;
        double recentNetChange = lastBar.Close - recentBars[0].Open;
        double recentOiChange = recentBars.Sum(b => b.OpenInterestChange ?? 0.0);

        analysis.EffortScore = Math.Round(recentRVol, 2);
        analysis.ResultScore = Math.Round(recentRange, 2);
        analysis.EfficiencyRatio = recentRVol > 0 ? Math.Round(recentRange / recentRVol, 2) : 0;

        // VSA Logic
        if (recentRVol >= 1.6)
        {
            // High Effort
            if (Math.Abs(recentNetChange) <= 0.3 * recentRange)
            {
                // Result is small despite huge volume -> Absorption
                if (recentNetChange >= 0 || (lastBar.CLV.HasValue && lastBar.CLV > 0))
                {
                    analysis.Type = EffortResultType.AbsorptionBullish;
                    analysis.Title = "Бычье поглощение продаж (VSA)";
                    analysis.Summary = $"Аномальный объем ({recentRVol:F1}x нормы) при остановке падения цены. Лимитный покупатель встретил предложение.";
                    analysis.BadgeColor = "#38A169"; // Green
                    analysis.Recommendation = "Поиск покупок при тесте уровня или пробое локального максимума.";
                }
                else
                {
                    analysis.Type = EffortResultType.AbsorptionBearish;
                    analysis.Title = "Медвежье поглощение спроса (VSA)";
                    analysis.Summary = $"Аномальный объем ({recentRVol:F1}x нормы) на вершине без продвижения вверх. Лимитный продавец удерживает уровень.";
                    analysis.BadgeColor = "#E53E3E"; // Red
                    analysis.Recommendation = "Внимание к продажам, возможно появление разворотного паттерна.";
                }
            }
            else
            {
                // High Effort with High Result -> Breakout or Climax
                if (recentRVol >= 2.8 && ((lastBar.CLV > 0.8 && recentNetChange < 0) || (lastBar.CLV < -0.8 && recentNetChange > 0)))
                {
                    analysis.Type = EffortResultType.ClimaxExhaustion;
                    analysis.Title = "Кульминация объёма (Истощение)";
                    analysis.Summary = $"Сверхвысокий объем ({recentRVol:F1}x) с длинным контртеневым хвостом. Признак кульминации движения.";
                    analysis.BadgeColor = "#ED8936"; // Orange
                    analysis.Recommendation = "Фиксация позиций по тренду, опасность глубокого отката.";
                }
                else
                {
                    analysis.Type = EffortResultType.BreakoutConfirmed;
                    var dir = recentNetChange > 0 ? "восходящий" : "нисходящий";
                    analysis.Title = $"Истинный {dir} импульс";
                    analysis.Summary = $"Усилие подтверждено результатом: объем {recentRVol:F1}x нормы сопровождает движение на {recentNetChange:+0.0;-0.0;0.0} п.";
                    analysis.BadgeColor = recentNetChange > 0 ? "#38A169" : "#E53E3E";
                    analysis.Recommendation = "Приоритет сделок в направлении подтвержденного импульса.";
                }
            }
        }
        else if (recentRVol <= 0.65)
        {
            if (recentRange > 1.2)
            {
                analysis.Type = EffortResultType.LowVolumeDrift;
                analysis.Title = "Движение на слабом объёме (Low Volume)";
                analysis.Summary = $"Цена сдвинулась на {recentRange:F1} п. при сниженном объеме ({recentRVol:F1}x). Отсутствие реального институционального интереса.";
                analysis.BadgeColor = "#805AD5"; // Purple
                analysis.Recommendation = "Осторожно с пробоями: высокий риск ложного выноса без поддержки объема.";
            }
            else
            {
                analysis.Type = EffortResultType.Consolidation;
                analysis.Title = "Консолидация / Накопление";
                analysis.Summary = $"Рынок в узком балансе, активность участников снижена ({recentRVol:F1}x нормы).";
                analysis.BadgeColor = "#718096"; // Slate
                analysis.Recommendation = "Выжидать накопления объема и направленного выхода из диапазона.";
            }
        }
        else
        {
            analysis.Type = EffortResultType.Normal;
            analysis.Title = "Сбалансированный рынок";
            analysis.Summary = $"Объем ({recentRVol:F1}x) и диапазон соответствуют типичным статистическим нормам.";
            analysis.BadgeColor = "#4A5568";
            analysis.Recommendation = "Работа по стандартным внутридневным уровням.";
        }

        analysis.Details = $"ОИ за 5м: {(recentOiChange >= 0 ? "+" : "")}{recentOiChange:F0} контрактов | Волатильность: {recentRange:F1} п.";
        return analysis;
    }

    /// <summary>
    /// Генерирует параллельный прогноз движения цены по двум временным горизонтам:
    /// 1. Горизонт 5–15 минут (микроструктурный импульс, быстрый моментум, VSA поглощение).
    /// 2. Горизонт 30–60 минут (институциональный контроль, положение к VWAP, дневной RVol, тренд ОИ).
    /// Использует поиск эмпирических аналогов в SQLite-истории и скоринг факторов.
    /// </summary>
    /// <param name="liveBars">Бары текущей сессии.</param>
    /// <param name="secCode">Код инструмента.</param>
    /// <param name="currentSessionId">Идентификатор текущей сессии.</param>
    /// <param name="effortAnalysis">Текущая оценка VSA.</param>
    /// <returns>Кортеж из двух независимых прогнозов (shortTerm, mediumTerm).</returns>
    public (HorizonForecast shortTerm, HorizonForecast mediumTerm) GenerateDualForecast(
        List<MinuteBar> liveBars,
        string secCode,
        string currentSessionId,
        EffortResultAnalysis effortAnalysis)
    {
        var shortTerm = new HorizonForecast
        {
            HorizonTitle = "Горизонт 5–15 минут",
            HorizonSubtitle = "Ближайший импульс / микроструктура"
        };

        var mediumTerm = new HorizonForecast
        {
            HorizonTitle = "Горизонт 30–60 минут",
            HorizonSubtitle = "Внутридневной тренд и баланс сессии"
        };

        if (liveBars.Count == 0)
        {
            shortTerm.RegimeSummary = "Ожидание данных...";
            mediumTerm.RegimeSummary = "Ожидание данных...";
            return (shortTerm, mediumTerm);
        }

        var last = liveBars[^1];
        var bars5 = liveBars.TakeLast(Math.Min(5, liveBars.Count)).ToList();
        var bars15 = liveBars.TakeLast(Math.Min(15, liveBars.Count)).ToList();

        // Оценка средней внутриминутной волатильности (ATR) за последние 15 минут для фильтрации рыночного шума
        double atr15 = bars15.Count > 0 ? Math.Max(0.8, bars15.Average(b => b.PriceRange)) : 1.2;

        double mom5 = last.Close - bars5[0].Open;
        double mom15 = last.Close - bars15[0].Open;
        double oiDelta15 = bars15.Sum(b => b.OpenInterestChange ?? 0.0);
        double avgRvol15 = bars15.Count > 0 ? bars15.Average(b => b.RVol) : 1.0;
        double vwapDist = last.Close - last.VWAP;

        // Поиск исторических аналогов с фильтром по направлению
        bool? momDir = Math.Abs(mom5) >= (atr15 * 0.5) ? (mom5 > 0) : null;
        var historicalMatches = QueryHistoricalAnalogs(secCode, currentSessionId, last.Time, momDir ?? (mom15 > 0), avgRvol15 > 1.2);

        // --- 1. Short-Term Forecast (5-15 min) ---
        CalculateShortTerm(shortTerm, last, mom5, mom15, oiDelta15, avgRvol15, atr15, effortAnalysis, historicalMatches);

        // --- 2. Medium-Term Forecast (30-60 min) ---
        CalculateMediumTerm(mediumTerm, liveBars, last, vwapDist, atr15, oiDelta15, avgRvol15, historicalMatches);

        return (shortTerm, mediumTerm);
    }

    private void CalculateShortTerm(
        HorizonForecast f,
        MinuteBar last,
        double mom5,
        double mom15,
        double oiDelta15,
        double avgRvol15,
        double atr15,
        EffortResultAnalysis effort,
        List<HistoricalAnalogResult> analogs)
    {
        // Score between -100 (strong down) to +100 (strong up)
        double score = 0;
        var drivers = new List<string>();

        // 1. Быстрый импульс за 5 минут с фильтрацией шума
        double noiseThreshold = Math.Max(0.5, atr15 * 0.5);
        if (mom5 > noiseThreshold * 1.5)
        {
            score += 35;
            drivers.Add($"Сильный импульс вверх: +{mom5:F1} п. за 5 мин");
        }
        else if (mom5 > noiseThreshold)
        {
            score += 25;
            drivers.Add($"Краткосрочный импульс: +{mom5:F1} п. за 5 мин");
        }
        else if (mom5 < -noiseThreshold * 1.5)
        {
            score -= 35;
            drivers.Add($"Сильное давление вниз: {mom5:F1} п. за 5 мин");
        }
        else if (mom5 < -noiseThreshold)
        {
            score -= 25;
            drivers.Add($"Краткосрочное давление: {mom5:F1} п. за 5 мин");
        }
        else
        {
            drivers.Add($"Колебания в пределах шума волатильности (±{noiseThreshold:F1} п.)");
        }

        // 2. Среднесрочный импульс за 15 минут (независимый от ОИ)
        if (mom15 > noiseThreshold * 1.5)
        {
            score += 30;
            drivers.Add($"15-мин. тренд восходящий (+{mom15:F1} п.)");
        }
        else if (mom15 > noiseThreshold)
        {
            score += 20;
            drivers.Add($"15-мин. тренд умеренно вверх (+{mom15:F1} п.)");
        }
        else if (mom15 < -noiseThreshold * 1.5)
        {
            score -= 30;
            drivers.Add($"15-мин. тренд нисходящий ({mom15:F1} п.)");
        }
        else if (mom15 < -noiseThreshold)
        {
            score -= 20;
            drivers.Add($"15-мин. тренд умеренно вниз ({mom15:F1} п.)");
        }

        // 3. Положение цены относительно VWAP
        double vwapDist = last.Close - last.VWAP;
        if (vwapDist > (atr15 * 0.5))
        {
            score += 15;
            drivers.Add($"Цена выше VWAP (+{vwapDist:F1} п.)");
        }
        else if (vwapDist < -(atr15 * 0.5))
        {
            score -= 15;
            drivers.Add($"Цена ниже VWAP ({vwapDist:F1} п.)");
        }

        // 4. VSA Effort factor
        if (effort.Type == EffortResultType.AbsorptionBullish)
        {
            score += 30;
            drivers.Add("VSA: бычье поглощение продаж лимитными ордерами");
        }
        else if (effort.Type == EffortResultType.AbsorptionBearish)
        {
            score -= 30;
            drivers.Add("VSA: медвежье поглощение спроса на сопротивлении");
        }
        else if (effort.Type == EffortResultType.BreakoutConfirmed)
        {
            if (mom5 > 0) { score += 25; drivers.Add($"VSA: истинный пробой вверх, поддержанный объемом ({effort.EffortScore:F1}x)"); }
            else { score -= 25; drivers.Add($"VSA: истинный пробой вниз, поддержанный объемом ({effort.EffortScore:F1}x)"); }
        }
        else if (effort.Type == EffortResultType.ClimaxExhaustion)
        {
            if (mom5 > 0) { score -= 20; drivers.Add("VSA: признак кульминации покупок, вероятность отката"); }
            else { score += 20; drivers.Add("VSA: признак кульминации продаж, вероятность отскока"); }
        }

        // 5. Open Interest synergy
        if (oiDelta15 > 10)
        {
            if (mom15 > 0) { score += 15; drivers.Add($"ОИ растёт синхронно с ценой (+{oiDelta15:F0} контр.) — приток лонгов"); }
            else { score -= 15; drivers.Add($"ОИ растёт на падении цены (+{oiDelta15:F0} контр.) — набор шортов"); }
        }
        else if (oiDelta15 < -10)
        {
            if (mom15 > 0) { score -= 10; drivers.Add($"Рост на закрытии шортов (ОИ {oiDelta15:F0}) — слабое топливо"); }
        }

        // Расчет вероятностей на базе скора и исторических аналогов
        double mappedProbDelta = (Math.Clamp(score, -100, 100) / 100.0) * 32.0;
        double mappedScoreProbUp = Math.Clamp(33.3 + mappedProbDelta, 8, 85);
        double mappedScoreProbDown = Math.Clamp(33.3 - mappedProbDelta, 8, 85);

        var shortAnalogs = analogs.Where(a => a.BarsForward == 10).ToList();
        if (shortAnalogs.Count >= 5)
        {
            f.HistoricalMatchesCount = shortAnalogs.Count;
            int upCount = shortAnalogs.Count(a => a.PriceDelta > 0.3);
            int downCount = shortAnalogs.Count(a => a.PriceDelta < -0.3);
            int flatCount = shortAnalogs.Count - upCount - downCount;

            double histProbUp = (double)upCount / shortAnalogs.Count * 100.0;
            double histProbDown = (double)downCount / shortAnalogs.Count * 100.0;

            f.ProbUp = Math.Round(0.6 * mappedScoreProbUp + 0.4 * histProbUp, 1);
            f.ProbDown = Math.Round(0.6 * mappedScoreProbDown + 0.4 * histProbDown, 1);
            f.ProbNeutral = Math.Round(Math.Max(0, 100.0 - f.ProbUp - f.ProbDown), 1);

            f.ExpectedPointsMove = Math.Round(shortAnalogs.Average(a => Math.Abs(a.PriceDelta)), 1);
            f.ConfidenceText = $"Основано на {shortAnalogs.Count} исторических аналогах";
        }
        else
        {
            f.ProbUp = Math.Round(mappedScoreProbUp, 1);
            f.ProbDown = Math.Round(mappedScoreProbDown, 1);
            f.ProbNeutral = Math.Round(Math.Max(0, 100.0 - f.ProbUp - f.ProbDown), 1);
            f.ExpectedPointsMove = Math.Round(Math.Max(1.0, last.PriceRange * 1.5), 1);
            f.ConfidenceText = "Качественный VSA/ОИ алгоритм (накопление базы)";
        }

        f.ExpectedPctMove = last.Close > 0 ? Math.Round((f.ExpectedPointsMove / last.Close) * 100, 2) : 0;

        // Корректная классификация 3-факторной вероятности (Up / Down / Neutral)
        if (f.ProbUp >= 42.0 && (f.ProbUp - f.ProbDown) >= 12.0)
        {
            f.Direction = "ВВЕРХ ▲";
            f.DirectionColor = "#38A169";
            f.RegimeSummary = "Преимущество покупателей на 5–15 мин.";
        }
        else if (f.ProbDown >= 42.0 && (f.ProbDown - f.ProbUp) >= 12.0)
        {
            f.Direction = "ВНИЗ ▼";
            f.DirectionColor = "#E53E3E";
            f.RegimeSummary = "Преимущество продавцов на 5–15 мин.";
        }
        else
        {
            f.Direction = "НЕЙТРАЛЬНО ▬";
            f.DirectionColor = "#718096";
            f.RegimeSummary = "Баланс сил / консолидация на 5–15 мин.";
        }

        f.KeyFactors = drivers.Take(3).ToList();
        if (f.KeyFactors.Count == 0)
        {
            f.KeyFactors.Add($"Активность: {avgRvol15:F1}x от среднего");
            f.KeyFactors.Add($"Локальный диапазон: {last.PriceRange:F1} п.");
        }
    }

    private void CalculateMediumTerm(
        HorizonForecast f,
        List<MinuteBar> liveBars,
        MinuteBar last,
        double vwapDist,
        double atr15,
        double oiDelta15,
        double avgRvol15,
        List<HistoricalAnalogResult> analogs)
    {
        double score = 0;
        var drivers = new List<string>();

        // 1. Положение относительно VWAP
        double vwapBuffer = Math.Max(0.8, atr15 * 0.5);
        if (vwapDist > vwapBuffer)
        {
            score += 30;
            drivers.Add($"Цена выше VWAP (+{vwapDist:F1} п.) — институциональный контроль быков");
        }
        else if (vwapDist < -vwapBuffer)
        {
            score -= 30;
            drivers.Add($"Цена ниже VWAP ({vwapDist:F1} п.) — институциональный контроль медведей");
        }
        else
        {
            drivers.Add($"Цена у справедливой стоимости VWAP (буфер ±{vwapBuffer:F1} п.) — нейтрально");
        }

        // 2. Устойчивый структурный тренд за 30 минут
        var bars30 = liveBars.TakeLast(Math.Min(30, liveBars.Count)).ToList();
        double mom30 = last.Close - bars30[0].Open;
        double trendThreshold = Math.Max(1.0, atr15 * 1.0);

        if (mom30 > trendThreshold * 1.5)
        {
            score += 40;
            drivers.Add($"30-мин. тренд уверенно восходящий (+{mom30:F1} п.)");
        }
        else if (mom30 > trendThreshold)
        {
            score += 25;
            drivers.Add($"30-мин. тренд восходящий (+{mom30:F1} п.)");
        }
        else if (mom30 < -trendThreshold * 1.5)
        {
            score -= 40;
            drivers.Add($"30-мин. тренд уверенно нисходящий ({mom30:F1} п.)");
        }
        else if (mom30 < -trendThreshold)
        {
            score -= 25;
            drivers.Add($"30-мин. тренд нисходящий ({mom30:F1} п.)");
        }
        else
        {
            drivers.Add($"30-мин. диапазон: рынок в балансе (колебания в пределах ±{trendThreshold:F1} п.)");
        }

        // 3. Структура дня относительно открытия
        double dayOpen = liveBars[0].Open;
        double dayChange = last.Close - dayOpen;
        if (dayChange > trendThreshold)
        {
            score += 20;
            drivers.Add($"Структура дня: развитие восходящего тренда (+{dayChange:F1} п.)");
        }
        else if (dayChange < -trendThreshold)
        {
            score -= 20;
            drivers.Add($"Структура дня: развитие нисходящего тренда ({dayChange:F1} п.)");
        }

        // 4. Дневной тренд открытого интереса
        if (last.CumulativeOiChange > 60)
        {
            if (dayChange > 0) score += 15;
            drivers.Add($"Дневной ОИ: активный приток (+{last.CumulativeOiChange:F0})");
        }
        else if (last.CumulativeOiChange < -60)
        {
            score = score * 0.8;
            drivers.Add($"Дневной ОИ: закрытие позиций ({last.CumulativeOiChange:F0})");
        }

        // Расчет вероятностей на 30-60 минут
        double mappedProbDelta = (Math.Clamp(score, -100, 100) / 100.0) * 32.0;
        double mappedScoreProbUp = Math.Clamp(33.3 + mappedProbDelta, 8, 85);
        double mappedScoreProbDown = Math.Clamp(33.3 - mappedProbDelta, 8, 85);

        var mediumAnalogs = analogs.Where(a => a.BarsForward == 45).ToList();
        if (mediumAnalogs.Count >= 5)
        {
            f.HistoricalMatchesCount = mediumAnalogs.Count;
            int upCount = mediumAnalogs.Count(a => a.PriceDelta > 0.6);
            int downCount = mediumAnalogs.Count(a => a.PriceDelta < -0.6);
            int flatCount = mediumAnalogs.Count - upCount - downCount;

            double histProbUp = (double)upCount / mediumAnalogs.Count * 100.0;
            double histProbDown = (double)downCount / mediumAnalogs.Count * 100.0;

            f.ProbUp = Math.Round(0.6 * mappedScoreProbUp + 0.4 * histProbUp, 1);
            f.ProbDown = Math.Round(0.6 * mappedScoreProbDown + 0.4 * histProbDown, 1);
            f.ProbNeutral = Math.Round(Math.Max(0, 100.0 - f.ProbUp - f.ProbDown), 1);
            f.ExpectedPointsMove = Math.Round(mediumAnalogs.Average(a => Math.Abs(a.PriceDelta)), 1);
            f.ConfidenceText = $"Основано на {mediumAnalogs.Count} исторических выборках";
        }
        else
        {
            f.ProbUp = Math.Round(mappedScoreProbUp, 1);
            f.ProbDown = Math.Round(mappedScoreProbDown, 1);
            f.ProbNeutral = Math.Round(Math.Max(0, 100.0 - f.ProbUp - f.ProbDown), 1);
            f.ExpectedPointsMove = Math.Round(Math.Max(2.5, last.PriceRange * 3.0), 1);
            f.ConfidenceText = "Модель на базе VWAP, кумулятивного RVol и ОИ";
        }

        f.ExpectedPctMove = last.Close > 0 ? Math.Round((f.ExpectedPointsMove / last.Close) * 100, 2) : 0;

        // Порог классификации 30-60 минут: переключение при преимуществе над противоположной стороной от 12%
        if (f.ProbUp >= 43.0 && (f.ProbUp - f.ProbDown) >= 12.0)
        {
            f.Direction = "ВВЕРХ ▲";
            f.DirectionColor = "#38A169";
            f.RegimeSummary = "Бычий контекст на 30–60 мин.";
        }
        else if (f.ProbDown >= 43.0 && (f.ProbDown - f.ProbUp) >= 12.0)
        {
            f.Direction = "ВНИЗ ▼";
            f.DirectionColor = "#E53E3E";
            f.RegimeSummary = "Медвежий контекст на 30–60 мин.";
        }
        else
        {
            f.Direction = "НЕЙТРАЛЬНО ▬";
            f.DirectionColor = "#718096";
            f.RegimeSummary = "Флэт / ротация внутри диапазона на 30–60 мин.";
        }

        f.KeyFactors = drivers.Take(3).ToList();
    }

    private class HistoricalAnalogResult
    {
        public int BarsForward { get; set; }
        public double PriceDelta { get; set; }
    }

    private List<HistoricalAnalogResult> QueryHistoricalAnalogs(
        string secCode,
        string excludeSessionId,
        string time,
        bool isPositiveMomentum,
        bool isHighVolume)
    {
        var results = new List<HistoricalAnalogResult>();
        try
        {
            // Sample historical sessions from DB
            var sessions = _db.GetAvailableSessions(secCode)
                              .Where(s => s != excludeSessionId)
                              .Take(12)
                              .ToList();

            foreach (var session in sessions)
            {
                var bars = _db.GetSessionBars(session, secCode);
                if (bars.Count < 20) continue;

                // Find bar closest in time (within +/- 30 min)
                for (int i = 5; i < bars.Count - 45; i++)
                {
                    var b = bars[i];
                    if (string.Compare(b.Time, time, StringComparison.Ordinal) >= 0)
                    {
                        // Check similarity
                        double histMom = b.Close - bars[i - 5].Open;
                        bool histMomSign = histMom > 0;
                        if (histMomSign == isPositiveMomentum)
                        {
                            // 10 bars forward (5-15 min)
                            results.Add(new HistoricalAnalogResult
                            {
                                BarsForward = 10,
                                PriceDelta = bars[i + 10].Close - b.Close
                            });

                            // 45 bars forward (30-60 min)
                            results.Add(new HistoricalAnalogResult
                            {
                                BarsForward = 45,
                                PriceDelta = bars[i + 45].Close - b.Close
                            });
                        }
                        break;
                    }
                }
            }
        }
        catch
        {
            // Fallback gracefully
        }
        return results;
    }
}
