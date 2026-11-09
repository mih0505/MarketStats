using System.Windows;
using MarketStatsViewer.ViewModels;

namespace MarketStatsViewer;

/// <summary>
/// Главное окно WPF приложения MarketStatsViewer.
/// Инициализирует визуальные компоненты XAML и привязывает контекст данных к MainViewModel.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Конструктор окна. Загружает разметку XAML и создает экземпляр MainViewModel.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}