using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MarketStatsViewer.Models;
using MarketStatsViewer.Services;

namespace MarketStatsViewer.ViewModels;

/// <summary>
/// Главная ViewModel приложения.
/// Управляет состоянием пользовательского интерфейса, связывает базу данных SQLite,
/// фоновую службу синхронизации файлов QUIK и аналитический движок VSA.
/// </summary>
public class MainViewModel : BaseViewModel
{
    private readonly DatabaseService _db;
    private readonly DataSyncService _syncService;
    private readonly MarketAnalyticsService _analyticsService;

    private List<MinuteBar> _loadedDbBars = new();
    private Dictionary<string, IntradayBenchmark> _loadedBenchmarks = new();

    private string _selectedSecCode = "GDZ6";
    private string _selectedSession = "";
    private bool _isLiveMode = true;
    private string _statusText = "Готов к работе";
    private string _lastUpdateTime = "--:--:--";
    private bool _isHelpOpen = false;

    // Свойства текущего рыночного состояния
    private double _lastPrice;
    private double _priceChange;
    private double _priceChangePct;
    private double _dayVolume;
    private int _dayTrades;
    private double _dayTurnover;
    private double _dayOiChange;
    private double _currentRVol = 1.0;
    private double _cumulativeRVol = 1.0;
    private double _dayVwap;

    // Сравнительные показатели с нормой полного торгового дня
    private DailyBenchmark _dailyBenchmark = new();
    private string _volumeComparisonText = "Ожидание данных...";
    private string _tradesComparisonText = "Ожидание данных...";

    private EffortResultAnalysis _effortResult = new();
    private HorizonForecast _forecastShort = new() { HorizonTitle = "Горизонт 5–15 минут" };
    private HorizonForecast _forecastMedium = new() { HorizonTitle = "Горизонт 30–60 минут" };

    /// <summary>
    /// Список доступных торговых инструментов (GDZ6, BRX6, SiZ6 и т.д.).
    /// </summary>
    public ObservableCollection<string> AvailableSecCodes { get; } = new();

    /// <summary>
    /// Список доступных сессий QUIK.
    /// </summary>
    public ObservableCollection<string> AvailableSessions { get; } = new();

    /// <summary>
    /// Коллекция последних минутных баров для отображения в таблице потока.
    /// </summary>
    public ObservableCollection<MinuteBar> RecentBars { get; } = new();

    /// <summary>
    /// Команда принудительного обновления аналитики.
    /// </summary>
    public ICommand RefreshCommand { get; }

    /// <summary>
    /// Команда полного пересканирования и пересчета базы данных.
    /// </summary>
    public ICommand ResyncAllHistoryCommand { get; }

    /// <summary>
    /// Команда включения/выключения режима онлайн-наблюдения.
    /// </summary>
    public ICommand ToggleLiveCommand { get; }

    /// <summary>
    /// Команда открытия/закрытия окна встроенной справки и глоссария терминов.
    /// </summary>
    public ICommand ToggleHelpCommand { get; }

