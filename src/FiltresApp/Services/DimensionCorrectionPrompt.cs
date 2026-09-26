using FiltresApp.Core.Services;

namespace FiltresApp.Services;

/// <summary>Harmonisation du champ Dimension à la saisie (écrans de filtres).</summary>
public static class DimensionCorrectionPrompt
{
    /// <summary>Propose une correction du champ Dimension selon la norme harmonisée ("aaaa x bbbb x
    /// cccc", voir <see cref="DimensionFormatService"/>) juste après validation du formulaire, si le
    /// texte saisi ne correspond pas déjà exactement à cette norme. N'affiche rien si aucun motif de
    /// dimension n'a pu être reconnu (le texte reste inchangé, ex. "A laver").</summary>
    public static void Propose(Func<string> get, Action<string> set)
    {
        var raw = get();
        var normalized = DimensionFormatService.Normalize(raw);
        if (normalized is null || normalized == raw) return;

        if (App.Dialogs.ShowConfirm("Format de dimension",
                "Le format standard des dimensions est « aaaa x bbbb x cccc » (le plus grand des deux " +
                "premiers nombres en premier, l'épaisseur toujours en dernier).\n\n" +
                $"Remplacer :\n« {raw} »\n\npar :\n« {normalized} » ?"))
        {
            set(normalized);
        }
    }
}
