using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

public partial class OpacimetricFilterListViewModel : ObservableObject
{
    public string Title => "Filtres F7 à H13";

    /// <summary>Cette feuille n'a pas de périodicité mensuelle fixe (contrairement aux 4 autres
    /// catégories) : c'est un historique de remplacements ponctuels. L'année consultée ne filtre donc
    /// pas des lignes de suivi mensuel, mais restreint "Dernier changement" / "Nb remplacements" aux
    /// remplacements datés de cette année-là ; l'historique complet reste toujours en base et l'export
    /// Excel de l'année inclut cette catégorie comme les 4 autres.</summary>
    public YearContext YearContext => App.YearContext;

    [ObservableProperty] private ObservableCollection<OpacimetricFilterRowViewModel> _filters = new();
    [ObservableProperty] private OpacimetricFilterRowViewModel? _selectedFilter;

    public OpacimetricFilterListViewModel()
    {
        App.YearContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Services.YearContext.Year)) Load();
        };
        Load();
    }

    private void Load()
    {
        var all = App.Db.OpacimetricFilters
            .Include(f => f.Replacements)
            .AsNoTracking()
            .OrderBy(f => f.Location)
            .ToList();
        Filters = new ObservableCollection<OpacimetricFilterRowViewModel>(
            all.Select(f => new OpacimetricFilterRowViewModel(f, YearContext.Year, this)));
    }

    [RelayCommand]
    private void AddFilter()
    {
        if (!App.GuardWritable()) return;
        var entity = new OpacimetricFilter();
        if (!EditEntity(entity, true)) return;
        App.Db.OpacimetricFilters.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void EditFilter()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        var tracked = App.Db.OpacimetricFilters.Include(f => f.Replacements).First(f => f.Id == SelectedFilter.Id);
        if (!EditEntity(tracked, false)) return;
        App.Db.SaveChanges();
        Load();
    }

    private bool EditEntity(OpacimetricFilter entity, bool isNew)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la centrale d'air", () => entity.Location, v => entity.Location = v, required: true),
            EditField.Text("Dimension", () => entity.Dimension, v => entity.Dimension = v),
            EditField.NullableText("Type", () => entity.FilterType, v => entity.FilterType = v),
            EditField.IntField("Quantité en place", () => entity.QuantityInPlace, v => entity.QuantityInPlace = v),
            EditField.Multiline("Notes", () => entity.Notes, v => entity.Notes = v)
        };
        return App.Dialogs.EditFields(isNew ? "Ajouter un filtre F7/H13" : "Modifier le filtre", fields);
    }

    [RelayCommand]
    private void DeleteFilter()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer définitivement '{SelectedFilter.Location}' ?")) return;
        var tracked = App.Db.OpacimetricFilters.First(f => f.Id == SelectedFilter.Id);
        App.Db.OpacimetricFilters.Remove(tracked);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void MarkReplacement()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        var picked = App.Dialogs.PickSimpleReplacement(SelectedFilter.QuantityInPlace);
        if (picked is null) return;

        var tracked = App.Db.OpacimetricFilters.Include(f => f.Replacements).First(f => f.Id == SelectedFilter.Id);
        tracked.Replacements.Add(new OpacimetricReplacement
        {
            QuantityChanged = picked.Value.Quantity,
            DateChanged = picked.Value.Date
        });
        App.Db.SaveChanges();
        App.YearContext.EnsureYear(picked.Value.Date.Year);
        Load();
    }

    [RelayCommand]
    private void Print()
    {
        var headers = new[] { "Nom de la centrale d'air", "Dimension", "Type", "Qté en place", $"Dernier changement ({YearContext.Year})", $"Nb remplacements ({YearContext.Year})" };
        var rows = Filters.Select(f => new[]
        {
            f.Location, f.Dimension, f.FilterType ?? "", f.QuantityInPlace.ToString(),
            f.LastChangedDateInYear?.ToString("dd/MM/yyyy") ?? "-", f.ReplacementCountInYear.ToString()
        }).ToList();
        App.Printer.PrintTable(Title, headers, rows);
    }

    [RelayCommand]
    private void ExportExcelYear()
    {
        var path = App.ExcelExport.ExportYear(App.Db, App.Settings.ResolvedPdfExportPath, YearContext.Year);
        App.Dialogs.ShowMessage("Export Excel",
            $"Export de l'année {YearContext.Year} généré avec succès (toutes catégories, une feuille par catégorie).\n\nIl est stocké dans :\n{path}");
    }

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille) : ouvre une fenêtre de lecture
    /// seule listant, pour l'année choisie parmi celles où ce filtre précis a de l'historique, la liste
    /// chronologique des remplacements (qté changée + date). Cette feuille n'a pas de mois fixe (voir
    /// Views.Dialogs.OpacimetricHistoryWindow).</summary>
    public void ShowHistory(OpacimetricFilter filter)
    {
        var replacements = App.Db.OpacimetricReplacements
            .AsNoTracking()
            .Where(r => r.OpacimetricFilterId == filter.Id)
            .ToList();
        App.Dialogs.ShowOpacimetricHistory(filter.Location, filter.Dimension, replacements);
    }
}
