using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Core.Services.Licensing;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>
/// Module « Suivi ZAC » (fonctionnalité de licence <see cref="LicenseFeatures.SuiviZac"/>, activable dans Paramètres) :
/// pièces suivies par type de ZAC (= famille), avec date du dernier et du prochain contrôle ; la saisie d'une date
/// enregistre un contrôle (commentaire + fichiers joints stockés en base). Partie commune avec Courroies / Roulements :
/// <see cref="TrackedItemListViewModel{TEntity, TFamily, TRow}"/>. Le droit au module est revérifié à chaque action et
/// réévalué à chaque changement de licence, sans jamais supprimer de données.
/// </summary>
public partial class ZacListViewModel : TrackedItemListViewModel<ZacItem, ZacFamily, ZacRowViewModel>, IDisposable
{
    private readonly Action _onLicenseChanged;

    public override string Title => "Suivi ZAC";

    public ZacListViewModel()
    {
        _alertEnabled = App.Settings.ZacAlertEnabled;
        _alertDays = Math.Max(0, App.Settings.ZacAlertDays);
        _onLicenseChanged = () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshLicense);
        LicenseManager.Changed += _onLicenseChanged;
        Initialize();
    }

    public void Dispose() => LicenseManager.Changed -= _onLicenseChanged;

    // ---- Licence ----

    public bool IsLicensed => LicenseManager.HasFeature(LicenseFeatures.SuiviZac);
    public bool IsNotLicensed => !IsLicensed;
    public string NotLicensedMessage => LicenseFeatures.SuiviZacNotIncludedMessage;

    private void RefreshLicense()
    {
        OnPropertyChanged(nameof(IsLicensed));
        OnPropertyChanged(nameof(IsNotLicensed));
        if (IsLicensed) Reload();
        else StatusMessage = NotLicensedMessage;
    }

    /// <summary>Droit vérifié au moment de l'exécution de chaque action du module.</summary>
    private bool EnsureLicensed()
    {
        if (IsLicensed) return true;
        OnPropertyChanged(nameof(IsLicensed));
        OnPropertyChanged(nameof(IsNotLicensed));
        StatusMessage = NotLicensedMessage;
        return false;
    }

    [ObservableProperty] private string _statusMessage = string.Empty;

    // ---- Alerte de démarrage ----

    [ObservableProperty] private bool _alertEnabled;
    [ObservableProperty] private int _alertDays;

    partial void OnAlertEnabledChanged(bool value)
    {
        App.Settings.ZacAlertEnabled = value;
        App.Settings.Save();
    }

    partial void OnAlertDaysChanged(int value)
    {
        if (value < 0) { AlertDays = 0; return; }
        App.Settings.ZacAlertDays = value;
        App.Settings.Save();
        foreach (var row in Rows) row.RefreshZac();
    }

    // ---- Données ----

    protected override List<ZacItem> LoadEntities(FamilyOption family)
    {
        if (!IsLicensed) return new List<ZacItem>();
        var query = App.Db.ZacItems.Include(i => i.Controls).Include(i => i.Family).AsNoTracking();
        if (family.IsNoFamily) query = query.Where(i => i.ZacFamilyId == null);
        else if (family.FamilyId is int familyId) query = query.Where(i => i.ZacFamilyId == familyId);
        return query.AsEnumerable().OrderBy(i => i.ZacFamilyId == null).ThenBy(i => i.Family?.Nom, NaturalStringComparer.Instance)
            .ThenBy(i => i.Location, NaturalStringComparer.Instance).ToList();
    }

    protected override ZacRowViewModel CreateRow(ZacItem item) => new(item, YearContext.Year, this);

    /// <summary>Colonne « Saisie du contrôle » : une date choisie ouvre la fenêtre de contrôle (commentaire, fichiers
    /// joints par glisser-déposer) puis enregistre le contrôle et recalcule le prochain. Rien n'est enregistré si la
    /// fenêtre est annulée.</summary>
    public override bool AddReplacement(ZacItem item, DateOnly date)
    {
        if (!EnsureLicensed() || !App.GuardWritable()) return false;

        var window = new FiltresApp.Views.Dialogs.ZacControlWindow($"Contrôle du {date:dd/MM/yyyy} - « {item.Location} »")
        { Owner = System.Windows.Application.Current.MainWindow };
        if (window.ShowDialog() != true) return false;

        var tracked = App.Db.ZacItems.Include(i => i.Controls).First(i => i.Id == item.Id);
        var control = new ZacControl { DateChanged = date, Comment = string.IsNullOrWhiteSpace(window.Comment) ? null : window.Comment.Trim() };
        foreach (var path in window.FilePaths)
            control.Attachments.Add(ZacService.CreateAttachment(System.IO.Path.GetFileName(path), System.IO.File.ReadAllBytes(path)));
        tracked.Controls.Add(control);

        // Le prochain contrôle ne se recalcule que si ce contrôle est le plus récent.
        var latest = tracked.Controls.Where(c => c.DateChanged.HasValue).Max(c => c.DateChanged!.Value);
        if (date >= latest) tracked.NextControlDate = ZacService.NextAfter(date, tracked.IntervalMonths);
        App.Db.SaveChanges();

        item.Controls = tracked.Controls.Select(c => new ZacControl { Id = c.Id, ZacItemId = c.ZacItemId, DateChanged = c.DateChanged, Comment = c.Comment }).ToList();
        item.NextControlDate = tracked.NextControlDate;
        Rows.FirstOrDefault(r => r.Id == item.Id)?.RefreshZac();
        return true;
    }

    protected override void SaveEntityFamily(int entityId, int? familyId) =>
        App.Db.ZacItems.Where(i => i.Id == entityId).ExecuteUpdate(s => s.SetProperty(i => i.ZacFamilyId, familyId));

    protected override void ApplyFamilyInMemory(ZacItem item, ZacFamily? family)
    {
        item.ZacFamilyId = family?.Id;
        item.Family = family;
    }

    protected override ZacItem CreateEntity(int? familyId) => new() { ZacFamilyId = familyId };

    protected override ZacItem CopyEntity(ZacItem source) => new()
    {
        Location = source.Location,
        IntervalMonths = source.IntervalMonths,
        NextControlDate = source.NextControlDate,
        Commentaire = source.Commentaire,
        ZacFamilyId = source.ZacFamilyId,
        RowColorId = source.RowColorId
    };

    protected override bool EditEntity(ZacItem entity, bool isNew)
    {
        if (!EnsureLicensed()) return false;
        var fields = new List<EditField>
        {
            FamilyField(entity.ZacFamilyId, id => entity.ZacFamilyId = id),
            EditField.Multiline("Nom de la pièce", () => entity.Location, v => entity.Location = v, required: true),
            EditField.IntField("Intervalle entre deux contrôles (mois)", () => entity.IntervalMonths, v => entity.IntervalMonths = Math.Max(1, v)),
            EditField.DateField("Prochain contrôle", () => entity.NextControlDate, v => entity.NextControlDate = v),
            EditField.Multiline("Commentaire", () => entity.Commentaire, v => entity.Commentaire = v)
        };
        return App.Dialogs.EditFields(isNew ? "Ajouter une pièce" : "Modifier la pièce", fields);
    }

    [RelayCommand]
    private void Print()
    {
        var headers = new[] { "Nom de la pièce", "Type de ZAC", "Dernier contrôle", "Prochain contrôle", "État", "Commentaire" };
        var rows = PrintService.BuildGroupedRows(Rows, r => r.FamilyGroupLabel, r => new[]
        {
            r.Location, r.FamilyGroupLabel, r.LastControlDate?.ToString("dd/MM/yyyy") ?? "-",
            r.NextControlDate?.ToString("dd/MM/yyyy") ?? "-", r.StatusText, r.Commentaire ?? ""
        }, headers.Length);
        var rowColors = PrintService.BuildGroupedRowColors(Rows, r => r.FamilyGroupLabel, r => RowColorPalette.ColorFor(r.RowColorId));
        App.Printer.PrintTable(PrintTitle, headers, rows, rowColors: rowColors);
    }

    /// <summary>Contrôles de cette pièce (date + commentaire), par année.</summary>
    protected override void OpenHistory(ZacItem item)
    {
        if (!EnsureLicensed()) return;
        var controls = App.Db.ZacControls.AsNoTracking().Where(c => c.ZacItemId == item.Id).ToList();
        App.Dialogs.ShowReplacementHistory(new ReplacementHistory<ZacControl>(
            $"Pièce : {item.Location}",
            "Aucun contrôle enregistré pour cette pièce.",
            "Commentaire", c => c.Comment ?? "", c => c.Comment ?? "",
            DateColumnWidth: 1.2, DetailColumnWidth: 1.6), controls);
    }

    /// <summary>« Pièces jointes... » : liste des fichiers de tous les contrôles de la pièce.</summary>
    public void ShowAttachments(ZacItem item)
    {
        if (!EnsureLicensed()) return;
        new FiltresApp.Views.Dialogs.ZacAttachmentsWindow(item.Id, item.Location) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
    }

    // ---- Familles = types de ZAC (partie commune : TrackedItemListViewModel.Families.cs) ----

    protected override List<ZacFamily> LoadFamilies() => App.Db.ZacFamilies.AsNoTracking().AsEnumerable().OrderBy(f => f.Nom, NaturalStringComparer.Instance).ToList();

    protected override ZacFamily CreateFamily() => new();

    protected override bool FamilyNameExists(string name, int excludedId) =>
        App.Db.ZacFamilies.AsNoTracking().Any(f => f.Id != excludedId && f.Nom.ToLower() == name.ToLower());

    protected override int CountItemsInFamily(int familyId) => App.Db.ZacItems.Count(i => i.ZacFamilyId == familyId);

    protected override void DeleteFamilyKeepingItems(int familyId)
    {
        App.Db.ZacItems.Where(i => i.ZacFamilyId == familyId).ExecuteUpdate(s => s.SetProperty(i => i.ZacFamilyId, (int?)null));
        App.Db.ZacFamilies.Where(f => f.Id == familyId).ExecuteDelete();
    }

    protected override string ItemsLabel => "pièces";

    protected override string FamilyDeleteDetail(int count) =>
        $"Ses {count} pièce(s) ne seront pas supprimées : elles passeront en « {ZacItem.NoFamilyLabel} ».";
}
