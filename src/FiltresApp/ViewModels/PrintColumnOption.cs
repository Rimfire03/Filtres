using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Services;

namespace FiltresApp.ViewModels;

/// <summary>Une colonne du sélecteur "Colonnes imprimées" (Paramètres) pour l'écran choisi : coché =
/// incluse à l'impression. Écrit immédiatement dans <see cref="ColumnPreferences"/> (comme les autres
/// préférences d'affichage, propre à ce poste).</summary>
public partial class PrintColumnOption : ObservableObject
{
    private readonly string _screenKey;
    public string Column { get; }

    [ObservableProperty] private bool _isIncluded;

    public PrintColumnOption(string screenKey, string column)
    {
        _screenKey = screenKey;
        Column = column;
        _isIncluded = !ColumnPreferences.IsPrintHidden(screenKey, column);
    }

    partial void OnIsIncludedChanged(bool value) => ColumnPreferences.SetPrintHidden(_screenKey, Column, !value);
}
