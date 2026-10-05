using FiltresApp.Core.Models;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Ligne de la grille "Courroies" (voir <see cref="TrackedItemRowViewModel{TEntity}"/>). Pas de
/// rattachement Commande / Inventaire pour ce module (pas de pastille "Lié").</summary>
public class BeltRowViewModel : TrackedItemRowViewModel<Belt>
{
    public BeltRowViewModel(Belt belt, int year, ITrackedItemOwner<Belt> owner) : base(belt, year, owner) { }

    public override bool ShowsLinkDot => false;

    public string? BeltType => Entity.BeltType;
    public int QuantitySoufflage => Entity.QuantitySoufflage;
    public int QuantityExtraction => Entity.QuantityExtraction;
}
