using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

/// <summary>Coefficient de correction de la charge d'une vue de changement de filtre périodique (onglet « Calcul de charge »
/// de Paramètres). 1 par défaut, réglable par pas de 0,1 avec les boutons + et -. Enregistré dans la base (commun à
/// tous les postes) dès qu'il change.</summary>
public partial class LoadCoefficientOption : ObservableObject
{
    private readonly int _viewId;

    public LoadCoefficientOption(int viewId, string name, double coefficient)
    {
        _viewId = viewId;
        Name = name;
        _coefficient = WorkloadService.Clamp(coefficient);
    }

    public string Name { get; }

    [ObservableProperty] private double _coefficient;

    public string CoefficientText => Coefficient.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);

    public bool CanDecrease => Coefficient > WorkloadService.MinCoefficient + 1e-9;
    public bool CanIncrease => Coefficient < WorkloadService.MaxCoefficient - 1e-9;

    partial void OnCoefficientChanged(double value)
    {
        OnPropertyChanged(nameof(CoefficientText));
        OnPropertyChanged(nameof(CanDecrease));
        OnPropertyChanged(nameof(CanIncrease));
    }

    [RelayCommand]
    private void Increase() => Change(+WorkloadService.Step);

    [RelayCommand]
    private void Decrease() => Change(-WorkloadService.Step);

    [RelayCommand]
    private void Reset() => Change(1.0 - Coefficient);

    private void Change(double delta)
    {
        if (!App.GuardWritable()) return;
        var next = WorkloadService.Clamp(Coefficient + delta);
        if (Math.Abs(next - Coefficient) < 1e-9) return;

        var view = App.Db.PeriodicViews.FirstOrDefault(v => v.Id == _viewId);
        if (view is null) return;
        view.LoadCoefficient = next;
        App.Db.SaveChanges();
        Coefficient = next;
    }
}

/// <summary>Onglet « Calcul de charge » de Paramètres : un coefficient de correction par vue de changement de filtre
/// périodique, pris en compte dans la charge des mois (couleurs des mois à l'édition d'un filtre, statistiques).</summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private ObservableCollection<LoadCoefficientOption> _loadCoefficients = new();

    private void LoadLoadCoefficients() =>
        LoadCoefficients = new ObservableCollection<LoadCoefficientOption>(
            App.Db.PeriodicViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList()
                .Select(v => new LoadCoefficientOption(v.Id, v.Nom, v.LoadCoefficient)));
}
