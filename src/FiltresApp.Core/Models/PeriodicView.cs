namespace FiltresApp.Core.Models;

/// <summary>Une vue du menu dépliant « Changement filtre périodique » (barre latérale) : G4 plissés, G4 plan, G3
/// et Charbon sont les quatre vues d'origine (<see cref="Category"/> renseignée), l'utilisateur peut en créer
/// d'autres (<see cref="Category"/> nulle) qui fonctionnent comme une vue G4 (suivi mensuel, familles de
/// Commande avec besoin calculé). Chaque vue a son titre, son icône et son ordre modifiables. Même principe que
/// <see cref="FilterVariety"/> pour le menu « Changement sur encrassement ».</summary>
public class PeriodicView
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;

    /// <summary>Icône affichée devant le titre dans le menu (un caractère / emoji).</summary>
    public string Icon { get; set; } = DefaultIcon;

    public const string DefaultIcon = "🟦";

    /// <summary>Ordre d'affichage dans le menu dépliant.</summary>
    public int Ordre { get; set; }

    /// <summary>Libellé de la première colonne de la grille (« Nom de la centrale d'air », « Emplacement de
    /// l'appareil »...), repris dans la fenêtre Ajouter / Modifier.</summary>
    public string LocationLabel { get; set; } = DefaultLocationLabel;

    public const string DefaultLocationLabel = "Emplacement de l'appareil";

    /// <summary>Vue d'origine (G4 plissés, G4 plan, G3, Charbon) ou null pour une vue créée par l'utilisateur.
    /// Une vue d'origine ne peut pas être supprimée et garde son comportement propre (option « 15 jours »,
    /// regroupement G3, compteur d'heures).</summary>
    public FilterCategory? Category { get; set; }

    public bool IsBuiltIn => Category is not null;

    /// <summary>Option (Paramètres du module Filtre) : cocher « Réalisé » demande le compteur d'heures de
    /// fonctionnement, repris dans l'export Excel de l'année.</summary>
    public bool TracksOperatingHours { get; set; }

    /// <summary>Besoin semestriel de Commande calculé automatiquement pour les filtres de cette vue : toutes les
    /// vues sauf Charbon (voir <see cref="OrderLine.UsesComputedNeed"/>).</summary>
    public bool ComputesNeed => Category != FilterCategory.Charbon;
}
