using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace FiltresApp.Views.Dialogs;

/// <summary>Contrôles d'une pièce du module « Suivi ZAC » : ajout de documents (glisser-déposer) et saisie des valeurs
/// mesurées <b>après</b> l'enregistrement du contrôle.</summary>
public partial class ZacControlsWindow : Window
{
    private const long MaxFileBytes = 50L * 1024 * 1024;

    private sealed record ControlRow(int Id, DateOnly? Date, string? Comment, int FileCount, int ValueCount)
    {
        public string DateText => Date?.ToString("dd/MM/yyyy") ?? "-";
    }

    private sealed record FileRow(int Id, string FileName, long Size, bool Compressed)
    {
        public string SizeText => ZacService.FormatSize(Size);
        public string StorageText => Compressed ? "Compressé" : "Tel quel";
    }

    private readonly int _itemId;
    private int? _controlId;

    /// <param name="selectControlId">Contrôle à sélectionner à l'ouverture (celui qui vient d'être enregistré).</param>
    public ZacControlsWindow(int itemId, string itemName, int? selectControlId = null)
    {
        InitializeComponent();
        _itemId = itemId;
        HeaderText.Text = $"Contrôles - « {itemName} »";
        LoadControls(selectControlId);
    }

    private ControlRow? SelectedControl => ControlsGrid.SelectedItem as ControlRow;

    private void LoadControls(int? selectId)
    {
        // Projection sans le contenu des fichiers : lus seulement à l'ouverture / l'enregistrement.
        var rows = App.Db.ZacControls.AsNoTracking().Where(c => c.ZacItemId == _itemId)
            .OrderByDescending(c => c.DateChanged)
            .Select(c => new ControlRow(c.Id, c.DateChanged, c.Comment, c.Attachments.Count, c.Measurements.Count))
            .ToList();
        ControlsGrid.ItemsSource = rows;
        ControlsGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selectId) ?? rows.FirstOrDefault();
    }

    private void OnControlSelected(object sender, SelectionChangedEventArgs e)
    {
        _controlId = SelectedControl?.Id;
        LoadFiles();
        LoadValues();
    }

    private void KeepSelection()
    {
        var id = _controlId;
        LoadControls(id);
    }

    // ---- Documents ----

    private void LoadFiles()
    {
        FilesGrid.ItemsSource = _controlId is int id
            ? App.Db.ZacAttachments.AsNoTracking().Where(a => a.ZacControlId == id).OrderBy(a => a.FileName)
                .Select(a => new FileRow(a.Id, a.FileName, a.OriginalSize, a.IsCompressed)).ToList()
            : new List<FileRow>();
        DropHint.Visibility = FilesGrid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        if (_controlId is not int controlId || !App.GuardWritable()) return;
        var refused = new List<string>();
        var added = 0;
        foreach (var path in paths)
        {
            if (!File.Exists(path)) { refused.Add($"{Path.GetFileName(path)} (dossier ou introuvable)"); continue; }
            if (new FileInfo(path).Length > MaxFileBytes) { refused.Add($"{Path.GetFileName(path)} (plus de {ZacService.FormatSize(MaxFileBytes)})"); continue; }
            var attachment = ZacService.CreateAttachment(Path.GetFileName(path), File.ReadAllBytes(path));
            attachment.ZacControlId = controlId;
            App.Db.ZacAttachments.Add(attachment);
            added++;
        }
        if (added > 0) App.Db.SaveChanges();
        KeepSelection();
        StatusText.Text = added > 0 ? $"{added} document(s) ajouté(s)." : string.Empty;
        if (refused.Count > 0)
            MessageBox.Show(this, "Fichiers non ajoutés :\n" + string.Join("\n", refused), "Documents", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        if (_controlId is null) return;
        var dialog = new OpenFileDialog { Title = "Ajouter des documents", Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }

    private void OnDragEnter(object sender, DragEventArgs e) => DropZone.BorderBrush = (Brush)FindResource("BrushPrimary");
    private void OnDragLeave(object sender, DragEventArgs e) => DropZone.BorderBrush = (Brush)FindResource("BrushBorder");

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && _controlId is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        OnDragLeave(sender, e);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths);
    }

    private FileRow? SelectedFile => FilesGrid.SelectedItem as FileRow;

    private byte[]? ReadContent(int id)
    {
        var attachment = App.Db.ZacAttachments.AsNoTracking().FirstOrDefault(a => a.Id == id);
        return attachment is null ? null : ZacService.ReadContent(attachment);
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        if (SelectedFile is not { } row || ReadContent(row.Id) is not { } content) return;
        var dir = Path.Combine(Path.GetTempPath(), "FiltresApp-ZAC", row.Id.ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(row.FileName));
        File.WriteAllBytes(path, content);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OnSaveFile(object sender, RoutedEventArgs e)
    {
        if (SelectedFile is not { } row) return;
        var dialog = new SaveFileDialog { FileName = row.FileName, Title = "Enregistrer le document" };
        if (dialog.ShowDialog(this) != true || ReadContent(row.Id) is not { } content) return;
        File.WriteAllBytes(dialog.FileName, content);
    }

    private void OnDeleteFile(object sender, RoutedEventArgs e)
    {
        if (SelectedFile is not { } row || !App.GuardWritable()) return;
        if (MessageBox.Show(this, $"Supprimer définitivement « {row.FileName} » ?", "Documents", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        App.Db.ZacAttachments.Where(a => a.Id == row.Id).ExecuteDelete();
        KeepSelection();
    }

    // ---- Valeurs ----

    private List<ZacMeasurement> _values = new();

    private void LoadValues()
    {
        _values = _controlId is int id
            ? App.Db.ZacMeasurements.AsNoTracking().Where(m => m.ZacControlId == id).OrderBy(m => m.Id).ToList()
            : new List<ZacMeasurement>();
        ValuesGrid.ItemsSource = null;
        ValuesGrid.ItemsSource = _values;
    }

    private void OnAddValue(object sender, RoutedEventArgs e)
    {
        if (_controlId is not int controlId) return;
        ValuesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _values.Add(new ZacMeasurement { ZacControlId = controlId });
        ValuesGrid.ItemsSource = null;
        ValuesGrid.ItemsSource = _values;
        ValuesGrid.SelectedItem = _values[^1];
        ValuesGrid.ScrollIntoView(_values[^1]);
    }

    private void OnDeleteValue(object sender, RoutedEventArgs e)
    {
        if (ValuesGrid.SelectedItem is not ZacMeasurement m) return;
        ValuesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _values.Remove(m);
        ValuesGrid.ItemsSource = null;
        ValuesGrid.ItemsSource = _values;
    }

    /// <summary>Remplace les valeurs du contrôle par la liste affichée (les lignes sans désignation sont ignorées).</summary>
    private void OnSaveValues(object sender, RoutedEventArgs e)
    {
        if (_controlId is not int controlId || !App.GuardWritable()) return;
        ValuesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ValuesGrid.CommitEdit();

        var kept = _values.Where(v => !string.IsNullOrWhiteSpace(v.Name)).ToList();
        App.Db.ZacMeasurements.Where(m => m.ZacControlId == controlId).ExecuteDelete();
        foreach (var v in kept)
            App.Db.ZacMeasurements.Add(new ZacMeasurement
            {
                ZacControlId = controlId, Name = v.Name.Trim(), Value = v.Value,
                Unit = string.IsNullOrWhiteSpace(v.Unit) ? null : v.Unit.Trim(), IsConform = v.IsConform
            });
        App.Db.SaveChanges();
        KeepSelection();
        StatusText.Text = $"{kept.Count} valeur(s) enregistrée(s).";
    }
}
