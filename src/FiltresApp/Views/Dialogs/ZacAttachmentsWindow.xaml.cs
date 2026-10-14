using System.Diagnostics;
using System.IO;
using System.Windows;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace FiltresApp.Views.Dialogs;

/// <summary>Fichiers joints aux contrôles d'une pièce du module « Suivi ZAC » : ouvrir, enregistrer sous, supprimer.</summary>
public partial class ZacAttachmentsWindow : Window
{
    private sealed record Row(int Id, string FileName, long Size, bool Compressed, DateOnly? Date)
    {
        public string DateText => Date?.ToString("dd/MM/yyyy") ?? "-";
        public string SizeText => ZacService.FormatSize(Size);
        public string StorageText => Compressed ? "Compressé" : "Tel quel";
    }

    private readonly int _itemId;

    public ZacAttachmentsWindow(int itemId, string itemName)
    {
        InitializeComponent();
        _itemId = itemId;
        HeaderText.Text = $"Pièces jointes - « {itemName} »";
        Reload();
    }

    private void Reload()
    {
        // Projection sans le contenu : les fichiers ne sont lus qu'à l'ouverture / l'enregistrement.
        Grid.ItemsSource = App.Db.ZacAttachments.AsNoTracking()
            .Where(a => a.ZacControl!.ZacItemId == _itemId)
            .OrderByDescending(a => a.ZacControl!.DateChanged).ThenBy(a => a.FileName)
            .Select(a => new Row(a.Id, a.FileName, a.OriginalSize, a.IsCompressed, a.ZacControl!.DateChanged))
            .ToList();
    }

    private Row? Selected => Grid.SelectedItem as Row;

    private byte[]? ReadContent(int id)
    {
        var attachment = App.Db.ZacAttachments.AsNoTracking().FirstOrDefault(a => a.Id == id);
        return attachment is null ? null : ZacService.ReadContent(attachment);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row || ReadContent(row.Id) is not { } content) return;
        // Copie temporaire (nom d'origine) ouverte avec le programme associé.
        var dir = Path.Combine(Path.GetTempPath(), "FiltresApp-ZAC", row.Id.ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(row.FileName));
        File.WriteAllBytes(path, content);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        var dialog = new SaveFileDialog { FileName = row.FileName, Title = "Enregistrer le fichier joint" };
        if (dialog.ShowDialog(this) != true || ReadContent(row.Id) is not { } content) return;
        File.WriteAllBytes(dialog.FileName, content);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row || !App.GuardWritable()) return;
        if (MessageBox.Show(this, $"Supprimer définitivement « {row.FileName} » ?", "Pièces jointes", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        App.Db.ZacAttachments.Where(a => a.Id == row.Id).ExecuteDelete();
        Reload();
    }
}
