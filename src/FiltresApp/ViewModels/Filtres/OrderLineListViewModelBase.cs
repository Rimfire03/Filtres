using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using FiltresApp.ViewModels;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Base commune des écrans "Inventaire" et "Commande", qui affichent les mêmes lignes
/// <see cref="OrderLine"/> avec des colonnes différentes : chargement groupé par famille, ajout,
/// modification, suppression, saisie directe dans une cellule, impression et export PDF.</summary>
public abstract partial class OrderLineListViewModelBase : ObservableObject, IReloadable
{
    protected OrderDocumentType DocumentType { get; }

    public abstract string Title { get; }

    [ObservableProperty] private ObservableCollection<OrderLine> _lines = new();
    [ObservableProperty] private OrderLine? _selectedLine;

    public OrderFamilyFilter FamilyFilter { get; }

    /// <summary>Le constructeur dérivé appelle <see cref="Load"/> une fois ses propres champs prêts.</summary>
    protected OrderLineListViewModelBase(OrderDocumentType documentType)
    {
        DocumentType = documentType;
        FamilyFilter = new OrderFamilyFilter(Load);
    }

    public void Reload() => Load();

    protected virtual void Load() =>
        Lines = new ObservableCollection<OrderLine>(FamilyFilter.Apply(OrderLineQueries.LoadWithLinks(App.Db, DocumentType)));

    // ---- Ajout / modification / suppression ----

    [RelayCommand]
    private void Add()
    {
        if (!App.GuardWritable()) return;
        var entity = new OrderLine { DocumentType = DocumentType, Ordre = OrderLineQueries.NextOrdre(App.Db, DocumentType) };
        FamilyFilter.ApplyDefaultFamily(entity);
        if (!EditEntity(entity, true, OrderLine.NoFamilyLabel)) return;
        App.Db.OrderLines.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void Edit()
    {
        if (!App.GuardWritable()) return;
        if (SelectedLine is null) return;
        var tracked = App.Db.OrderLines.First(l => l.Id == SelectedLine.Id);
        if (!EditEntity(tracked, false, SelectedLine.AutomaticFamilyLabel)) return;
        App.Db.SaveChanges();
        Load();
    }

    private bool EditEntity(OrderLine entity, bool isNew, string automaticFamilyLabel)
    {
        var fields = new List<EditField>
        {
            OrderFamilyFilter.CreateEditField(entity, automaticFamilyLabel),
            EditField.Text("Dimension", () => entity.Designation, v => entity.Designation = v, required: true),
            EditField.NullableText("Destination", () => entity.Destination, v => entity.Destination = v),
            EditField.NullableText("Type", () => entity.Dimension, v => entity.Dimension = v),
            EditField.Multiline("Référence fournisseur", () => entity.Notes, v => entity.Notes = v)
        };
        fields.AddRange(ExtraEditFields(entity));
        return App.Dialogs.EditFields(isNew ? "Ajouter une ligne" : "Modifier la ligne", fields);
    }

    /// <summary>Champs ajoutés à la fin de la fenêtre Ajouter / Modifier propres à l'écran.</summary>
    protected virtual IEnumerable<EditField> ExtraEditFields(OrderLine entity) => Enumerable.Empty<EditField>();

    /// <summary>Ne recharge pas toute la liste (un rechargement remplace <see cref="Lines"/> et fait sauter
    /// la grille tout en haut) : la ligne est simplement retirée, et la sélection se fixe sur la ligne
    /// suivante (précédente si c'était la dernière).</summary>
    [RelayCommand]
    private void Delete()
    {
        if (!App.GuardWritable()) return;
        if (SelectedLine is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer '{SelectedLine.Designation}' ?")) return;
        var tracked = App.Db.OrderLines.First(l => l.Id == SelectedLine.Id);
        App.Db.OrderLines.Remove(tracked);
        App.Db.SaveChanges();

        var index = Lines.IndexOf(SelectedLine);
        var next = NeighborOf(index);
        if (index >= 0) Lines.RemoveAt(index);
        SelectedLine = next;
    }

    /// <summary>Ligne suivante de <paramref name="index"/> (précédente si c'est la dernière), point de
    /// repère de la sélection quand la ligne quitte sa place.</summary>
    protected OrderLine? NeighborOf(int index) =>
        index >= 0 && index + 1 < Lines.Count ? Lines[index + 1] : index > 0 ? Lines[index - 1] : null;

    /// <summary>Saisie directe d'un entier dans une cellule de la grille : enregistre la valeur sur la ligne en
    /// base et sur la ligne affichée, sans recharger la grille. Retourne false (saisie à annuler) si le texte
    /// n'est pas un entier ou si le poste est en lecture seule.</summary>
    protected static bool SaveIntCell(OrderLine line, string text, string label, Func<OrderLine, int?> get, Action<OrderLine, int?> set)
    {
        if (!IntInput.TryParse(text, label, out var value)) return false;
        if (value == get(line)) return true;
        if (!App.GuardWritable()) return false;

        var tracked = App.Db.OrderLines.First(l => l.Id == line.Id);
        set(tracked, value);
        App.Db.SaveChanges();
        set(line, value);
        return true;
    }

    /// <summary>Couleur de ligne (clic droit, OrderView.xaml.cs / InventoryView.xaml.cs) : sauvegarde
    /// immédiate, puis retrait/réinsertion au même index (même technique que SetFamilyChoice) pour
    /// rafraîchir la ligne sans recharger toute la grille - OrderLine n'est pas un ObservableObject.</summary>
    public void SetRowColor(OrderLine line, int? colorId)
    {
        if (!App.GuardWritable()) return;
        var tracked = App.Db.OrderLines.First(l => l.Id == line.Id);
        tracked.RowColorId = colorId;
        App.Db.SaveChanges();
        line.RowColorId = colorId;

        var index = Lines.IndexOf(line);
        if (index < 0) return;
        Lines.RemoveAt(index);
        Lines.Insert(index, line);
        SelectedLine = line;
    }

    // ---- Impression / PDF (lignes affichées, une ligne titre par famille) ----

    protected abstract string[] PrintHeaders { get; }
    protected abstract string[] PrintRow(OrderLine line);

    /// <summary>Titre du bon de commande PDF.</summary>
    protected virtual string PdfTitle => Title;

    private List<string[]> BuildPrintRows() =>
        PrintService.BuildGroupedRows(Lines, l => l.FamilyGroupLabel, PrintRow, PrintHeaders.Length);

    private List<System.Windows.Media.Color?> BuildPrintRowColors() =>
        PrintService.BuildGroupedRowColors(Lines, l => l.FamilyGroupLabel, l => RowColorPalette.ColorFor(l.RowColorId));

    [RelayCommand]
    private void Print() =>
        App.Printer.PrintTable(Title + FamilyFilter.TitleSuffix, PrintHeaders, BuildPrintRows(), printColumnsKey: Title, rowColors: BuildPrintRowColors());

    [RelayCommand]
    private void ExportPdf()
    {
        var path = App.PdfExport.ExportTable(App.Settings.ResolvedPdfExportPath, PdfTitle + FamilyFilter.TitleSuffix, PrintHeaders, BuildPrintRows(), App.CompanyLogo);
        App.Dialogs.ShowMessage("Export PDF", $"Bon de commande généré avec succès.\n\nIl est stocké dans :\n{path}");
    }
}
