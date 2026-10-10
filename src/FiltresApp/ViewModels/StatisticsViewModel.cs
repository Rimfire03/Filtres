using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Services;
using FiltresApp.Core.Services.Licensing;
using FiltresApp.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace FiltresApp.ViewModels;

/// <summary>Écran « Statistiques » : analyse du site courant, module libre de licence. Chaque onglet (Filtres,
/// Encrassement, Courroies, Roulements) n'apparaît que si le module correspondant est couvert par la licence et
/// activé ; la vue d'ensemble ne compte que ces modules. Les calculs sont dans <see cref="StatisticsService"/>.</summary>
public partial class StatisticsViewModel : ObservableObject, IReloadable
{
    private static readonly string[] Palette = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"];
    private static readonly string[] ShortMonths = ["Jan", "Fév", "Mar", "Avr", "Mai", "Juin", "Juil", "Août", "Sep", "Oct", "Nov", "Déc"];

    private bool _loading;

    // ---- Sélections ----
    /// <summary>Année analysée : celle du sélecteur du menu de gauche (<see cref="YearContext"/>).</summary>
    [ObservableProperty] private int _selectedYear = App.YearContext.Year;
    [ObservableProperty] private int _foulingMonths = 12;
    [ObservableProperty] private int _bearingYears = 5;

