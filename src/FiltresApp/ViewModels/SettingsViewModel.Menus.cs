using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

public enum MenuEditKind { ParentMenu, PeriodicView, Variety }

/// <summary>Une ligne de la carte « Titres et icônes des menus » (Paramètres du module Filtre) : titre et icône
/// d'un menu parent, d'une vue « Changement filtre périodique » ou d'une variété « Changement sur
/// encrassement » ; l'état plié / déplié par défaut ne concerne que les menus parents.</summary>
public partial class MenuTitleEdit : ObservableObject
{
    public MenuEditKind Kind { get; init; }

    /// <summary>Clé du menu parent (<see cref="MenuEntry.Key"/>) ou identifiant de la vue / variété.</summary>
    public string? Key { get; init; }
    public int Id { get; init; }

    public string KindLabel => Kind switch
    {
        MenuEditKind.ParentMenu => "Menu",
        MenuEditKind.PeriodicView => "Vue",
        _ => "Variété"
    };

    public bool IsParentMenu => Kind == MenuEditKind.ParentMenu;

    /// <summary>Les menus dépliants n'ont pas d'icône devant leur titre (gain de place) ; vues et variétés oui.</summary>
    public bool HasIcon => !IsParentMenu;

    public string OriginalTitle { get; init; } = string.Empty;
    public string OriginalIcon { get; init; } = string.Empty;

    public bool IsChanged => Title != OriginalTitle || Icon != OriginalIcon;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _icon = string.Empty;

    /// <summary>Icônes proposées par la liste déroulante (les choix habituels, plus l'icône actuelle si elle n'en
    /// fait pas partie).</summary>
    public List<string> IconChoices => MenuIcons.WithCurrent(OriginalIcon);
    [ObservableProperty] private bool _defaultExpanded;
}

/// <summary>Case « Saisir le compteur d'heures de fonctionnement » d'une vue (Paramètres du module Filtre).</summary>
public partial class HourCounterViewOption : ObservableObject
{
    private readonly int _viewId;

    public HourCounterViewOption(int viewId, string name, bool enabled)
    {
        _viewId = viewId;
        Name = name;
        _isEnabled = enabled;
    }

    public string Name { get; }

    [ObservableProperty] private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (!App.GuardWritable()) return;
        var view = App.Db.PeriodicViews.FirstOrDefault(v => v.Id == _viewId);
        if (view is null) return;
        view.TracksOperatingHours = value;
        App.Db.SaveChanges();
    }
}

