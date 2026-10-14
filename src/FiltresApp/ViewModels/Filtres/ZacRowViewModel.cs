using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Ligne de la grille « Suivi ZAC » (voir <see cref="TrackedItemRowViewModel{TEntity}"/>) : dernier et prochain
/// contrôle, état (orange à l'approche de l'échéance, rouge si elle est dépassée) ; une date saisie ouvre la fenêtre de
/// contrôle avec ses fichiers joints (voir <see cref="ZacListViewModel.AddReplacement"/>).</summary>
public partial class ZacRowViewModel : TrackedItemRowViewModel<ZacItem>
{
    private static readonly Brush OrangeBrush = Frozen(0xD9, 0x77, 0x06);
    private static readonly Brush RedBrush = Frozen(0xDC, 0x26, 0x26);

    private readonly ZacListViewModel _list;

    public ZacRowViewModel(ZacItem item, int year, ZacListViewModel list) : base(item, year, list) => _list = list;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public override bool ShowsLinkDot => false;

    public DateOnly? LastControlDate => Entity.LastControlDate;
    public DateOnly? NextControlDate => Entity.NextControlDate;

    public ZacAlertLevel Level => ZacService.Level(Entity.NextControlDate, DateOnly.FromDateTime(DateTime.Today), _list.AlertDays);

    public string StatusText => Level switch
    {
        ZacAlertLevel.Overdue => "Dépassé",
        ZacAlertLevel.Approaching => "À contrôler",
        _ => ""
    };

    public Brush? StatusBrush => Level switch
    {
        ZacAlertLevel.Overdue => RedBrush,
        ZacAlertLevel.Approaching => OrangeBrush,
        _ => null
    };

    /// <summary>Recalcule les colonnes propres à ce module après un contrôle ou un changement du délai d'alerte.</summary>
    public void RefreshZac()
    {
        OnPropertyChanged(nameof(LastControlDate));
        OnPropertyChanged(nameof(NextControlDate));
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
    }

    /// <summary>« Pièces jointes... » (menu contextuel de la grille).</summary>
    [RelayCommand]
    private void ShowAttachments() => _list.ShowAttachments(Entity);
}
