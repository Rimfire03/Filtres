using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Services;

namespace FiltresApp.ViewModels;

/// <summary>Coefficient de correction d'un mois pour le calcul de charge (onglet « Calcul de charge » de Paramètres), en %.
/// 100 % par défaut, réglable par pas de 5 avec + et -. Un mois à 80 % : une charge égale à 80 % de la normale y est
/// considérée comme une charge de 100 %.</summary>
public partial class MonthPercentOption : ObservableObject
{
    private readonly Action<MonthPercentOption> _changed;

    public MonthPercentOption(string monthName, double percent, Action<MonthPercentOption> changed)
    {
        MonthName = monthName;
        _percent = WorkloadService.ClampMonthPercent(percent);
        _changed = changed;
    }

    public string MonthName { get; }

    [ObservableProperty] private double _percent;

    public string PercentText => $"{Percent:0} %";
    public bool CanDecrease => Percent > WorkloadService.MinMonthPercent + 1e-9;
    public bool CanIncrease => Percent < WorkloadService.MaxMonthPercent - 1e-9;

    partial void OnPercentChanged(double value)
    {
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(CanDecrease));
        OnPropertyChanged(nameof(CanIncrease));
    }

    [RelayCommand]
    private void Increase() => Set(Percent + WorkloadService.MonthPercentStep);

    [RelayCommand]
    private void Decrease() => Set(Percent - WorkloadService.MonthPercentStep);

    [RelayCommand]
    private void Reset() => Set(100);

    private void Set(double value)
    {
        if (!App.GuardWritable()) return;
        var next = WorkloadService.ClampMonthPercent(value);
        if (Math.Abs(next - Percent) < 1e-9) return;
        Percent = next;
        _changed(this);
    }
}

/// <summary>Coefficients de correction des 12 mois (onglet « Calcul de charge »). Enregistrés dans la base (commun à tous les
/// postes) à chaque changement.</summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private ObservableCollection<MonthPercentOption> _monthPercents = new();

    private void LoadMonthPercents()
    {
        var saved = WorkloadService.MonthPercents(App.Db);
        MonthPercents = new ObservableCollection<MonthPercentOption>(
            StatisticsService.MonthNames.Select((name, i) => new MonthPercentOption(name, saved[i], _ => SaveMonthPercents())));
    }

    private void SaveMonthPercents()
    {
        try
        {
            WorkloadService.SaveMonthPercents(App.Db, MonthPercents.Select(m => m.Percent).ToList());
        }
        catch (Exception ex)
        {
            App.Dialogs.ShowMessage("Calcul de charge", "Enregistrement impossible : " + ex.Message);
        }
    }

    [RelayCommand]
    private void ResetMonthPercents()
    {
        if (!App.GuardWritable()) return;
        foreach (var m in MonthPercents) m.Percent = 100;
        SaveMonthPercents();
    }
}
