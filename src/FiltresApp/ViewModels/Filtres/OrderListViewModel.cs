using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Services;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Écran "Commande". Depuis la fusion Inventaire / Commande chmy (voir README), les lignes de
/// type <see cref="OrderDocumentType.CommandeChmy"/> sont exactement les mêmes que celles affichées par
/// <see cref="InventoryListViewModel"/> ("Inventaire") : les deux écrans partagent la même table
/// <see cref="OrderLine"/>, seule la mise en page de la grille diffère. Rattachement des filtres :
/// voir OrderListViewModel.Links.cs.</summary>
public partial class OrderListViewModel : OrderLineListViewModelBase
{
    public override string Title { get; }
    public bool AllowPdfExport => DocumentType == OrderDocumentType.CommandeChmy;

    public OrderListViewModel(OrderDocumentType type, string title) : base(type)
    {
        Title = title;
        Load();
    }

    protected override void Load()
    {
        FamilyQuickChoices = OrderFamilyFilter.QuickChoices();
        base.Load();
    }

    /// <summary>Choix de la colonne "Famille" (Automatique, familles), relus à chaque chargement.</summary>
    [ObservableProperty] private List<string> _familyQuickChoices = new();

    /// <summary>Colonne "Famille" (masquée par défaut) : change la famille de la ligne sans ouvrir
    /// "Modifier". "Automatique" revient à la famille déduite des filtres rattachés.
    /// <para>Ne recharge jamais toute la liste (<see cref="Load"/>) : un rechargement remplace
    /// entièrement <see cref="Lines"/>, ce qui fait sauter la grille tout en haut quelle que soit la
    /// sélection ensuite restaurée. La ligne modifiée est plutôt retirée puis réinsérée au même endroit
    /// dans la collection existante (même identité d'objet, même ObservableCollection) : cela force le
    /// regroupement à se recalculer pour cette seule ligne, avec seulement les notifications de
    /// changement de collection nécessaires, sans jamais remplacer la source de la grille.</para></summary>
    public void SetFamilyChoice(OrderLine line, string choice)
    {
        if (choice == line.FamilyChoiceLabel || !App.GuardWritable()) return;
        var tracked = App.Db.OrderLines.First(l => l.Id == line.Id);
        OrderFamilyFilter.ApplyQuickChoice(tracked, choice);
        App.Db.SaveChanges();
        line.FamilyOverride = tracked.FamilyOverride;
        line.FamilyOverrideType = tracked.FamilyOverrideType;

        var index = Lines.IndexOf(line);
        if (index < 0) return;

        // La ligne suivante (avant déplacement) reste le point de repère visuel demandé : la vue s'y
        // fixe plutôt que de suivre la ligne qui vient de changer de famille/groupe.
        var nextLine = NeighborOf(index);

        if (!FamilyFilter.Matches(line))
        {
            // Ne correspond plus au filtre de famille actuellement affiché : simplement retirée.
            Lines.RemoveAt(index);
        }
        else
        {
            // Retrait + réinsertion au même index (au lieu d'une simple notification de "changement de
            // propriété", qu'OrderLine ne peut pas émettre - ce n'est pas un ObservableObject) : la vue
            // groupée range alors la ligne dans son nouveau groupe sans recharger le reste.
            Lines.RemoveAt(index);
            Lines.Insert(index, line);
        }

        SelectedLine = nextLine ?? line;
    }

    /// <summary>Saisie directe dans la cellule "Besoin" (lignes hors familles G4 plissé, G4 plan, G3).
    /// Retourne false (saisie à annuler) en lecture seule ou si le texte n'est pas un nombre entier.</summary>
    public bool SetManualNeed(OrderLine line, string text) =>
        line.UsesManualNeed && SaveIntCell(line, text, "Besoin", l => l.ManualNeed, (l, v) => l.ManualNeed = v);

    /// <summary>Colonnes imprimées / exportées en PDF : la colonne "Filtres liés" de la grille n'y figure
    /// jamais.</summary>
    protected override string[] PrintHeaders { get; } =
        { "Dimension", "Destination", "Type", "Référence fournisseur", "Besoin", "Quantité à commander" };

    protected override string[] PrintRow(OrderLine l) => new[]
    {
        l.Designation,
        l.Destination ?? "",
        l.Dimension ?? "",
        l.Notes ?? "",
        l.Need?.ToString() ?? "",
        l.InventoryQuantity?.ToString() ?? ""
    };
}
