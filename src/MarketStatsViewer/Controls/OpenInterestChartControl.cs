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
/// График кумулятивного Открытого Интереса (Open Interest Chart):
/// Показывает динамику притока (+) или оттока (-) капитала в позиции с начала торговой сессии.
/// Поддерживает интерактивное перекрестие и всплывающий тултип точки.
/// </summary>
public class OpenInterestChartControl : FrameworkElement
{
    public static readonly DependencyProperty BarsProperty =
        DependencyProperty.Register(nameof(Bars), typeof(IEnumerable<MinuteBar>), typeof(OpenInterestChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<MinuteBar>? Bars
    {
        get => (IEnumerable<MinuteBar>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    private static readonly CultureInfo InvCulture = CultureInfo.InvariantCulture;

    private static readonly SolidColorBrush BgBrush = new(Color.FromRgb(15, 23, 42)); // #0F172A
    private static readonly SolidColorBrush GridBrush = new(Color.FromArgb(40, 71, 85, 105)); // #475569
    private static readonly SolidColorBrush TextBrush = new(Color.FromRgb(148, 163, 184)); // #94A3B8

    private static readonly Pen ZeroPen = new(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1.0)
    {
        DashStyle = DashStyles.Dash
    };

    private static readonly Pen OiPen = new(new SolidColorBrush(Color.FromRgb(168, 85, 247)), 2.0); // #A855F7 (Purple)

    private static readonly Pen CrosshairPen = new(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 1.0)
    {
        DashStyle = DashStyles.Dot
    };

    private static readonly FontFamily ChartFontFamily = new("Segoe UI");
    private static readonly Typeface RegularTypeface = new(ChartFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface BoldTypeface = new(ChartFontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface SemiBoldTypeface = new(ChartFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private Point? _mousePos;

    public OpenInterestChartControl()
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
            var txt = new FormattedText("Ожидание данных Открытого Интереса...", InvCulture, FlowDirection.LeftToRight,
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

        // Поиск минимума и максимума кумулятивного ОИ
        double minOi = barList.Min(b => b.CumulativeOiChange);
        double maxOi = barList.Max(b => b.CumulativeOiChange);

        // Всегда включаем 0 в шкалу
        minOi = Math.Min(minOi, 0);
        maxOi = Math.Max(maxOi, 0);

        double spread = Math.Max(20.0, maxOi - minOi) * 1.15;
        double centerOffset = (minOi + maxOi) / 2.0;
        double plotMin = centerOffset - spread / 2.0;
        double plotMax = centerOffset + spread / 2.0;
        double rangeOi = plotMax - plotMin;

        // Заголовок
        double curOi = barList[^1].CumulativeOiChange;
        string sign = curOi >= 0 ? "+" : "";
        var title = new FormattedText($"КУМУЛЯТИВНЫЙ ОТКРЫТЫЙ ИНТЕРЕС (ДЕЛЬТА: {sign}{curOi:F0} контр.)", InvCulture, FlowDirection.LeftToRight,
            BoldTypeface, 10, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(title, new Point(marginL, 6));

        // Нулевая линия
        double yZero = marginT + plotH - ((0 - plotMin) / rangeOi) * plotH;
        dc.DrawLine(ZeroPen, new Point(marginL, yZero), new Point(marginL + plotW, yZero));

        var zeroLbl = new FormattedText("0", InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(zeroLbl, new Point(marginL + plotW + 4, yZero - zeroLbl.Height / 2.0));

        // Метки максимума и минимума
        var maxLbl = new FormattedText($"{plotMax:+0;-0;0}", InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(maxLbl, new Point(marginL + plotW + 4, marginT));

        var minLbl = new FormattedText($"{plotMin:+0;-0;0}", InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(minLbl, new Point(marginL + plotW + 4, marginT + plotH - minLbl.Height));

        // Построение точек
        double stepX = plotW / Math.Max(1, barList.Count - 1);
        var points = new List<Point>();
        MinuteBar? hoveredBar = null;
        Point? hoveredPoint = null;

        for (int i = 0; i < barList.Count; i++)
        {
            var b = barList[i];
            double x = marginL + i * stepX;
            double y = marginT + plotH - ((b.CumulativeOiChange - plotMin) / rangeOi) * plotH;
            var pt = new Point(x, y);
            points.Add(pt);

            if (_mousePos.HasValue && Math.Abs(_mousePos.Value.X - x) <= stepX / 2.0)
            {
                hoveredBar = b;
                hoveredPoint = pt;
            }
        }

        // Отрисовка линии ОИ
        for (int i = 0; i < points.Count - 1; i++)
        {
            dc.DrawLine(OiPen, points[i], points[i + 1]);
        }

        // Подпись оси X
        var startTxt = new FormattedText(barList[0].ShortTime, InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(startTxt, new Point(marginL, h - marginB + 3));

        var endTxt = new FormattedText(barList[^1].ShortTime, InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(endTxt, new Point(marginL + plotW - endTxt.Width, h - marginB + 3));

        // Интерактивный тултип при наведении
        if (hoveredBar != null && hoveredPoint.HasValue)
        {
            dc.DrawLine(CrosshairPen, new Point(hoveredPoint.Value.X, marginT), new Point(hoveredPoint.Value.X, marginT + plotH));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(168, 85, 247)), new Pen(Brushes.White, 1.5), hoveredPoint.Value, 4, 4);

            string s = hoveredBar.CumulativeOiChange >= 0 ? "+" : "";
            string minDeltaS = (hoveredBar.OpenInterestChange ?? 0) >= 0 ? "+" : "";
            var tipText = new FormattedText(
                $"[{hoveredBar.Time}] Накопл. ОИ: {s}{hoveredBar.CumulativeOiChange:F0} к. (минута: {minDeltaS}{hoveredBar.OpenInterestChange:F0})",
                InvCulture, FlowDirection.LeftToRight,
                SemiBoldTypeface, 10, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            double tipX = Math.Clamp(hoveredPoint.Value.X + 8, marginL, marginL + plotW - tipText.Width - 14);
            double tipY = Math.Clamp(hoveredPoint.Value.Y - 24, marginT, marginT + plotH - tipText.Height - 6);

            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 30, 41, 59)), new Pen(new SolidColorBrush(Color.FromRgb(71, 85, 105)), 1),
                new Rect(tipX, tipY, tipText.Width + 12, tipText.Height + 6));
            dc.DrawText(tipText, new Point(tipX + 6, tipY + 3));
        }
    }
}