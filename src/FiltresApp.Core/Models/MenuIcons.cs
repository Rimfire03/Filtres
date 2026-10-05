namespace FiltresApp.Core.Models;

/// <summary>Icônes proposées devant le titre d'une vue « Changement filtre périodique » ou d'une variété
/// « Changement sur encrassement » (liste déroulante de « Paramètres du module Filtre » et fenêtre de
/// création / modification d'une vue). Une icône déjà enregistrée qui n'est plus dans la liste reste
/// sélectionnable (voir <see cref="WithCurrent"/>).</summary>
public static class MenuIcons
{
    public static readonly string[] Choices =
    {
        "🟦", "🟩", "🟥", "🟧", "🟨", "🟪", "🟫", "⬛", "⬜", "▫", "▪",
        "🔹", "🔸", "🔷", "🔶", "🔵", "🟢", "🔴", "🟠", "🟡", "🟣",
        "📄", "📋", "📁", "🗂", "📦", "🧾", "📊", "📈", "🔧", "⚙", "🧰",
        "🌀", "💨", "🔥", "💧", "❄", "🌿", "⭐", "⚠"
    };

    /// <summary>Les choix, plus <paramref name="current"/> s'il n'en fait pas partie (pour que la liste puisse
    /// l'afficher comme sélection).</summary>
    public static List<string> WithCurrent(string? current)
    {
        var list = Choices.ToList();
        if (!string.IsNullOrWhiteSpace(current) && !list.Contains(current)) list.Insert(0, current);
        return list;
    }
}
