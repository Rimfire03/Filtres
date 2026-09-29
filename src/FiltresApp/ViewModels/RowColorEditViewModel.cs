using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.ViewModels;

/// <summary>Une couleur du réglage "Couleurs de ligne" (Paramètres) : nom personnalisé + couleur, avec
/// écriture immédiate en base (partagée, comme les familles K7) et invalidation de
/// <see cref="RowColorPalette"/> pour que les grilles déjà ouvertes se recolorent tout de suite.</summary>
public partial class RowColorEditViewModel : ObservableObject
{
    private readonly RowColor _entity;

    [ObservableProperty] private string _nom;
    [ObservableProperty] private string _hex;

    public Brush Preview => (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hex)!);

    public RowColorEditViewModel(RowColor entity)
    {
        _entity = entity;
        _nom = entity.Nom;
        _hex = entity.Hex;
    }

    public int Id => _entity.Id;

    partial void OnNomChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        _entity.Nom = value;
        App.Db.SaveChanges();
        RowColorPalette.Invalidate();
    }

    partial void OnHexChanged(string value)
    {
        try
        {
            ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return;
        }
        _entity.Hex = value;
        App.Db.SaveChanges();
        OnPropertyChanged(nameof(Preview));
        RowColorPalette.Invalidate();
    }
}