    // ---- Visibilité des onglets (licence + module activé) ----
    [ObservableProperty] private bool _showFilters;
    [ObservableProperty] private bool _showFouling;
    [ObservableProperty] private bool _showBelts;
    [ObservableProperty] private bool _showBearings;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowContent))] private bool _showNothing;
    public bool ShowContent => !ShowNothing;

    // ---- Vue d'ensemble ----
    [ObservableProperty] private string _dueThisMonth = "-";
    [ObservableProperty] private string _overdueCount = "-";
    [ObservableProperty] private string _yearCompletion = "-";
    [ObservableProperty] private string _yearTotalDue = "-";
    [ObservableProperty] private string _peakText = "";
    [ObservableProperty] private string _statusMessage = "";

    // ---- Filtres périodiques ----
    [ObservableProperty] private IReadOnlyList<MonthlyWorkloadRow> _workload = [];
    [ObservableProperty] private IReadOnlyList<OverdueRow> _overdue = [];
    [ObservableProperty] private IReadOnlyList<OperatingHoursRow> _operatingHours = [];
    [ObservableProperty] private ISeries[] _workloadSeries = [];
    [ObservableProperty] private ISeries[] _viewSeries = [];
    [ObservableProperty] private ISeries[] _completionSeries = [];
    public Axis[] MonthAxes { get; } = [new Axis { Labels = ShortMonths, LabelsPaint = new SolidColorPaint(SKColor.Parse("#52514e")) }];
    [ObservableProperty] private bool _hasOperatingHours;

    // ---- Encrassement ----
    [ObservableProperty] private IReadOnlyList<YearlyRow> _foulingYearly = [];
    [ObservableProperty] private IReadOnlyList<IntervalRow> _foulingIntervals = [];
    [ObservableProperty] private IReadOnlyList<UnchangedRow> _foulingUnchanged = [];
    [ObservableProperty] private ISeries[] _foulingSeries = [];
    [ObservableProperty] private Axis[] _foulingAxes = [];

    // ---- Courroies ----
    [ObservableProperty] private IReadOnlyList<BeltYearRow> _beltYearly = [];
    [ObservableProperty] private IReadOnlyList<IntervalRow> _beltIntervals = [];
    [ObservableProperty] private IReadOnlyList<BeltStockRow> _beltStock = [];
    [ObservableProperty] private ISeries[] _beltSeries = [];
    [ObservableProperty] private Axis[] _beltAxes = [];

    // ---- Roulements ----
    [ObservableProperty] private IReadOnlyList<BearingYearRow> _bearingYearly = [];
    [ObservableProperty] private IReadOnlyList<IntervalRow> _bearingIntervals = [];
    [ObservableProperty] private IReadOnlyList<UnchangedRow> _bearingUnchanged = [];
    [ObservableProperty] private ISeries[] _bearingSeries = [];
    [ObservableProperty] private Axis[] _bearingAxes = [];

    public StatisticsViewModel()
    {
        App.YearContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(YearContext.Year)) SelectedYear = App.YearContext.Year;
        };
        Reload();
    }

    partial void OnSelectedYearChanged(int value) { if (!_loading) Reload(); }
    partial void OnFoulingMonthsChanged(int value) { if (!_loading) Reload(); }
    partial void OnBearingYearsChanged(int value) { if (!_loading) Reload(); }

    public void Reload()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var settings = App.Settings;
            ShowFilters = LicenseManager.HasFeature("filtres") && settings.ShowFiltresModule;
            ShowFouling = ShowFilters;
            ShowBelts = LicenseManager.HasFeature("courroies") && settings.ShowBeltsModule;
            ShowBearings = LicenseManager.HasFeature("roulements") && settings.ShowBearingsModule;
            ShowNothing = !(ShowFilters || ShowBelts || ShowBearings);
            FoulingMonths = Math.Clamp(FoulingMonths, 1, 120);
            BearingYears = Math.Clamp(BearingYears, 1, 50);

            using var ctx = App.DbFactory.Create();
            var today = DateOnly.FromDateTime(DateTime.Today);

            if (ShowFilters) LoadFilters(ctx, today); else ClearFilters();
            if (ShowFouling) LoadFouling(ctx, today); else ClearFouling();
            if (ShowBelts) LoadBelts(ctx, today); else { BeltYearly = []; BeltIntervals = []; BeltStock = []; BeltSeries = []; }
            if (ShowBearings) LoadBearings(ctx, today); else { BearingYearly = []; BearingIntervals = []; BearingUnchanged = []; BearingSeries = []; }
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            StatusMessage = "Statistiques indisponibles : " + ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    // ---- Chargements ----

    private void LoadFilters(Core.Data.FiltresDbContext ctx, DateOnly today)
    {
        var rows = StatisticsService.MonthlyWorkload(ctx, SelectedYear);
        Workload = rows;
        Overdue = StatisticsService.Overdue(ctx, today);
        OperatingHours = StatisticsService.OperatingHours(ctx);
        HasOperatingHours = OperatingHours.Count > 0;

        WorkloadSeries =
        [
            Column("Filtres à changer", rows.Select(r => r.FiltersDue), 0),
            Column("Filtres à laver", rows.Select(r => r.ToWash), 3),
            new LineSeries<int>
            {
                Name = "Réalisés", Values = rows.Select(r => r.DoneOfDue).ToArray(), Fill = null,
                Stroke = new SolidColorPaint(SKColor.Parse(Palette[1]), 2.5f),
                GeometrySize = 8, GeometryStroke = new SolidColorPaint(SKColor.Parse(Palette[1]), 2.5f),
                GeometryFill = new SolidColorPaint(SKColors.White), LineSmoothness = 0.4
            }
        ];

        var views = rows.SelectMany(r => r.DueByView.Keys).Distinct().OrderBy(v => v).ToList();
        ViewSeries = views.Select((v, i) => (ISeries)new StackedColumnSeries<int>
        {
            Name = v, Values = rows.Select(r => r.DueByView.GetValueOrDefault(v)).ToArray(),
            Fill = new SolidColorPaint(SKColor.Parse(Palette[i % Palette.Length]))
        }).Append(new StackedColumnSeries<int>
        {
            Name = "Filtres à laver", Values = rows.Select(r => r.ToWash).ToArray(),
            Fill = new SolidColorPaint(SKColor.Parse(Palette[views.Count % Palette.Length]))
        }).ToArray();

        CompletionSeries =
        [
            new LineSeries<double?>
            {
                Name = "Taux de réalisation (%)", Values = rows.Select(r => r.CompletionRate is { } c ? Math.Round(c * 100) : (double?)null).ToArray(),
                Fill = null, Stroke = new SolidColorPaint(SKColor.Parse(Palette[2]), 2.5f),
                GeometrySize = 8, GeometryStroke = new SolidColorPaint(SKColor.Parse(Palette[2]), 2.5f),
                GeometryFill = new SolidColorPaint(SKColors.White), LineSmoothness = 0.4
            }
        ];

        var thisMonth = rows[today.Month - 1];
        DueThisMonth = today.Year == SelectedYear ? thisMonth.Total.ToString() : "-";
        OverdueCount = Overdue.Count.ToString();
        var totalDue = rows.Sum(r => r.Total);
        var totalDone = rows.Sum(r => r.DoneOfDue);
        YearTotalDue = totalDue.ToString();
        YearCompletion = totalDue == 0 ? "-" : $"{(double)totalDone / totalDue:P0}";
        var withLoad = rows.Where(r => r.Total > 0).ToList();
        if (withLoad.Count == 0) PeakText = "";
        else
        {
            var avg = rows.Average(r => r.Total);
            var max = rows.MaxBy(r => r.Total)!;
            var min = rows.MinBy(r => r.Total)!;
            PeakText = $"Pic : {max.MonthName} ({max.Total} filtres, {max.Total - avg:+0.#;-0.#} par rapport à la moyenne de {avg:0.#}). Creux : {min.MonthName} ({min.Total}).";
        }
    }

    private void ClearFilters()
    {
        Workload = []; Overdue = []; OperatingHours = []; HasOperatingHours = false;
        WorkloadSeries = []; ViewSeries = []; CompletionSeries = [];
        DueThisMonth = OverdueCount = YearCompletion = YearTotalDue = "-"; PeakText = "";
    }

    private void LoadFouling(Core.Data.FiltresDbContext ctx, DateOnly today)
    {
        var yearly = StatisticsService.FoulingYearly(ctx);
        FoulingYearly = yearly;
        FoulingIntervals = StatisticsService.FoulingIntervals(ctx);
        FoulingUnchanged = StatisticsService.FoulingUnchanged(ctx, today, FoulingMonths);

        var years = yearly.Select(r => r.Year).Distinct().OrderBy(y => y).ToList();
        FoulingAxes = [new Axis { Labels = years.Select(y => y.ToString()).ToArray(), LabelsPaint = new SolidColorPaint(SKColor.Parse("#52514e")) }];
        FoulingSeries = yearly.Select(r => r.Label).Distinct().OrderBy(v => v).Select((v, i) => (ISeries)new ColumnSeries<int>
        {
            Name = v,
            Values = years.Select(y => yearly.Where(r => r.Label == v && r.Year == y).Sum(r => r.Changes)).ToArray(),
            Fill = new SolidColorPaint(SKColor.Parse(Palette[i % Palette.Length]))
        }).ToArray();
    }

    private void ClearFouling()
    {
        FoulingYearly = []; FoulingIntervals = []; FoulingUnchanged = []; FoulingSeries = [];
    }

    private void LoadBelts(Core.Data.FiltresDbContext ctx, DateOnly today)
    {
        var yearly = StatisticsService.BeltYearly(ctx);
        BeltYearly = yearly;
        BeltIntervals = StatisticsService.BeltIntervals(ctx);
        BeltStock = StatisticsService.BeltStock(ctx, today);
        BeltAxes = [new Axis { Labels = yearly.Select(r => r.Year.ToString()).ToArray(), LabelsPaint = new SolidColorPaint(SKColor.Parse("#52514e")) }];
        BeltSeries =
        [
            Column("Soufflage", yearly.Select(r => r.Soufflage), 0),
            Column("Extraction", yearly.Select(r => r.Extraction), 1)
        ];
    }

    private void LoadBearings(Core.Data.FiltresDbContext ctx, DateOnly today)
    {
        var yearly = StatisticsService.BearingYearly(ctx);
        BearingYearly = yearly;
        BearingIntervals = StatisticsService.BearingIntervals(ctx);
        BearingUnchanged = StatisticsService.BearingUnchanged(ctx, today, BearingYears);
        BearingAxes = [new Axis { Labels = yearly.Select(r => r.Year.ToString()).ToArray(), LabelsPaint = new SolidColorPaint(SKColor.Parse("#52514e")) }];
        BearingSeries =
        [
            Column("Avant", yearly.Select(r => r.Avant), 0),
            Column("Arrière", yearly.Select(r => r.Arriere), 1),
            Column("Volute", yearly.Select(r => r.Volute), 2)
        ];
    }

    private static ColumnSeries<int> Column(string name, IEnumerable<int> values, int color) => new()
    {
        Name = name, Values = values.ToArray(), Fill = new SolidColorPaint(SKColor.Parse(Palette[color % Palette.Length]))
    };

    // ---- Export ----

    [RelayCommand]
    private void ExportExcel()
    {
        try
        {
            var sheets = new List<StatisticsSheet>();
            if (ShowFilters)
            {
                sheets.Add(new("Charge mensuelle", ["Mois", "Filtres à changer", "Quantité à fournir", "Filtres à laver", "Réalisés", "Taux de réalisation"],
                    Workload.Select(r => new object?[] { r.MonthName, r.FiltersDue, r.QuantityToSupply, r.ToWash, r.DoneOfDue, r.CompletionText }).ToList()));
                sheets.Add(new("Retards", ["Vue", "Emplacement", "Dimension", "Échéance", "Jours de retard", "Quantité"],
                    Overdue.Select(r => new object?[] { r.View, r.Location, r.Dimension, r.MonthText, r.DaysLate, r.Quantity }).ToList()));
                if (HasOperatingHours)
                    sheets.Add(new("Heures de fonctionnement", ["Vue", "Emplacement", "Relevés", "Durée moyenne"],
                        OperatingHours.Select(r => new object?[] { r.View, r.Location, r.Changes, r.AverageText }).ToList()));
            }
            if (ShowFouling)
            {
                sheets.Add(new("Encrassement par année", ["Variété", "Année", "Changements", "Quantité"],
                    FoulingYearly.Select(r => new object?[] { r.Label, r.Year, r.Changes, r.Quantity }).ToList()));
                sheets.Add(new("Encrassement durées", ["Filtre", "Variété", "Changements", "Moyenne", "Minimum", "Maximum", "Dernier changement"],
                    FoulingIntervals.Select(r => new object?[] { r.Name, r.Group, r.Changes, r.AverageText, r.MinText, r.MaxText, r.LastText }).ToList()));
                sheets.Add(new("Encrassement inactifs", ["Filtre", "Variété", "Dernier changement", "Depuis"],
                    FoulingUnchanged.Select(r => new object?[] { r.Name, r.Group, r.LastText, r.SinceText }).ToList()));
            }
            if (ShowBelts)
            {
                sheets.Add(new("Courroies par année", ["Année", "Changements", "Quantité", "Soufflage", "Extraction"],
                    BeltYearly.Select(r => new object?[] { r.Year, r.Changes, r.Quantity, r.Soufflage, r.Extraction }).ToList()));
                sheets.Add(new("Courroies stock minimum", ["Type", "Qté max dans une centrale", "Centrale", "Stock minimum", "Changées sur 12 mois"],
                    BeltStock.Select(r => new object?[] { r.BeltType, r.MaxInOneCentrale, r.Centrale, r.MinimumStock, r.ChangedLast12Months }).ToList()));
                sheets.Add(new("Courroies durées", ["Centrale", "Famille", "Changements", "Intervalle moyen", "Dernier changement"],
                    BeltIntervals.Select(r => new object?[] { r.Name, r.Group, r.Changes, r.AverageText, r.LastText }).ToList()));
            }
            if (ShowBearings)
            {
                sheets.Add(new("Roulements par année", ["Année", "Changements", "Avant", "Arrière", "Volute"],
                    BearingYearly.Select(r => new object?[] { r.Year, r.Changes, r.Avant, r.Arriere, r.Volute }).ToList()));
                sheets.Add(new("Roulements durées", ["Centrale", "Type", "Changements", "Intervalle moyen", "Dernier changement"],
                    BearingIntervals.Select(r => new object?[] { r.Name, r.Group, r.Changes, r.AverageText, r.LastText }).ToList()));
                sheets.Add(new("Roulements inactifs", ["Centrale", "Type", "Dernier changement", "Depuis"],
                    BearingUnchanged.Select(r => new object?[] { r.Name, r.Group, r.LastText, r.SinceText }).ToList()));
            }
            if (sheets.Count == 0)
            {
                StatusMessage = "Aucune statistique à exporter.";
                return;
            }

            var folder = App.Settings.ResolvedPdfExportPath;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Statistiques {SelectedYear} - {DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            StatisticsExcelExport.Export(path, sheets);
            StatusMessage = "Statistiques exportées : " + path;
        }
        catch (Exception ex)
        {
            StatusMessage = "Export impossible : " + ex.Message;
        }
    }
}
