using FiltresApp.Core.Models;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Ligne de la grille "Roulements" (voir <see cref="TrackedItemRowViewModel{TEntity}"/>) : une date
/// saisie ouvre la sélection des roulements changés (voir BearingListViewModel.AddReplacement) avant
/// d'enregistrer un nouveau remplacement. Pas de rattachement Commande / Inventaire pour ce module (pas de
/// pastille "Lié").</summary>
public class BearingRowViewModel : TrackedItemRowViewModel<BearingUnit>
{
    public BearingRowViewModel(BearingUnit bearing, int year, ITrackedItemOwner<BearingUnit> owner) : base(bearing, year, owner) { }

    public override bool ShowsLinkDot => false;

    public string CentraleType => Entity.CentraleType;

    /// <summary>"Entraînement direct" : pas de roulement volute (voir VoluteCellStyle, case noire dans la
    /// grille, et BearingListViewModel qui force la référence à null et retire "Volute" de la sélection à
    /// la saisie d'un changement).</summary>
    public bool IsDirectDrive => Entity.CentraleType == BearingUnit.CentraleTypeOptions[1];

    public string? RefAvant => Entity.RefAvant;
    public string? RefArriere => Entity.RefArriere;
    public string? RefVolute => Entity.RefVolute;
}
