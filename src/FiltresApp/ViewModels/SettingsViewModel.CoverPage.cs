using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Services;

namespace FiltresApp.ViewModels;

/// <summary>Section "Page de garde du bon de commande" de l'écran Paramètres : textes de la page de garde
/// de l'export PDF Commande / Inventaire (voir <see cref="CoverPageInfo"/>).</summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private string _coverOrganisation = string.Empty;
    [ObservableProperty] private string _coverDirection = string.Empty;
    [ObservableProperty] private string _coverContact = string.Empty;
    [ObservableProperty] private string _coverTitle = string.Empty;
    [ObservableProperty] private string _coverDelivery = string.Empty;
    [ObservableProperty] private string _coverMarket = string.Empty;
    [ObservableProperty] private string _coverStatusMessage = string.Empty;

    private void InitializeCoverPage() => LoadCoverPage(App.Settings.CoverPage);

    private void LoadCoverPage(CoverPageInfo info)
    {
        CoverOrganisation = info.Organisation;
        CoverDirection = info.Direction;
        CoverContact = info.Contact;
        CoverTitle = info.Title;
        CoverDelivery = info.Delivery;
        CoverMarket = info.Market;
    }

    [RelayCommand]
    private void SaveCoverPage()
    {
        App.Settings.CoverPage = new CoverPageInfo
        {
            Organisation = CoverOrganisation.Trim(),
            Direction = CoverDirection.Trim(),
            Contact = CoverContact.Trim(),
            Title = CoverTitle.Trim(),
            Delivery = CoverDelivery.Trim(),
            Market = CoverMarket.Trim()
        };
        App.Settings.Save();
        CoverStatusMessage = "Page de garde enregistrée.";
    }

    /// <summary>Remet les textes d'origine dans les champs ; à enregistrer ensuite comme le reste.</summary>
    [RelayCommand]
    private void ResetCoverPage()
    {
        LoadCoverPage(new CoverPageInfo());
        CoverStatusMessage = "Textes d'origine remis : cliquez sur « Enregistrer » pour les conserver.";
    }
}
