using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MarketStatsViewer;

/// <summary>
/// Окно подробного иллюстрированного руководства пользователя и справочника по всем блокам терминала.
/// </summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        LoadHelpImages();
    }

    private void LoadHelpImages()
    {
        LoadImageSafe(ImgTopPanel, "top-panel.png");
        LoadImageSafe(ImgPrice, "price.png");
        LoadImageSafe(ImgVolume, "volume.png");
        LoadImageSafe(ImgContracts, "contracts.png");
        LoadImageSafe(ImgOpenInterest, "open-interest.png");
        LoadImageSafe(ImgRvolCurrent, "rvol-current.png");
        LoadImageSafe(ImgMiddlePanel, "middle-panel.png");
        LoadImageSafe(ImgForecast515, "5-15min.png");
        LoadImageSafe(ImgForecast3060, "30-60min.png");
        LoadImageSafe(ImgTable, "table.png");
    }

    private static void LoadImageSafe(System.Windows.Controls.Image imgControl, string fileName)
    {
        try
        {
            var path = ResolveImagePath(fileName);
            if (File.Exists(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                imgControl.Source = bitmap;
            }
        }
        catch
        {
            // Игнорируем ошибки отсутствия файла
        }
    }

    private static string ResolveImagePath(string fileName)
    {
        // 1. Папка help рядом с исполняемым файлом
        var candidate1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "help", fileName);
        if (File.Exists(candidate1)) return candidate1;

        // 2. Родительская папка help
        var candidate2 = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "help", fileName));
        if (File.Exists(candidate2)) return candidate2;

        // 3. Папка исходников src/MarketStatsViewer/help
        var candidate3 = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "help", fileName));
        if (File.Exists(candidate3)) return candidate3;

        // 4. Папка C:\QUIK_VTB\lua\MarketStats\help
        var candidate4 = Path.Combine(@"C:\QUIK_VTB\lua\MarketStats\help", fileName);
        if (File.Exists(candidate4)) return candidate4;

        return candidate1;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ScrollToElement(FrameworkElement element)
    {
        element.BringIntoView();
    }

    private void Nav1_Click(object sender, RoutedEventArgs e) => ScrollToElement(Sec1);
    private void Nav2_Click(object sender, RoutedEventArgs e) => ScrollToElement(Sec2);
    private void Nav3_Click(object sender, RoutedEventArgs e) => ScrollToElement(Sec3);
    private void Nav4_Click(object sender, RoutedEventArgs e) => ScrollToElement(Sec4);
    private void Nav5_Click(object sender, RoutedEventArgs e) => ScrollToElement(Sec5);
    private void Nav6_Click(object sender, RoutedEventArgs e) => ScrollToElement(Sec6);
    private void Nav7_Click(object sender, RoutedEventArgs e) => ScrollToElement(Sec7);
    private void Nav8_Click(object sender, RoutedEventArgs e) => ScrollToElement(Sec8);
    private void Nav9_Click(object sender, RoutedEventArgs e) => ScrollToElement(Sec9);
    private void Nav10_Click(object sender, RoutedEventArgs e) => ScrollToElement(Sec10);
    private void Nav11_Click(object sender, RoutedEventArgs e) => ScrollToElement(Sec11);
}
