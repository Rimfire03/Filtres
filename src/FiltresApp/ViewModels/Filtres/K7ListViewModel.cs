using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using FiltresApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Écran "Liste K7". Les lieux sont regroupés visuellement par famille (voir README, section
/// "Familles K7 et migration") : <see cref="Locations"/> est triée par <see cref="K7Location.FamilyGroupLabel"/>
/// pour permettre le regroupement dans la grille (<c>GroupStyle</c> XAML sur une
/// <c>CollectionViewSource</c>). Les familles elles-mêmes (nom + périodicité) sont gérées via un panneau
/// dédié (ajout/modification/suppression) sur le même écran.</summary>
public partial class K7ListViewModel : ObservableObject, IReloadable
{
    public string Title => "Liste K7";

    [ObservableProperty] private ObservableCollection<K7Location> _locations = new();
    [ObservableProperty] private K7Location? _selectedLocation;
    [ObservableProperty] private ObservableCollection<K7Family> _families = new();
    [ObservableProperty] private K7Family? _selectedFamily;

    public K7ListViewModel()
    {
        Load();
    }

    public void Reload() => Load();

    private void Load()
    {
        Families = new ObservableCollection<K7Family>(App.Db.K7Families.AsNoTracking().OrderBy(f => f.Nom).ToList());

        // Les lignes marquées IsFamilyHeader sont les anciennes lignes "titre de famille" du classeur
        // Excel d'origine (ex. "AP RDC periodicités 1/4/7/10") : elles ne sont pas supprimées (voir
        // K7FamilyReconstructionService) mais masquées ici pour éviter un doublon visuel avec l'en-tête
        // de groupe généré à partir de K7Family.
        Locations = new ObservableCollection<K7Location>(
            App.Db.K7Locations
                .Include(l => l.Family)
                .AsNoTracking()
                .Where(l => !l.IsFamilyHeader)
                .AsEnumerable()
                .OrderBy(l => l.FamilyGroupLabel)
                .ThenBy(l => l.Lieu)
                .ToList());
    }

    // ---- Lieux ----

    [RelayCommand]
    private void Add()
    {
        if (!App.GuardWritable()) return;
        var entity = new K7Location();
        if (!EditEntity(entity, true)) return;
        App.Db.K7Locations.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void Edit()
    {
        if (!App.GuardWritable()) return;
        if (SelectedLocation is null) return;
        var tracked = App.Db.K7Locations.First(l => l.Id == SelectedLocation.Id);
        if (!EditEntity(tracked, false)) return;
        App.Db.SaveChanges();
        Load();
    }

    /// <summary>Copie le lieu sélectionné et ouvre directement son édition. N'enregistre rien si
    /// l'édition est annulée.</summary>
    [RelayCommand]
    private void Duplicate()
    {
        if (!App.GuardWritable()) return;
        if (SelectedLocation is null) return;
        var source = SelectedLocation;
        var copy = new K7Location
        {
            Lieu = source.Lieu,
            NumeroPorte = source.NumeroPorte,
            ChangementRealise = source.ChangementRealise,
            NbFiltres = source.NbFiltres,
            K7FamilyId = source.K7FamilyId,
            RowColorId = source.RowColorId
        };
        if (!EditEntity(copy, true)) return;
        App.Db.K7Locations.Add(copy);
        App.Db.SaveChanges();
        Load();
    }

    private bool EditEntity(K7Location entity, bool isNew)
    {
        var familyList = Families.ToList();
        var familyNames = familyList.Select(f => string.IsNullOrEmpty(f.PeriodicityDisplay) ? f.Nom : $"{f.Nom} (périodicités : {f.PeriodicityDisplay})").ToList();
        var currentIndex = entity.K7FamilyId.HasValue ? familyList.FindIndex(f => f.Id == entity.K7FamilyId.Value) : -1;

        var fields = new List<EditField>
        {
            EditField.Text("Lieu", () => entity.Lieu, v => entity.Lieu = v, required: true),
            EditField.NullableText("N° porte", () => entity.NumeroPorte, v => entity.NumeroPorte = v),
            EditField.DateField("Changement réalisé", () => entity.ChangementRealise, v => entity.ChangementRealise = v),
            EditField.IntField("Nb de filtres", () => entity.NbFiltres, v => entity.NbFiltres = v),
            EditField.ComboField("Famille", familyNames, () => currentIndex,
                v => entity.K7FamilyId = v >= 0 && v < familyList.Count ? familyList[v].Id : null)
        };
        return App.Dialogs.EditFields(isNew ? "Ajouter un lieu K7" : "Modifier le lieu K7", fields);
    }

    [RelayCommand]
    private void Delete()
    {
        if (!App.GuardWritable()) return;
        if (SelectedLocation is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer '{SelectedLocation.Lieu}' ?")) return;
        var tracked = App.Db.K7Locations.First(l => l.Id == SelectedLocation.Id);
        App.Db.K7Locations.Remove(tracked);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void Print()
    {
        var headers = new[] { "Famille", "Lieu", "N° porte", "Changement réalisé", "Nb de filtres" };
        var rows = Locations.Select(l => new[]
        {
            l.FamilyGroupLabel, l.Lieu, l.NumeroPorte ?? "", l.ChangementRealise?.ToString("dd/MM/yyyy") ?? "-", l.NbFiltres.ToString()
        }).ToList();
        var rowColors = Locations.Select(l => RowColorPalette.ColorFor(l.RowColorId)).ToList();
        App.Printer.PrintTable(Title, headers, rows, printColumnsKey: Title, rowColors: rowColors);
    }

    /// <summary>Couleur de ligne (clic droit, K7View.xaml.cs) : sauvegarde immédiate, puis retrait/réinsertion
    /// au même index (même technique que OrderListViewModel.SetFamilyChoice) pour rafraîchir la ligne sans
    /// recharger toute la grille - K7Location n'est pas un ObservableObject.</summary>
    public void SetRowColor(K7Location location, int? colorId)
    {
        if (!App.GuardWritable()) return;
        var tracked = App.Db.K7Locations.First(l => l.Id == location.Id);
        tracked.RowColorId = colorId;
        App.Db.SaveChanges();
        location.RowColorId = colorId;

        var index = Locations.IndexOf(location);
        if (index < 0) return;
        Locations.RemoveAt(index);
        Locations.Insert(index, location);
        SelectedLocation = location;
    }

    // ---- Familles ----

    [RelayCommand]
    private void AddFamily()
    {
        if (!App.GuardWritable()) return;
        var entity = new K7Family();
        if (!EditFamilyFields(entity, true)) return;
        App.Db.K7Families.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void EditFamily()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFamily is null) return;
        var tracked = App.Db.K7Families.First(f => f.Id == SelectedFamily.Id);
        if (!EditFamilyFields(tracked, false)) return;
        App.Db.SaveChanges();
        Load();
    }

    private bool EditFamilyFields(K7Family entity, bool isNew)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la famille", () => entity.Nom, v => entity.Nom = v, required: true),
            EditField.MonthsField("Périodicité (mois de remplacement)", entity.GetPeriodicityMonths, v => entity.Periodicite = K7Family.FormatPeriodicityMonths(v))
        };
        return App.Dialogs.EditFields(isNew ? "Ajouter une famille" : "Modifier la famille", fields);
    }

    /// <summary>Empêche la suppression d'une famille encore rattachée à des lieux plutôt que de les
    /// détacher silencieusement (choix documenté dans le README) : l'utilisateur doit d'abord réaffecter
    /// explicitement ces lieux à une autre famille (ou en créer une nouvelle) via "Modifier".</summary>
    [RelayCommand]
    private void DeleteFamily()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFamily is null) return;

        var locationsInFamily = App.Db.K7Locations.Count(l => l.K7FamilyId == SelectedFamily.Id);
        if (locationsInFamily > 0)
        {
            App.Dialogs.ShowMessage("Suppression impossible",
                $"La famille '{SelectedFamily.Nom}' est encore rattachée à {locationsInFamily} lieu(x). " +
                "Réaffectez-les d'abord à une autre famille (bouton \"Modifier\" sur chaque lieu) avant de pouvoir supprimer cette famille.");
            return;
        }

        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer la famille '{SelectedFamily.Nom}' ?")) return;
        var tracked = App.Db.K7Families.First(f => f.Id == SelectedFamily.Id);
        App.Db.K7Families.Remove(tracked);
        App.Db.SaveChanges();
        Load();
    }
}
