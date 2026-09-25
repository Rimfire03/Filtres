using FiltresApp.Core.Models;

namespace FiltresApp.Core.Services;

/// <summary>
/// Reproduit le calcul du besoin semestriel de commande des feuilles Excel d'origine (G4 plissés,
/// G4 plan, G3, Charbon), colonnes "BESOIN POUR septembre" / "BESOIN POUR mars" :
/// formule <c>=(COUNTA(fenêtre des colonnes "changement prévu en [mois]")+1)*Qté en place</c>.
///
/// Fenêtres vérifiées sur le classeur source (feuille "Filtres G4 plissés", ligne 5) :
/// - BESOIN POUR septembre (inventaire du 15/07 au 31/07) : COUNTA(AD,AG,AJ,AM,AP,I,L,O) où les colonnes
///   correspondent, par pas de 3 à partir de la colonne I, aux mois août à mars de l'année suivante
///   (colonnes "changement prévu en [mois]" août, sept, oct, nov, déc, jan, fév, mars).
/// - BESOIN POUR mars (inventaire du 15/01 au 31/01) : COUNTA(L,O,R,U,X,AA,AD,AG), soit les mois
///   février à septembre de la même année.
/// Les deux fenêtres se chevauchent volontairement (marge de sécurité de l'Excel d'origine) ; le nombre
/// compté est le nombre d'échéances de la périodicité du filtre tombant dans la fenêtre, +1 (le "+1"
/// correspond au changement courant/en cours, en plus des échéances à venir), multiplié par la quantité
/// en place.
/// </summary>
public static class OrderNeedCalculationService
{
    /// <summary>Août, Septembre, Octobre, Novembre, Décembre, Janvier, Février, Mars.</summary>
    private static readonly int[] WindowSeptembre = { 8, 9, 10, 11, 12, 1, 2, 3 };

    /// <summary>Février, Mars, Avril, Mai, Juin, Juillet, Août, Septembre.</summary>
    private static readonly int[] WindowMars = { 2, 3, 4, 5, 6, 7, 8, 9 };

    public static int NeedForFilter(PeriodicFilter filter, int[] window)
    {
        var count = filter.GetPeriodicityMonths().Count(window.Contains);
        var need = (count + 1) * filter.QuantityInPlace;
        // Option "Changé tous les 15 jours" (G4 plissé) : le filtre est changé deux fois plus souvent
        // que sa périodicité habituelle ne le suggère, donc le besoin est doublé.
        return filter.ChangedEvery15Days ? need * 2 : need;
    }

    public static int NeedForFilterSeptembre(PeriodicFilter filter) => NeedForFilter(filter, WindowSeptembre);
    public static int NeedForFilterMars(PeriodicFilter filter) => NeedForFilter(filter, WindowMars);

    public static int ComputeNeedSeptembre(IEnumerable<PeriodicFilter> filters) =>
        filters.Sum(f => NeedForFilterSeptembre(f));

    public static int ComputeNeedMars(IEnumerable<PeriodicFilter> filters) =>
        filters.Sum(f => NeedForFilterMars(f));
}
