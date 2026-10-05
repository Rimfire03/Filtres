namespace FiltresApp.Core.Models;

/// <summary>Titre, icône et état par défaut (plié / déplié) d'un menu dépliant de la barre latérale, modifiables
/// dans « Paramètres du module Filtre ». Clés : <see cref="PeriodicMenuKey"/> et <see cref="FoulingMenuKey"/>.</summary>
public class MenuEntry
{
    public const string PeriodicMenuKey = "periodic";
    public const string FoulingMenuKey = "fouling";

    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool DefaultExpanded { get; set; }

    public static MenuEntry DefaultFor(string key) => key switch
    {
        PeriodicMenuKey => new MenuEntry { Key = key, Title = "Changement filtre périodique", Icon = "🟦", DefaultExpanded = true },
        FoulingMenuKey => new MenuEntry { Key = key, Title = "Changement sur encrassement", Icon = "🟪", DefaultExpanded = true },
        _ => new MenuEntry { Key = key, Title = key, Icon = "▫", DefaultExpanded = false }
    };
}
