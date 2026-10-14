using System.Windows;
using System.Windows.Media;
using FiltresApp.Core.Services;

namespace FiltresApp.Views.Dialogs;

/// <summary>Fenêtre d'alerte du module « Suivi ZAC » à l'ouverture de l'application : orange pour les contrôles à
/// venir (dans le délai paramétré), rouge si au moins une échéance est dépassée. <see cref="OpenModuleRequested"/>
/// est vrai si l'utilisateur demande l'ouverture du module.</summary>
public partial class ZacAlertWindow : Window
{
    private sealed record Row(string Item, string ZacType, string NextText, string DelayText, string LevelName);

    public bool OpenModuleRequested { get; private set; }

    public ZacAlertWindow(IReadOnlyList<ZacAlertRow> alerts)
    {
        InitializeComponent();
        var overdue = alerts.Count(a => a.Level == ZacAlertLevel.Overdue);
        var approaching = alerts.Count - overdue;

        Banner.Background = new SolidColorBrush(overdue > 0 ? Color.FromRgb(0xDC, 0x26, 0x26) : Color.FromRgb(0xD9, 0x77, 0x06));
        BannerText.Text = overdue > 0
            ? $"{overdue} contrôle(s) dépassé(s)" + (approaching > 0 ? $" et {approaching} à effectuer prochainement." : ".")
            : $"{approaching} contrôle(s) à effectuer prochainement.";

        // Dépassés d'abord, puis par échéance.
        Grid.ItemsSource = alerts
            .OrderBy(a => a.Level == ZacAlertLevel.Overdue ? 0 : 1).ThenBy(a => a.NextControl)
            .Select(a => new Row(a.Item, a.ZacType, a.NextText, a.DelayText, a.Level.ToString()))
            .ToList();
    }

    private void OnOpenModule(object sender, RoutedEventArgs e)
    {
        OpenModuleRequested = true;
        Close();
    }
}
