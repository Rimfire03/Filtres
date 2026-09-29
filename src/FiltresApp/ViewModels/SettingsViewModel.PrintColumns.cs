using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Services;

namespace FiltresApp.ViewModels;

/// <summary>Section "Colonnes imprimées" de l'écran Paramètres : choix, par écran (G4/G3/Charbon,
/// Filtres F7 à H14, Liste K7, Commande, Inventaire), des colonnes incluses à l'impression. Garantit avec
/// PrintService (colonnes Star) qu'une impression ne dépasse jamais une feuille de large.</summary>
public partial class SettingsViewModel
{
    public IReadOnlyList<PrintableScreen> PrintableScreens => PrintableColumnsRegistry.All;

    [ObservableProperty] private PrintableScreen? _selectedPrintScreen;
    [ObservableProperty] private ObservableCollection<PrintColumnOption> _printColumnOptions = new();

    private void InitializePrintColumns() => SelectedPrintScreen = PrintableScreens.FirstOrDefault();

    partial void OnSelectedPrintScreenChanged(PrintableScreen? value) =>
        PrintColumnOptions = value is null
            ? new ObservableCollection<PrintColumnOption>()
            : new ObservableCollection<PrintColumnOption>(value.Columns.Select(c => new PrintColumnOption(value.Key, c)));
}
