using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.ViewModels;

/// <summary>Section "Couleurs de ligne" de Paramètres : palette de couleurs nommées, partagée en base
/// (comme les familles K7), proposée dans le menu contextuel (clic droit) de chaque grille de filtres,
/// K7, Commande et Inventaire (voir <see cref="RowColorMenu"/>, <see cref="RowColorPalette"/>).</summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private ObservableCollection<RowColorEditViewModel> _rowColors = new();

    private void InitializeRowColors() =>
        RowColors = new ObservableCollection<RowColorEditViewModel>(
            App.Db.RowColors.OrderBy(c => c.Ordre).ThenBy(c => c.Nom).ToList().Select(c => new RowColorEditViewModel(c)));

    [RelayCommand]
    private void AddRowColor()
    {
        if (!App.GuardWritable()) return;
        var entity = new RowColor { Nom = "Nouvelle couleur", Hex = "#FDE68A", Ordre = RowColors.Count };
        App.Db.RowColors.Add(entity);
        App.Db.SaveChanges();
        RowColors.Add(new RowColorEditViewModel(entity));
        RowColorPalette.Invalidate();
    }

    [RelayCommand]
    private void DeleteRowColor(RowColorEditViewModel? color)
    {
        if (color is null || !App.GuardWritable()) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer la couleur « {color.Nom} » ? Les lignes qui l'utilisent perdront leur couleur.")) return;
        var tracked = App.Db.RowColors.First(c => c.Id == color.Id);
        App.Db.RowColors.Remove(tracked);
        App.Db.SaveChanges();
        RowColors.Remove(color);
        RowColorPalette.Invalidate();
    }
}
