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
/// Высокопроизводительный векторный биржевой график на WPF:
/// 1. Свечи цены (Candlesticks) с фитилями.
/// 2. Динамическая линия VWAP дня.
/// 3. Гистограмма объемов с цветовой VSA-раскраской (RVol) и линией нормы времени.
/// 4. Горизонтальный профиль объема (Volume Profile) с выделением POC (Point of Control).
/// 5. Интерактивное перекрестие (Crosshair) и тултип с параметрами бара.
/// </summary>
public class TradingChartControl : FrameworkElement
{
    public static readonly DependencyProperty BarsProperty =
        DependencyProperty.Register(nameof(Bars), typeof(IEnumerable<MinuteBar>), typeof(TradingChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VolumeProfileProperty =
        DependencyProperty.Register(nameof(VolumeProfile), typeof(IEnumerable<VolumeProfileLevel>), typeof(TradingChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<MinuteBar>? Bars
    {
        get => (IEnumerable<MinuteBar>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public IEnumerable<VolumeProfileLevel>? VolumeProfile
    {
        get => (IEnumerable<VolumeProfileLevel>?)GetValue(VolumeProfileProperty);
        set => SetValue(VolumeProfileProperty, value);
    }

    private static readonly CultureInfo InvCulture = CultureInfo.InvariantCulture;

    // Цветовая палитра
    private static readonly SolidColorBrush BgBrush = new(Color.FromRgb(15, 23, 42)); // #0F172A
    private static readonly SolidColorBrush GridBrush = new(Color.FromArgb(50, 71, 85, 105)); // #475569
    private static readonly SolidColorBrush TextBrush = new(Color.FromRgb(148, 163, 184)); // #94A3B8

    private static readonly SolidColorBrush BullCandleBrush = new(Color.FromRgb(16, 185, 129)); // #10B981
    private static readonly SolidColorBrush BearCandleBrush = new(Color.FromRgb(239, 68, 68));   // #EF4444
    private static readonly Pen BullWickPen = new(new SolidColorBrush(Color.FromRgb(16, 185, 129)), 1.2);
    private static readonly Pen BearWickPen = new(new SolidColorBrush(Color.FromRgb(239, 68, 68)), 1.2);

    private static readonly Pen VwapPen = new(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 2.0); // #38BDF8
    private static readonly Pen BenchVolPen = new(new SolidColorBrush(Color.FromArgb(180, 251, 191, 36)), 1.2)
    {
        DashStyle = DashStyles.Dash
    };

    private static readonly SolidColorBrush PocBrush = new(Color.FromRgb(251, 191, 36)); // #FBBF24
    private static readonly SolidColorBrush ProfileBarBrush = new(Color.FromArgb(120, 56, 189, 248)); // #38BDF8 semi

    private static readonly Pen CrosshairPen = new(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 1.0)
    {
        DashStyle = DashStyles.Dot
    };

    private static readonly FontFamily ChartFontFamily = new("Segoe UI");
    private static readonly Typeface RegularTypeface = new(ChartFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface BoldTypeface = new(ChartFontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface SemiBoldTypeface = new(ChartFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    // Состояние мыши для тултипа
    private Point? _mousePos;
    private MinuteBar? _hoveredBar;

    public TradingChartControl()
    {
        ClipToBounds = true;
        Cursor = Cursors.Cross;

        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _mousePos = e.GetPosition(this);
        InvalidateVisual();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _mousePos = null;
        _hoveredBar = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 50 || h < 50) return;

        // Фон
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

        var barList = Bars?.ToList();
        if (barList == null || barList.Count == 0)
        {
            var emptyText = new FormattedText("Ожидание данных для построения графика...",
                InvCulture, FlowDirection.LeftToRight,
                RegularTypeface, 13, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(emptyText, new Point((w - emptyText.Width) / 2, (h - emptyText.Height) / 2));
            return;
        }

        // Разметка областей
        double rightAxisWidth = 65;
        double profileWidth = 140;
        double bottomAxisHeight = 22;

        double chartWidth = Math.Max(20, w - rightAxisWidth - profileWidth - 10);
        double totalHeight = h - bottomAxisHeight;

        double priceHeight = totalHeight * 0.72;
        double volumeHeight = totalHeight * 0.28;
        double volumeTop = priceHeight;

        // Ограничиваем количество отображаемых баров (последние 70 баров для комфортного масштаба)
        int maxBarsToShow = Math.Min(barList.Count, Math.Max(20, (int)(chartWidth / 9)));
        var visibleBars = barList.TakeLast(maxBarsToShow).ToList();

        // Расчет минимума и максимума цены
        double minPrice = visibleBars.Min(b => b.Low);
        double maxPrice = visibleBars.Max(b => b.High);
        if (Math.Abs(maxPrice - minPrice) < 0.001)
        {
            minPrice -= 1.0;
            maxPrice += 1.0;
        }

        // Небольшой отступ 5% сверху и снизу
        double pricePadding = (maxPrice - minPrice) * 0.06;
        minPrice -= pricePadding;
        maxPrice += pricePadding;
        double priceRange = maxPrice - minPrice;

        // Расчет максимума объема
        double maxVolume = visibleBars.Max(b => Math.Max(b.Volume, b.BenchmarkAvgVolume ?? 0));
        if (maxVolume <= 0) maxVolume = 10;

        // Отрисовка координатной сетки цен
        DrawPriceGrid(dc, 0, chartWidth, priceHeight, minPrice, maxPrice, rightAxisWidth, profileWidth, w);

        // Расчет геометрии свечей
        double barSlotWidth = chartWidth / visibleBars.Count;
        double candleBodyWidth = Math.Clamp(barSlotWidth * 0.7, 3.0, 18.0);

        Point? prevVwapPoint = null;
        _hoveredBar = null;

        for (int i = 0; i < visibleBars.Count; i++)
        {
            var b = visibleBars[i];
            double xCenter = i * barSlotWidth + barSlotWidth / 2.0;

            // Координаты цены
            double yOpen = priceHeight - ((b.Open - minPrice) / priceRange) * priceHeight;
            double yClose = priceHeight - ((b.Close - minPrice) / priceRange) * priceHeight;
            double yHigh = priceHeight - ((b.High - minPrice) / priceRange) * priceHeight;
            double yLow = priceHeight - ((b.Low - minPrice) / priceRange) * priceHeight;

            bool isBull = b.Close >= b.Open;
            var candleBrush = isBull ? BullCandleBrush : BearCandleBrush;
            var wickPen = isBull ? BullWickPen : BearWickPen;

            // Фитиль (High - Low)
            dc.DrawLine(wickPen, new Point(xCenter, yHigh), new Point(xCenter, yLow));

            // Тело свечи
            double bodyTop = Math.Min(yOpen, yClose);
            double bodyHeight = Math.Max(2.0, Math.Abs(yClose - yOpen));
            dc.DrawRectangle(candleBrush, null, new Rect(xCenter - candleBodyWidth / 2.0, bodyTop, candleBodyWidth, bodyHeight));

            // VWAP линия
            if (b.VWAP >= minPrice && b.VWAP <= maxPrice)
            {
                double yVwap = priceHeight - ((b.VWAP - minPrice) / priceRange) * priceHeight;
                var currentVwapPoint = new Point(xCenter, yVwap);
                if (prevVwapPoint.HasValue)
                {
                    dc.DrawLine(VwapPen, prevVwapPoint.Value, currentVwapPoint);
                }
                prevVwapPoint = currentVwapPoint;
            }

            // Отрисовка столбика объема
            double barVolHeight = (b.Volume / maxVolume) * (volumeHeight - 12);
            double yVolTop = (totalHeight) - barVolHeight;

            // Цвет объема по VSA / RVol
            var volBrush = GetVolumeBarBrush(b);
            dc.DrawRectangle(volBrush, null, new Rect(xCenter - candleBodyWidth / 2.0, yVolTop, candleBodyWidth, barVolHeight));

            // Норма объема для времени
            if (b.BenchmarkAvgVolume.HasValue && b.BenchmarkAvgVolume > 0)
            {
                double normHeight = (b.BenchmarkAvgVolume.Value / maxVolume) * (volumeHeight - 12);
                double yNorm = (totalHeight) - normHeight;
                dc.DrawLine(BenchVolPen, new Point(xCenter - candleBodyWidth / 2.0, yNorm), new Point(xCenter + candleBodyWidth / 2.0, yNorm));
            }

            // Проверка наведения мыши
            if (_mousePos.HasValue && Math.Abs(_mousePos.Value.X - xCenter) <= barSlotWidth / 2.0)
            {
                _hoveredBar = b;
                // Вертикальная линия перекрестия
                dc.DrawLine(CrosshairPen, new Point(xCenter, 0), new Point(xCenter, totalHeight));
            }
        }

        // Отрисовка горизонтальной линии перекрестия
        if (_mousePos.HasValue && _mousePos.Value.X <= chartWidth && _mousePos.Value.Y <= totalHeight)
        {
            dc.DrawLine(CrosshairPen, new Point(0, _mousePos.Value.Y), new Point(chartWidth, _mousePos.Value.Y));

            // Подпись цены под курсором
            if (_mousePos.Value.Y <= priceHeight)
            {
                double hoverPrice = maxPrice - (_mousePos.Value.Y / priceHeight) * priceRange;
                var priceText = new FormattedText($"{hoverPrice:F1}", InvCulture, FlowDirection.LeftToRight,
                    RegularTypeface, 11, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 41, 59)), null, new Rect(chartWidth + 2, _mousePos.Value.Y - 9, 55, 18));
                dc.DrawText(priceText, new Point(chartWidth + 6, _mousePos.Value.Y - 8));
            }
        }

        // Отрисовка горизонтального профиля объема (Volume Profile справа)
        DrawVolumeProfile(dc, chartWidth + rightAxisWidth + 5, profileWidth, priceHeight, minPrice, maxPrice);

        // Разделительная линия между ценой и объемом
        dc.DrawLine(new Pen(GridBrush, 1.5), new Point(0, priceHeight), new Point(w, priceHeight));

        // Временные метки внизу (X-Axis)
        DrawTimeAxis(dc, visibleBars, barSlotWidth, totalHeight, bottomAxisHeight);

        // Интерактивный тултип бара в левом верхнем углу
        DrawBarTooltip(dc, _hoveredBar ?? visibleBars[^1]);
    }

    private static Brush GetVolumeBarBrush(MinuteBar b)
    {
        if (b.EffortResultTag == "Поглощение")
            return new SolidColorBrush(Color.FromRgb(249, 115, 22)); // Orange

        if (b.RVol >= 2.2)
            return new SolidColorBrush(Color.FromRgb(251, 191, 36)); // Gold

        if (b.RVol >= 1.5)
            return new SolidColorBrush(Color.FromRgb(56, 189, 248)); // Bright Cyan

        if (b.Close >= b.Open)
            return new SolidColorBrush(Color.FromArgb(160, 16, 185, 129)); // Soft Green

        return new SolidColorBrush(Color.FromArgb(160, 239, 68, 68)); // Soft Red
    }

    private void DrawPriceGrid(DrawingContext dc, double left, double right, double height, double minPrice, double maxPrice, double axisWidth, double profWidth, double totalW)
    {
        int gridLines = 5;
        double step = (maxPrice - minPrice) / gridLines;

        for (int i = 0; i <= gridLines; i++)
        {
            double p = minPrice + i * step;
            double y = height - (i * (height / gridLines));

            // Горизонтальная линия сетки
            dc.DrawLine(new Pen(GridBrush, 0.8), new Point(left, y), new Point(right, y));

            // Подпись цены справа
            var text = new FormattedText($"{p:F1}", InvCulture, FlowDirection.LeftToRight,
                RegularTypeface, 10, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(right + 6, y - text.Height / 2.0));
        }
    }

    private void DrawVolumeProfile(DrawingContext dc, double left, double width, double height, double minPrice, double maxPrice)
    {
        var prof = VolumeProfile?.ToList();
        if (prof == null || prof.Count == 0) return;

        double priceRange = maxPrice - minPrice;
        if (priceRange <= 0) return;

        // Заголовок профиля
        var title = new FormattedText("ПРОФИЛЬ ДНЯ", InvCulture, FlowDirection.LeftToRight,
            BoldTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(title, new Point(left, 4));

        double maxBarLen = width - 38; // Оставляем 38px справа под полную подпись POC

        foreach (var lvl in prof)
        {
            if (lvl.Price < minPrice || lvl.Price > maxPrice) continue;

            double y = height - ((lvl.Price - minPrice) / priceRange) * height;
            double barLen = Math.Max(2.0, lvl.RelativeWidth * maxBarLen);

            var brush = lvl.IsPOC ? PocBrush : ProfileBarBrush;
            dc.DrawRectangle(brush, null, new Rect(left, y - 2.5, barLen, 5.0));

            // Выделение POC (с гарантированным свободным местом для надписи)
            if (lvl.IsPOC)
            {
                var pocLabel = new FormattedText("POC", InvCulture, FlowDirection.LeftToRight,
                    BoldTypeface, 9, PocBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(pocLabel, new Point(left + barLen + 5, y - pocLabel.Height / 2.0));
            }
        }
    }

    private void DrawTimeAxis(DrawingContext dc, List<MinuteBar> visibleBars, double slotWidth, double yAxis, double axisH)
    {
        int labelInterval = Math.Max(1, visibleBars.Count / 6);
        for (int i = 0; i < visibleBars.Count; i += labelInterval)
        {
            var b = visibleBars[i];
            double x = i * slotWidth + slotWidth / 2.0;

            var timeText = new FormattedText(b.ShortTime, InvCulture, FlowDirection.LeftToRight,
                RegularTypeface, 10, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(timeText, new Point(x - timeText.Width / 2.0, yAxis + 4));
        }
    }

    private void DrawBarTooltip(DrawingContext dc, MinuteBar b)
    {
        // 1. Верхняя сводная строка бара в левом верхнем углу
        string sign = b.PriceChange >= 0 ? "+" : "";
        string oiSign = (b.OpenInterestChange ?? 0) >= 0 ? "+" : "";

        var header = new FormattedText(
            $"[{b.Time}] O: {b.Open:F1}  H: {b.High:F1}  L: {b.Low:F1}  C: {b.Close:F1} ({sign}{b.PriceChange:F1}) | VWAP: {b.VWAP:F1} | Vol: {b.Volume:N0} (RVol: {b.RVol:F2}x) | ОИ: {oiSign}{b.OpenInterestChange:F0}",
            InvCulture, FlowDirection.LeftToRight,
            SemiBoldTypeface, 11, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 30, 41, 59)), new Pen(new SolidColorBrush(Color.FromRgb(51, 65, 85)), 1),
            new Rect(8, 6, header.Width + 14, header.Height + 6));
        dc.DrawText(header, new Point(15, 9));

        // 2. Всплывающее контекстное окно (Floating Tooltip) прямо рядом с курсором мыши
        if (_mousePos.HasValue && _hoveredBar != null)
        {
            string vsaText = string.IsNullOrEmpty(b.EffortResultTag) ? "Баланс" : b.EffortResultTag;
            string normText = b.BenchmarkAvgVolume.HasValue ? $"{b.BenchmarkAvgVolume:N0}" : "—";
            string tradesText = $"{b.TradesCount} (чек: {b.AvgTradeSize:F2})";

            string tooltipContent =
                $"⏰ Минута: {b.Time}\n" +
                $"💵 Close: {b.Close:F1} (размах: {b.PriceRange:F1} п.)\n" +
                $"📊 Объем: {b.Volume:N0} к. (RVol: {b.RVol:F2}x | норма: {normText})\n" +
                $"🤝 Сделки: {tradesText}\n" +
                $"🎯 VSA: {vsaText}\n" +
                $"📈 Дельта ОИ: {oiSign}{b.OpenInterestChange:F0} к.\n" +
                $"⚖️ VWAP дня: {b.VWAP:F1}";

            var ft = new FormattedText(
                tooltipContent,
                InvCulture, FlowDirection.LeftToRight,
                RegularTypeface, 10, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            double tipW = ft.Width + 16;
            double tipH = ft.Height + 14;

            // Позиционируем плашку правее или левее курсора, чтобы она не вылезала за края
            double tx = _mousePos.Value.X + 15;
            if (tx + tipW > ActualWidth - 70)
            {
                tx = _mousePos.Value.X - tipW - 15;
            }

            double ty = Math.Clamp(_mousePos.Value.Y - 20, 35, ActualHeight - tipH - 25);

            // Тень и рамка тултипа
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(240, 15, 23, 42)), new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 1.2),
                new Rect(tx, ty, tipW, tipH), 6, 6);
            dc.DrawText(ft, new Point(tx + 8, ty + 7));
        }
    }
}
