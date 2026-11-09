using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MarketStatsViewer.Models;

namespace MarketStatsViewer.Controls;

/// <summary>
/// График темпа дня (Intraday Pace Chart):
/// Сравнивает фактическую кривую накопления объема сегодня с эталонной исторической кривой среднего дня.
/// Поддерживает интерактивное перекрестие и всплывающий тултип точки.
/// </summary>
public class IntradayPaceChartControl : FrameworkElement
{
    public static readonly DependencyProperty BarsProperty =
        DependencyProperty.Register(nameof(Bars), typeof(IEnumerable<MinuteBar>), typeof(IntradayPaceChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BenchmarksProperty =
        DependencyProperty.Register(nameof(Benchmarks), typeof(Dictionary<string, IntradayBenchmark>), typeof(IntradayPaceChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<MinuteBar>? Bars
    {
        get => (IEnumerable<MinuteBar>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public Dictionary<string, IntradayBenchmark>? Benchmarks
    {
        get => (Dictionary<string, IntradayBenchmark>?)GetValue(BenchmarksProperty);
        set => SetValue(BenchmarksProperty, value);
    }

    private static readonly CultureInfo InvCulture = CultureInfo.InvariantCulture;

    private static readonly SolidColorBrush BgBrush = new(Color.FromRgb(15, 23, 42)); // #0F172A
    private static readonly SolidColorBrush GridBrush = new(Color.FromArgb(40, 71, 85, 105)); // #475569
    private static readonly SolidColorBrush TextBrush = new(Color.FromRgb(148, 163, 184)); // #94A3B8

    private static readonly Pen TodayPen = new(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 2.2); // #38BDF8 (Today)
    private static readonly Pen BenchmarkPen = new(new SolidColorBrush(Color.FromArgb(180, 251, 191, 36)), 1.5) // #FBBF24 (Norm)
    {
        DashStyle = DashStyles.Dash
    };

    private static readonly Pen CrosshairPen = new(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 1.0)
    {
        DashStyle = DashStyles.Dot
    };

    private static readonly FontFamily ChartFontFamily = new("Segoe UI");
    private static readonly Typeface RegularTypeface = new(ChartFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface BoldTypeface = new(ChartFontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface SemiBoldTypeface = new(ChartFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private Point? _mousePos;

    public IntradayPaceChartControl()
    {
        ClipToBounds = true;
        Cursor = Cursors.Cross;

        MouseMove += (s, e) => { _mousePos = e.GetPosition(this); InvalidateVisual(); };
        MouseLeave += (s, e) => { _mousePos = null; InvalidateVisual(); };
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 40 || h < 40) return;

        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

        var barList = Bars?.ToList();
        if (barList == null || barList.Count == 0)
        {
            var txt = new FormattedText("Ожидание данных темпа дня...", InvCulture, FlowDirection.LeftToRight,
                RegularTypeface, 11, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(txt, new Point((w - txt.Width) / 2, (h - txt.Height) / 2));
            return;
        }

        double marginL = 10;
        double marginR = 55;
        double marginT = 24;
        double marginB = 18;

        double plotW = w - marginL - marginR;
        double plotH = h - marginT - marginB;

        // Определяем максимальный объем для шкалы Y
        double maxVolToday = barList[^1].CumulativeVolume;
        double maxVolNorm = 0;
        if (Benchmarks != null && Benchmarks.TryGetValue(barList[^1].Time, out var endBm))
        {
            maxVolNorm = endBm.AvgCumulativeVolume;
        }
        double maxY = Math.Max(maxVolToday, maxVolNorm) * 1.1;
        if (maxY <= 0) maxY = 1000;

        // Заголовок и легенда
        var title = new FormattedText("ТЕМП НАКОПЛЕНИЯ ОБЪЕМА (СЕГОДНЯ vs НОРМА)", InvCulture, FlowDirection.LeftToRight,
            BoldTypeface, 10, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(title, new Point(marginL, 6));

        // Отрисовка сетки
        for (int i = 0; i <= 3; i++)
        {
            double yVal = (maxY / 3.0) * i;
            double y = marginT + plotH - (i * (plotH / 3.0));
            dc.DrawLine(new Pen(GridBrush, 0.8), new Point(marginL, y), new Point(marginL + plotW, y));

            var lbl = new FormattedText($"{yVal:N0}", InvCulture, FlowDirection.LeftToRight,
                RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(lbl, new Point(marginL + plotW + 4, y - lbl.Height / 2.0));
        }

        // Построение точек
        var todayPoints = new List<Point>();
        var normPoints = new List<Point>();

        double stepX = plotW / Math.Max(1, barList.Count - 1);
        MinuteBar? hoveredBar = null;
        Point? hoveredTodayPoint = null;
        double hoveredNormVol = 0;

        for (int i = 0; i < barList.Count; i++)
        {
            var b = barList[i];
            double x = marginL + i * stepX;

            // Сегодня
            double yToday = marginT + plotH - (b.CumulativeVolume / maxY) * plotH;
            var ptToday = new Point(x, yToday);
            todayPoints.Add(ptToday);

            // Норма
            double curNorm = 0;
            if (Benchmarks != null && Benchmarks.TryGetValue(b.Time, out var bm) && bm.AvgCumulativeVolume > 0)
            {
                curNorm = bm.AvgCumulativeVolume;
                double yNorm = marginT + plotH - (bm.AvgCumulativeVolume / maxY) * plotH;
                normPoints.Add(new Point(x, yNorm));
            }

            if (_mousePos.HasValue && Math.Abs(_mousePos.Value.X - x) <= stepX / 2.0)
            {
                hoveredBar = b;
                hoveredTodayPoint = ptToday;
                hoveredNormVol = curNorm;
            }
        }

        // Отрисовка эталонной кривой нормы (пунктир)
        for (int i = 0; i < normPoints.Count - 1; i++)
        {
            dc.DrawLine(BenchmarkPen, normPoints[i], normPoints[i + 1]);
        }

        // Отрисовка кривой сегодня (сплошная голубая)
        for (int i = 0; i < todayPoints.Count - 1; i++)
        {
            dc.DrawLine(TodayPen, todayPoints[i], todayPoints[i + 1]);
        }

        // Подпись внизу (X-Axis)
        var startTxt = new FormattedText(barList[0].ShortTime, InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(startTxt, new Point(marginL, h - marginB + 3));

        var endTxt = new FormattedText(barList[^1].ShortTime, InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(endTxt, new Point(marginL + plotW - endTxt.Width, h - marginB + 3));

        // Интерактивный тултип при наведении
        if (hoveredBar != null && hoveredTodayPoint.HasValue)
        {
            dc.DrawLine(CrosshairPen, new Point(hoveredTodayPoint.Value.X, marginT), new Point(hoveredTodayPoint.Value.X, marginT + plotH));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(56, 189, 248)), new Pen(Brushes.White, 1.5), hoveredTodayPoint.Value, 4, 4);

            double diff = hoveredNormVol > 0 ? ((hoveredBar.CumulativeVolume - hoveredNormVol) / hoveredNormVol) * 100.0 : 0;
            string diffSign = diff >= 0 ? "+" : "";

            var tipText = new FormattedText(
                $"[{hoveredBar.Time}] Сегодня: {hoveredBar.CumulativeVolume:N0} к. | Норма: {hoveredNormVol:N0} к. ({diffSign}{diff:F0}%)",
                InvCulture, FlowDirection.LeftToRight,
                SemiBoldTypeface, 10, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            double tipX = Math.Clamp(hoveredTodayPoint.Value.X + 8, marginL, marginL + plotW - tipText.Width - 14);
            double tipY = Math.Clamp(hoveredTodayPoint.Value.Y - 24, marginT, marginT + plotH - tipText.Height - 6);

            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 30, 41, 59)), new Pen(new SolidColorBrush(Color.FromRgb(71, 85, 105)), 1),
                new Rect(tipX, tipY, tipText.Width + 12, tipText.Height + 6));
            dc.DrawText(tipText, new Point(tipX + 6, tipY + 3));
        }
    }
}