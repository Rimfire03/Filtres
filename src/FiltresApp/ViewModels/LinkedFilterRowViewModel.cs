using CommunityToolkit.Mvvm.ComponentModel;

namespace FiltresApp.ViewModels;

/// <summary>Base commune des lignes de filtres (écrans à périodicité et F7 à H13) : puce verte / rouge de la
/// colonne "Lié" (modèle <c>LinkDotCellTemplate</c> de Styles/Controls.xaml).</summary>
public abstract class LinkedFilterRowViewModel : ObservableObject
{
    protected LinkedFilterRowViewModel(string? linkedOrderLine) => LinkedOrderLine = linkedOrderLine;

    /// <summary>Dimension de la ligne de Commande / Inventaire à laquelle ce filtre est rattaché, ou null.</summary>
    public string? LinkedOrderLine { get; }

    /// <summary>Puce verte (rattaché) ou rouge (non rattaché) en tête de ligne.</summary>
    public bool IsLinkedToOrder => LinkedOrderLine is not null;

    /// <summary>False pour masquer la puce (filtres qui ne peuvent jamais être rattachés).</summary>
    public virtual bool ShowsLinkDot => true;

    public string LinkToolTip => IsLinkedToOrder
        ? $"Rattaché à la ligne « {LinkedOrderLine} » de Commande / Inventaire"
        : "Rattaché à aucune ligne de Commande / Inventaire";
}
