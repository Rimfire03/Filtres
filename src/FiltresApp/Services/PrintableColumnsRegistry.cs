namespace FiltresApp.Services;

/// <summary>Écran imprimable et ses colonnes, pour le sélecteur "Colonnes imprimées" de Paramètres. Les
/// noms de colonnes sont des clés stables, indépendantes du texte affiché quand celui-ci varie (ex.
/// en-têtes "Dernier changement (année)" des variétés F7 à H14, qui changent chaque année) - voir
/// <see cref="PrintService.FilterByPrintKeys"/>.</summary>
public sealed record PrintableScreen(string Key, string DisplayName, string[] Columns);

public static class PrintableColumnsRegistry
{
    /// <summary>Clé générique unique, partagée par TOUTES les listes de filtres (G4 plissés, G4 plan, G3,
    /// Charbon, et chaque variété "Filtres F7 à H14") : un seul choix de colonnes s'applique à toutes,
    /// plutôt qu'un réglage séparé par écran. Une colonne absente d'une liste donnée (ex. "Périodicité"
    /// pour une variété F7 à H14) n'y a simplement aucun effet.</summary>
    public const string FilterListsKey = "Filtres (toutes les listes)";

    private static readonly string[] FilterListsColumns =
        { "Lié", "Nom", "Dimension", "Type", "Qté en place", "Périodicité", "Prochaine échéance", "Dernier changement", "Réalisé", "Date du changement", "Nb remplacements", "Commentaire" };

    public static readonly IReadOnlyList<PrintableScreen> All = new List<PrintableScreen>
    {
        new(FilterListsKey, "Listes de filtres (G4, G3, Charbon, F7 à H14 - toutes)", FilterListsColumns),
        new("Liste K7", "Liste K7", new[] { "Famille", "Lieu", "N° porte", "Changement réalisé", "Nb de filtres" }),
        new("Commande", "Commande", new[] { "Dimension", "Destination", "Type", "Référence fournisseur", "Besoin", "Quantité à commander" }),
        new("Inventaire", "Inventaire", new[] { "Dimension", "Destination", "Type", "Référence fournisseur", "Besoin mars (calculé)", "Besoin septembre (calculé)", "Inventaire", "Quantité" })
    };
}