/// <summary>Carte « Titres et icônes des menus » de « Paramètres du module Filtre » : titre, icône devant le titre
/// et, pour les deux menus dépliants, état par défaut (plié / déplié) au lancement. Enregistré dans la base
/// (commun à tous les postes).</summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private ObservableCollection<MenuTitleEdit> _menuEdits = new();
    [ObservableProperty] private string _menuEditStatusMessage = string.Empty;

    private void InitializeMenus()
    {
        LoadMenuEdits();
        LoadHourCounterViews();
    }

    /// <summary>Carte « Comportement » : une case par vue « Changement filtre périodique » pour activer la saisie
    /// du compteur d'heures de fonctionnement à la réalisation. Enregistré au clic.</summary>
    [ObservableProperty] private ObservableCollection<HourCounterViewOption> _hourCounterViews = new();

    private void LoadHourCounterViews() =>
        HourCounterViews = new ObservableCollection<HourCounterViewOption>(
            App.Db.PeriodicViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList()
                .Select(v => new HourCounterViewOption(v.Id, v.Nom, v.TracksOperatingHours)));

    private void LoadMenuEdits()
    {
        var list = new List<MenuTitleEdit>();
        foreach (var key in new[] { MenuEntry.PeriodicMenuKey, MenuEntry.FoulingMenuKey })
        {
            var entry = App.Db.MenuEntries.AsNoTracking().FirstOrDefault(m => m.Key == key) ?? MenuEntry.DefaultFor(key);
            list.Add(new MenuTitleEdit { Kind = MenuEditKind.ParentMenu, Key = key, OriginalTitle = entry.Title, OriginalIcon = entry.Icon, Title = entry.Title, Icon = entry.Icon, DefaultExpanded = entry.DefaultExpanded });
        }
        foreach (var v in App.Db.PeriodicViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList())
            list.Add(new MenuTitleEdit { Kind = MenuEditKind.PeriodicView, Id = v.Id, OriginalTitle = v.Nom, OriginalIcon = v.Icon, Title = v.Nom, Icon = v.Icon });
        foreach (var v in App.Db.FilterVarieties.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList())
            list.Add(new MenuTitleEdit { Kind = MenuEditKind.Variety, Id = v.Id, OriginalTitle = v.Nom, OriginalIcon = v.Icon, Title = v.Nom, Icon = v.Icon });
        MenuEdits = new ObservableCollection<MenuTitleEdit>(list);
    }

    [RelayCommand]
    private void SaveMenuEdits()
    {
        if (!App.GuardWritable()) return;

        // Contrôles avant d'écrire quoi que ce soit : titre non vide, pas de doublon entre eux ni avec un libellé réservé.
        var titles = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var edit in MenuEdits)
        {
            edit.Title = edit.Title.Trim();
            edit.Icon = edit.Icon.Trim();
            if (edit.Title.Length == 0) { MenuEditStatusMessage = "Un titre ne peut pas être vide."; return; }
            if (!titles.Add(edit.Title)) { MenuEditStatusMessage = $"Le titre « {edit.Title} » est utilisé deux fois."; return; }
            var taken = edit.Kind switch
            {
                MenuEditKind.ParentMenu => MenuNames.IsTaken(edit.Title, excludedMenuKey: edit.Key),
                MenuEditKind.PeriodicView => MenuNames.IsTaken(edit.Title, excludedPeriodicViewId: edit.Id),
                _ => MenuNames.IsTaken(edit.Title, excludedVarietyId: edit.Id)
            };
            if (taken) { MenuEditStatusMessage = $"Le titre « {edit.Title} » est déjà utilisé ou réservé."; return; }
        }

        foreach (var edit in MenuEdits)
        {
            var icon = edit.Icon.Length == 0 ? "▫" : edit.Icon;
            switch (edit.Kind)
            {
                case MenuEditKind.ParentMenu:
                    var entry = App.Db.MenuEntries.FirstOrDefault(m => m.Key == edit.Key);
                    if (entry is null) App.Db.MenuEntries.Add(entry = new MenuEntry { Key = edit.Key! });
                    entry.Title = edit.Title;
                    entry.Icon = icon;
                    entry.DefaultExpanded = edit.DefaultExpanded;
                    break;
                case MenuEditKind.PeriodicView:
                    var view = App.Db.PeriodicViews.First(v => v.Id == edit.Id);
                    view.Nom = edit.Title;
                    view.Icon = icon;
                    break;
                default:
                    var variety = App.Db.FilterVarieties.First(v => v.Id == edit.Id);
                    if (!string.Equals(variety.Nom, edit.Title, StringComparison.Ordinal))
                        RenameVarietyInOrderLines(variety.Nom, edit.Title);
                    variety.Nom = edit.Title;
                    variety.Icon = icon;
                    break;
            }
        }
        App.Db.SaveChanges();

        PeriodicViewRegistry.Load(App.Db);
        _main.RefreshMenuEntries();
        // Seuls les sous-menus dont le titre ou l'icône a changé sont rafraîchis (un écran affiché garde ainsi ses filtres).
        foreach (var edit in MenuEdits.Where(e => e.IsChanged))
        {
            if (edit.Kind == MenuEditKind.PeriodicView) _main.UpdatePeriodicViewNavigationItem(App.Db.PeriodicViews.AsNoTracking().First(v => v.Id == edit.Id));
            else if (edit.Kind == MenuEditKind.Variety) _main.RefreshVarietyNavigationItem(App.Db.FilterVarieties.AsNoTracking().First(v => v.Id == edit.Id));
        }
        _main.ResetOtherScreens();

        LoadMenuEdits();
        MenuEditStatusMessage = "Titres et icônes enregistrés. L'état plié / déplié par défaut s'applique au prochain lancement.";
    }

    /// <summary>Le choix manuel d'une variété comme famille de Commande / Inventaire mémorise son nom : un
    /// renommage doit le suivre, sinon la ligne perdrait sa famille.</summary>
    internal static void RenameVarietyInOrderLines(string oldName, string newName) =>
        App.Db.Database.ExecuteSqlRaw(
            """UPDATE "OrderLines" SET "FamilyOverrideType" = {0} WHERE "FamilyOverride" = {1} AND lower("FamilyOverrideType") = lower({2});""",
            newName, OrderLine.DynamicTypeOverride, oldName);
}
