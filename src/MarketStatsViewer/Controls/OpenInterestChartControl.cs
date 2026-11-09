using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using MarketStatsViewer.Models;

namespace MarketStatsViewer.Controls;

/// <summary>
/// Р“СЂР°С„РёРє РєСѓРјСѓР»СЏС‚РёРІРЅРѕРіРѕ РћС‚РєСЂС‹С‚РѕРіРѕ РРЅС‚РµСЂРµСЃР° (Open Interest Chart):
/// РџРѕРєР°Р·С‹РІР°РµС‚ РґРёРЅР°РјРёРєСѓ РїСЂРёС‚РѕРєР° (+) РёР»Рё РѕС‚С‚РѕРєР° (-) РєР°РїРёС‚Р°Р»Р° РІ РїРѕР·РёС†РёРё СЃ РЅР°С‡Р°Р»Р° С‚РѕСЂРіРѕРІРѕР№ СЃРµСЃСЃРёРё.
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
    private static readonly SolidColorBrush OiPosBrush = new(Color.FromArgb(70, 16, 185, 129)); // Soft green fill
    private static readonly SolidColorBrush OiNegBrush = new(Color.FromArgb(70, 239, 68, 68));   // Soft red fill

    private static readonly FontFamily ChartFontFamily = new("Segoe UI");
    private static readonly Typeface RegularTypeface = new(ChartFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface BoldTypeface = new(ChartFontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public OpenInterestChartControl()
    {
        ClipToBounds = true;
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
            var txt = new FormattedText("РћР¶РёРґР°РЅРёРµ РґР°РЅРЅС‹С… РћС‚РєСЂС‹С‚РѕРіРѕ РРЅС‚РµСЂРµСЃР°...", InvCulture, FlowDirection.LeftToRight,
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

        // РџРѕРёСЃРє РјРёРЅРёРјСѓРјР° Рё РјР°РєСЃРёРјСѓРјР° РєСѓРјСѓР»СЏС‚РёРІРЅРѕРіРѕ РћР
        double minOi = barList.Min(b => b.CumulativeOiChange);
        double maxOi = barList.Max(b => b.CumulativeOiChange);

        // Р’СЃРµРіРґР° РІРєР»СЋС‡Р°РµРј 0 РІ С€РєР°Р»Сѓ
        minOi = Math.Min(minOi, 0);
        maxOi = Math.Max(maxOi, 0);

        double spread = Math.Max(20.0, maxOi - minOi) * 1.15;
        double centerOffset = (minOi + maxOi) / 2.0;
        double plotMin = centerOffset - spread / 2.0;
        double plotMax = centerOffset + spread / 2.0;
        double rangeOi = plotMax - plotMin;

        // Р—Р°РіРѕР»РѕРІРѕРє
        double curOi = barList[^1].CumulativeOiChange;
        string sign = curOi >= 0 ? "+" : "";
        var title = new FormattedText($"РљРЈРњРЈР›РЇРўРР’РќР«Р™ РћРўРљР Р«РўР«Р™ РРќРўР•Р Р•РЎ (Р”Р•Р›Р¬РўРђ: {sign}{curOi:F0} РєРѕРЅС‚СЂ.)", InvCulture, FlowDirection.LeftToRight,
            BoldTypeface, 10, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(title, new Point(marginL, 6));

        // РќСѓР»РµРІР°СЏ Р»РёРЅРёСЏ
        double yZero = marginT + plotH - ((0 - plotMin) / rangeOi) * plotH;
        dc.DrawLine(ZeroPen, new Point(marginL, yZero), new Point(marginL + plotW, yZero));

        var zeroLbl = new FormattedText("0", InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(zeroLbl, new Point(marginL + plotW + 4, yZero - zeroLbl.Height / 2.0));

        // РњРµС‚РєРё РјР°РєСЃРёРјСѓРјР° Рё РјРёРЅРёРјСѓРјР°
        var maxLbl = new FormattedText($"{plotMax:+0;-0;0}", InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(maxLbl, new Point(marginL + plotW + 4, marginT));

        var minLbl = new FormattedText($"{plotMin:+0;-0;0}", InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(minLbl, new Point(marginL + plotW + 4, marginT + plotH - minLbl.Height));

        // РџРѕСЃС‚СЂРѕРµРЅРёРµ С‚РѕС‡РµРє
        double stepX = plotW / Math.Max(1, barList.Count - 1);
        var points = new List<Point>();

        for (int i = 0; i < barList.Count; i++)
        {
            var b = barList[i];
            double x = marginL + i * stepX;
            double y = marginT + plotH - ((b.CumulativeOiChange - plotMin) / rangeOi) * plotH;
            points.Add(new Point(x, y));
        }

        // РћС‚СЂРёСЃРѕРІРєР° Р»РёРЅРёРё РћР
        for (int i = 0; i < points.Count - 1; i++)
        {
            dc.DrawLine(OiPen, points[i], points[i + 1]);
        }

        // РџРѕРґРїРёСЃСЊ РѕСЃРё X
        var startTxt = new FormattedText(barList[0].ShortTime, InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(startTxt, new Point(marginL, h - marginB + 3));

        var endTxt = new FormattedText(barList[^1].ShortTime, InvCulture, FlowDirection.LeftToRight,
            RegularTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(endTxt, new Point(marginL + plotW - endTxt.Width, h - marginB + 3));
    }
}
