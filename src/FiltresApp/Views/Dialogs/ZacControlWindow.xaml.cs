using System.IO;
using System.Windows;
using FiltresApp.Core.Services;
using Microsoft.Win32;

namespace FiltresApp.Views.Dialogs;

/// <summary>Saisie d'un contrôle du module « Suivi ZAC » : commentaire et fichiers joints (glisser-déposer depuis
/// l'Explorateur ou « Parcourir... »). Les fichiers sont lus et stockés en base par l'appelant.</summary>
public partial class ZacControlWindow : Window
{
    private const long MaxFileBytes = 50L * 1024 * 1024;

    private readonly List<string> _files = new();

    public string Comment => CommentBox.Text;
    public IReadOnlyList<string> FilePaths => _files;

    public ZacControlWindow(string title)
    {
        InitializeComponent();
        Title = "Contrôle";
        HeaderText.Text = title;
    }

    // ---- Glisser-déposer ----

    private void OnDragEnter(object sender, DragEventArgs e) => DropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("BrushPrimary");

    private void OnDragLeave(object sender, DragEventArgs e) => DropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("BrushBorder");

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        OnDragLeave(sender, e);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Joindre des fichiers", Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var refused = new List<string>();
        foreach (var path in paths)
        {
            if (!File.Exists(path)) { refused.Add($"{Path.GetFileName(path)} (dossier ou introuvable)"); continue; }
            if (new FileInfo(path).Length > MaxFileBytes) { refused.Add($"{Path.GetFileName(path)} (plus de {ZacService.FormatSize(MaxFileBytes)})"); continue; }
            if (_files.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            _files.Add(path);
        }
        RefreshList();
        if (refused.Count > 0)
            MessageBox.Show(this, "Fichiers non ajoutés :\n" + string.Join("\n", refused), "Fichiers joints", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedIndex < 0) return;
        _files.RemoveAt(FilesList.SelectedIndex);
        RefreshList();
    }

    private void RefreshList()
    {
        FilesList.ItemsSource = null;
        FilesList.ItemsSource = _files.Select(p => $"{Path.GetFileName(p)}  ({ZacService.FormatSize(new FileInfo(p).Length)})").ToList();
        FilesList.Visibility = _files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DropHint.Visibility = _files.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Vrai si l'utilisateur a choisi d'enregistrer puis de saisir documents et valeurs.</summary>
    public bool OpenDetailsAfter { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnOkThenDetails(object sender, RoutedEventArgs e)
    {
        OpenDetailsAfter = true;
        DialogResult = true;
    }
}
