using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Services.Licensing;

namespace FiltresApp.ViewModels;

/// <summary>Élément du module « Suivi ZAC » (point d'extension : champs métier à définir).</summary>
public sealed record ZacEntry(int Id, string Name);

/// <summary>
/// Module « Suivi ZAC » : écran principal (liste, détail, ajout) débloqué par la fonctionnalité de licence
/// <see cref="LicenseFeatures.SuiviZac"/>. Le droit est revérifié à chaque action (<see cref="EnsureLicensed"/>) et
/// réévalué à chaque changement de licence (activation, revalidation, fin de démo, révocation) sans redémarrage :
/// aucune donnée n'est jamais supprimée, le module redevient simplement inaccessible puis se rouvre avec la licence.
/// Les règles métier ne sont pas encore définies : les points d'extension sont les méthodes <c>Load</c>,
/// <c>OnAdd</c> et <c>OnOpenDetail</c>.
/// </summary>
public partial class ZacViewModel : ObservableObject, IDisposable
{
    private readonly Action _onLicenseChanged;

    public string Title => "Suivi ZAC";

    public ObservableCollection<ZacEntry> Items { get; } = [];

    [ObservableProperty] private ZacEntry? _selectedItem;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Droit courant au module (règle commune : LicenseManager.HasFeature).</summary>
    public bool IsLicensed => LicenseManager.HasFeature(LicenseFeatures.SuiviZac);

    public bool IsNotLicensed => !IsLicensed;

    public string NotLicensedMessage => LicenseFeatures.SuiviZacNotIncludedMessage;

    public ZacViewModel()
    {
        _onLicenseChanged = () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshLicense);
        LicenseManager.Changed += _onLicenseChanged;
        Load();
    }

    public void Dispose() => LicenseManager.Changed -= _onLicenseChanged;

    private void RefreshLicense()
    {
        OnPropertyChanged(nameof(IsLicensed));
        OnPropertyChanged(nameof(IsNotLicensed));
        if (!IsLicensed)
        {
            SelectedItem = null;
            StatusMessage = NotLicensedMessage;
        }
        else
        {
            StatusMessage = string.Empty;
            Load();
        }
    }

    /// <summary>Vérifie le droit au moment de l'exécution (pas seulement à l'affichage du menu).</summary>
    private bool EnsureLicensed()
    {
        if (IsLicensed) return true;
        OnPropertyChanged(nameof(IsLicensed));
        OnPropertyChanged(nameof(IsNotLicensed));
        StatusMessage = NotLicensedMessage;
        return false;
    }

    /// <summary>Point d'extension : chargement des éléments du module (données à définir).</summary>
    private void Load()
    {
        Items.Clear();
        if (!IsLicensed) return;
        // À définir : source des données du module.
    }

    [RelayCommand]
    private void Add()
    {
        if (!EnsureLicensed() || !App.GuardWritable()) return;
        OnAdd();
    }

    [RelayCommand]
    private void OpenDetail()
    {
        if (!EnsureLicensed() || SelectedItem is null) return;
        OnOpenDetail(SelectedItem);
    }

    /// <summary>Point d'extension : ajout d'un élément (champs et règles métier à définir).</summary>
    private void OnAdd() =>
        StatusMessage = "L'ajout d'un élément sera disponible quand le contenu du module aura été défini.";

    /// <summary>Point d'extension : affichage du détail d'un élément.</summary>
    private void OnOpenDetail(ZacEntry entry) =>
        StatusMessage = $"Le détail de « {entry.Name} » sera disponible quand le contenu du module aura été défini.";
}