    /// <summary>
    /// Конструктор MainViewModel. Инициализирует сервисы, команды и запускает загрузку сессий.
    /// </summary>
    public MainViewModel()
    {
        _db = new DatabaseService();
        _syncService = new DataSyncService(_db);
        _analyticsService = new MarketAnalyticsService(_db);

        RefreshCommand = new RelayCommand(async () => await RefreshDataAsync());
        ResyncAllHistoryCommand = new RelayCommand(async () => await ResyncHistoryAsync());
        ToggleLiveCommand = new RelayCommand(() =>
        {
            IsLiveMode = !IsLiveMode;
            if (IsLiveMode)
            {
                _syncService.StartLiveWatch(TimeSpan.FromSeconds(2));
            }
            else
            {
                _syncService.StopLiveWatch();
            }
        });

        ToggleHelpCommand = new RelayCommand(() =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var helpWin = new HelpWindow();
                helpWin.Owner = Application.Current?.MainWindow;
                helpWin.Show();
            });
        });

        _syncService.BarsUpdated += OnBarsUpdated;
        _syncService.LiveBarUpdated += OnLiveBarUpdated;
        _syncService.StatusChanged += msg => StatusText = msg;

        // Автоматический запуск начальной синхронизации
        _ = InitializeAsync();
    }

    /// <summary>
    /// Выбранный в интерфейсе инструмент.
    /// </summary>
    public string SelectedSecCode
    {
        get => _selectedSecCode;
        set
        {
            if (SetProperty(ref _selectedSecCode, value))
            {
                _ = RefreshDataAsync();
            }
        }
    }

    /// <summary>
    /// Выбранная сессия (по умолчанию активная последняя сессия).
    /// </summary>
    public string SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                _ = RefreshDataAsync();
            }
        }
    }

    /// <summary>
    /// Флаг активности режима живого слежения за файлами QUIK.
    /// </summary>
    public bool IsLiveMode
    {
        get => _isLiveMode;
        set => SetProperty(ref _isLiveMode, value);
    }

    /// <summary>
    /// Флаг открытия панели справки и терминов.
    /// </summary>
    public bool IsHelpOpen
    {
        get => _isHelpOpen;
        set => SetProperty(ref _isHelpOpen, value);
    }

    /// <summary>
    /// Строка статуса в нижней панели окна.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>
    /// Время последнего полученного минутного бара ("HH:mm:ss").
    /// </summary>
    public string LastUpdateTime
    {
        get => _lastUpdateTime;
        set => SetProperty(ref _lastUpdateTime, value);
    }

    /// <summary>
    /// Последняя зафиксированная цена сделки (Close последнего бара).
    /// </summary>
    public double LastPrice
    {
        get => _lastPrice;
        set => SetProperty(ref _lastPrice, value);
    }

    /// <summary>
    /// Изменение цены с начала текущей сессии в пунктах.
    /// </summary>
    public double PriceChange
    {
        get => _priceChange;
        set => SetProperty(ref _priceChange, value);
    }

    /// <summary>
    /// Процентное изменение цены с начала сессии.
    /// </summary>
    public double PriceChangePct
    {
        get => _priceChangePct;
        set => SetProperty(ref _priceChangePct, value);
    }

    /// <summary>
    /// Суммарный объем проторгованных контрактов за текущий день.
    /// </summary>
    public double DayVolume
    {
        get => _dayVolume;
        set => SetProperty(ref _dayVolume, value);
    }

    /// <summary>
    /// Суммарное количество сделок за текущий день.
    /// </summary>
    public int DayTrades
    {
        get => _dayTrades;
        set => SetProperty(ref _dayTrades, value);
    }

    /// <summary>
    /// Суммарный денежный оборот за текущий день в рублях.
    /// </summary>
    public double DayTurnover
    {
        get => _dayTurnover;
        set => SetProperty(ref _dayTurnover, value);
    }

    /// <summary>
    /// Накопленное изменение открытого интереса с открытия сессии.
    /// </summary>
    public double DayOiChange
    {
        get => _dayOiChange;
        set => SetProperty(ref _dayOiChange, value);
    }

    /// <summary>
    /// Коэффициент относительного объема (RVol) за последнюю минуту по отношению к норме времени.
    /// </summary>
    public double CurrentRVol
    {
        get => _currentRVol;
        set => SetProperty(ref _currentRVol, value);
    }

    /// <summary>
    /// Кумулятивный RVol дня: во сколько раз дневной оборот к этой минуте выше или ниже среднего.
    /// </summary>
    public double CumulativeRVol
    {
        get => _cumulativeRVol;
        set => SetProperty(ref _cumulativeRVol, value);
    }

    /// <summary>
    /// Дневная средневзвешенная по объему цена (VWAP).
    /// </summary>
    public double DayVwap
    {
        get => _dayVwap;
        set => SetProperty(ref _dayVwap, value);
    }

    /// <summary>
    /// Исторический суточный бенчмарк для выбранного инструмента.
    /// </summary>
    public DailyBenchmark DailyBenchmark
    {
        get => _dailyBenchmark;
        set => SetProperty(ref _dailyBenchmark, value);
    }

    /// <summary>
    /// Строка сравнения объема дня со среднесуточным историческим показателем.
    /// </summary>
    public string VolumeComparisonText
    {
        get => _volumeComparisonText;
        set => SetProperty(ref _volumeComparisonText, value);
    }

    /// <summary>
    /// Строка сравнения сделок и среднего чека с историческими нормами.
    /// </summary>
    public string TradesComparisonText
    {
        get => _tradesComparisonText;
        set => SetProperty(ref _tradesComparisonText, value);
    }

    /// <summary>
    /// Аналитическая оценка текущего состояния по закону «Усилие против Результата» (VSA).
    /// </summary>
    public EffortResultAnalysis EffortResult
    {
        get => _effortResult;
        set => SetProperty(ref _effortResult, value);
    }

    /// <summary>
    /// Прогноз движения на горизонт 5–15 минут.
    /// </summary>
    public HorizonForecast ForecastShort
    {
        get => _forecastShort;
        set => SetProperty(ref _forecastShort, value);
    }

    /// <summary>
    /// Прогноз движения на горизонт 30–60 минут.
    /// </summary>
    public HorizonForecast ForecastMedium
    {
        get => _forecastMedium;
        set => SetProperty(ref _forecastMedium, value);
    }

    /// <summary>
    /// Асинхронно производит первичную синхронизацию базы данных и запускает слежение за файлами.
    /// </summary>
    private async Task InitializeAsync()
    {
        StatusText = "Проверка данных и синхронизация сессий...";
        await _syncService.SyncAllHistoryAsync(new Progress<string>(s => StatusText = s));

        LoadAvailableInstrumentsAndSessions();

        _syncService.StartLiveWatch(TimeSpan.FromSeconds(2));
        await RefreshDataAsync();
    }

    /// <summary>
    /// Принудительно повторно сканирует все папки сессий на диске.
    /// </summary>
    public async Task ResyncHistoryAsync()
    {
        StatusText = "Полная перепроверка исторических файлов...";
        await _syncService.SyncAllHistoryAsync(new Progress<string>(s => StatusText = s));
        LoadAvailableInstrumentsAndSessions();
        await RefreshDataAsync();
    }

    /// <summary>
    /// Заполняет выпадающие списки доступных инструментов и сессий.
    /// </summary>
    private void LoadAvailableInstrumentsAndSessions()
    {
        var secCodes = _db.GetAvailableSecCodes();
        Application.Current?.Dispatcher.Invoke(() =>
        {
            AvailableSecCodes.Clear();
            foreach (var code in secCodes)
            {
                AvailableSecCodes.Add(code);
            }

            if (!AvailableSecCodes.Contains(_selectedSecCode))
            {
                if (AvailableSecCodes.Contains("GDZ6"))
                    _selectedSecCode = "GDZ6";
                else if (AvailableSecCodes.Count > 0)
                    _selectedSecCode = AvailableSecCodes[0];
            }
            OnPropertyChanged(nameof(SelectedSecCode));

            var sessions = _syncService.DiscoverSessionDirectories();
            AvailableSessions.Clear();
            foreach (var s in sessions)
            {
                AvailableSessions.Add(s);
            }

            if (string.IsNullOrEmpty(_selectedSession) && AvailableSessions.Count > 0)
            {
                _selectedSession = AvailableSessions[0];
            }
            OnPropertyChanged(nameof(SelectedSession));
        });
    }

    /// <summary>
    /// Обработчик события появления новых баров в дописываемых файлах QUIK.
    /// </summary>
    private void OnBarsUpdated(string sessionId, string secCode)
    {
        if (secCode == SelectedSecCode)
        {
            Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                await RefreshDataAsync();
            });
        }
    }

    /// <summary>
    /// Обработчик оперативного тикового обновления текущей формирующейся минуты (каждые 2 секунды).
    /// Обновляет текущую цену, объем, сделки и верхнюю строку таблицы без ожидания закрытия минуты.
    /// </summary>
    private void OnLiveBarUpdated(string sessionId, string secCode, MinuteBar liveBar)
    {
        if (secCode != SelectedSecCode || sessionId != SelectedSession)
            return;

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (_loadedDbBars.Count == 0) return;

            var lastDb = _loadedDbBars[^1];
            // Проверяем, относится ли liveBar к текущей или новой минуте по отношению к закрытой истории
            if (string.Compare(liveBar.Time, lastDb.Time, StringComparison.Ordinal) >= 0)
            {
                if (_loadedBenchmarks.TryGetValue(liveBar.Time, out var bm) && bm.AvgVolume > 0)
                {
                    liveBar.BenchmarkAvgVolume = bm.AvgVolume;
                    liveBar.RVol = Math.Round(liveBar.Volume / bm.AvgVolume, 2);
                    if (bm.AvgCumulativeVolume > 0)
                    {
                        liveBar.CumRVol = Math.Round((lastDb.CumulativeVolume + liveBar.Volume) / bm.AvgCumulativeVolume, 2);
                    }
                }
                else
                {
                    liveBar.RVol = 1.0;
                    liveBar.CumRVol = 1.0;
                }

                liveBar.DataStatus = "LIVE ⏳";
                liveBar.EffortResultTag = "Формируется...";
                liveBar.EffortResultColor = "#0284C7";
                liveBar.CumulativeVolume = lastDb.CumulativeVolume + liveBar.Volume;
                liveBar.CumulativeTrades = lastDb.CumulativeTrades + liveBar.TradesCount;
                liveBar.CumulativeTurnover = lastDb.CumulativeTurnover + liveBar.Turnover;
                liveBar.CumulativeOiChange = lastDb.CumulativeOiChange + (liveBar.OpenInterestChange ?? 0.0);

                // Мгновенное обновление цен и объемов в карточках
                LastPrice = liveBar.Close;
                PriceChange = Math.Round(liveBar.Close - _loadedDbBars[0].Open, 2);
                PriceChangePct = _loadedDbBars[0].Open > 0 ? Math.Round((PriceChange / _loadedDbBars[0].Open) * 100, 2) : 0;

                DayVolume = liveBar.CumulativeVolume;
                DayTrades = liveBar.CumulativeTrades;
                DayTurnover = liveBar.CumulativeTurnover;
                DayOiChange = liveBar.CumulativeOiChange;
                CurrentRVol = liveBar.RVol;
                CumulativeRVol = liveBar.CumRVol;
                LastUpdateTime = $"{liveBar.LastTradeTime} (live)";

                // Обновление таблицы потока: текущий бар всегда на самом верху
                if (RecentBars.Count > 0 && RecentBars[0].DataStatus.StartsWith("LIVE"))
                {
                    RecentBars[0] = liveBar;
                }
                else
                {
                    RecentBars.Insert(0, liveBar);
                    if (RecentBars.Count > 40)
                    {
                        RecentBars.RemoveAt(RecentBars.Count - 1);
                    }
                }

                // Пересчет VSA и прогноза на лету с учетом тиков текущей минуты
                var combined = new List<MinuteBar>(_loadedDbBars);
                if (combined[^1].Time == liveBar.Time) combined[^1] = liveBar;
                else combined.Add(liveBar);

                var effort = _analyticsService.EvaluateEffortVsResult(combined, _loadedBenchmarks);
                var (shortF, medF) = _analyticsService.GenerateDualForecast(combined, SelectedSecCode, SelectedSession, effort);
                EffortResult = effort;
                ForecastShort = shortF;
                ForecastMedium = medF;

                StatusText = $"LIVE поток активен | {DateTime.Now:HH:mm:ss} | Сделок в минуте: {liveBar.TradesCount} | Объем: {liveBar.Volume:N0}";
            }
        });
    }

    /// <summary>
    /// Загружает бары текущей сессии, вычисляет бенчмарки, VSA и двухгоризонтный прогноз, обновляя свойства UI.
    /// </summary>
    public async Task RefreshDataAsync()
    {
        if (string.IsNullOrEmpty(SelectedSecCode) || string.IsNullOrEmpty(SelectedSession))
            return;

        await Task.Run(() =>
        {
            var bars = _db.GetSessionBars(SelectedSession, SelectedSecCode);
            var benchmarks = _db.GetHistoricalBenchmarks(SelectedSecCode, SelectedSession);
            var dailyBm = _db.GetDailyBenchmark(SelectedSecCode, SelectedSession);

            _loadedDbBars = bars;
            _loadedBenchmarks = benchmarks;

            _analyticsService.EnrichBars(bars, benchmarks);

            var effort = _analyticsService.EvaluateEffortVsResult(bars, benchmarks);
            var (shortFcast, medFcast) = _analyticsService.GenerateDualForecast(bars, SelectedSecCode, SelectedSession, effort);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                EffortResult = effort;
                ForecastShort = shortFcast;
                ForecastMedium = medFcast;
                DailyBenchmark = dailyBm;

                if (bars.Count > 0)
                {
                    var last = bars[^1];
                    LastPrice = last.Close;
                    PriceChange = Math.Round(last.Close - bars[0].Open, 2);
                    PriceChangePct = bars[0].Open > 0 ? Math.Round((PriceChange / bars[0].Open) * 100, 2) : 0;

                    DayVolume = last.CumulativeVolume;
                    DayTrades = last.CumulativeTrades;
                    DayTurnover = last.CumulativeTurnover;
                    DayOiChange = last.CumulativeOiChange;
                    CurrentRVol = last.RVol;
                    CumulativeRVol = last.CumRVol;
                    DayVwap = Math.Round(last.VWAP, 1);
                    LastUpdateTime = last.Time;

                    // Сравнение дневного объема со средней нормой за полный день
                    if (dailyBm.AvgFullDayVolume > 0)
                    {
                        double pctOfDay = (last.CumulativeVolume / dailyBm.AvgFullDayVolume) * 100.0;
                        string diffSign = last.CumRVol >= 1.0 ? "+" : "";
                        double rvolPct = (last.CumRVol - 1.0) * 100.0;
                        VolumeComparisonText = $"Норма дня: {dailyBm.AvgFullDayVolume:N0} к. ({pctOfDay:F0}% проторговано) | К норме времени: {diffSign}{rvolPct:F0}%";
                    }
                    else
                    {
                        VolumeComparisonText = $"Кумул. RVol: {last.CumRVol:F2}x от типичной активности";
                    }

                    // Сравнение числа сделок и среднего размера чека
                    if (dailyBm.AvgFullDayTrades > 0)
                    {
                        double currentAvgSize = last.AvgTradeSize > 0 ? last.AvgTradeSize : (last.CumulativeTrades > 0 ? last.CumulativeVolume / last.CumulativeTrades : 1.0);
                        double sizeDiffPct = dailyBm.TypicalAvgTradeSize > 0 
                            ? ((currentAvgSize - dailyBm.TypicalAvgTradeSize) / dailyBm.TypicalAvgTradeSize) * 100.0 
                            : 0;
                        string sizeSign = sizeDiffPct >= 0 ? "+" : "";
                        TradesComparisonText = $"Ср. чек: {currentAvgSize:F2} к./сд. (норма {dailyBm.TypicalAvgTradeSize:F2}, {sizeSign}{sizeDiffPct:F0}%) | Норма дня: {dailyBm.AvgFullDayTrades:N0} сдел.";
                    }
                    else
                    {
                        TradesComparisonText = $"Оборот: {last.CumulativeTurnover:N0} ₽";
                    }

                    RecentBars.Clear();
                    // Отображаем последние 40 баров в обратном хронологическом порядке (самый свежий сверху)
                    foreach (var b in bars.TakeLast(40).Reverse())
                    {
                        RecentBars.Add(b);
                    }
                }
                else
                {
                    RecentBars.Clear();
                    LastPrice = 0;
                    PriceChange = 0;
                    PriceChangePct = 0;
                    DayVolume = 0;
                    DayTrades = 0;
                    DayTurnover = 0;
                    DayOiChange = 0;
                    CurrentRVol = 1.0;
                    CumulativeRVol = 1.0;
                    VolumeComparisonText = "Нет данных по выбранной сессии";
                    TradesComparisonText = "Нет данных по выбранной сессии";
                }

                StatusText = $"Данные обновлены: {DateTime.Now:HH:mm:ss} | Сессия: {SelectedSession} | Инструмент: {SelectedSecCode} ({bars.Count} баров) | В истории: {dailyBm.TotalDaysCount} дн.";
            });
        });
    }
}
