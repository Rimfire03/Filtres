using System.Windows;
using FiltresApp.Core.Models;
using FiltresApp.ViewModels;
using FiltresApp.ViewModels.Filtres;
using FiltresApp.Views.Dialogs;

namespace FiltresApp.Services;

/// <summary>Description de la fenêtre "Consulter l'historique..." d'un élément suivi sans périodicité fixe
/// (filtre "F7 à H14", courroie, jeu de roulements) : textes d'en-tête et colonne "détail" propre à chaque
/// module (quantité changée, roulements changés...). Voir <see cref="ReplacementHistoryWindow"/>.</summary>
/// <param name="Header">Ligne d'identification sous le titre (nom de la centrale...).</param>
/// <param name="NoHistoryText">Texte affiché quand aucun remplacement n'est enregistré.</param>
/// <param name="DetailHeader">Titre de la seconde colonne.</param>
/// <param name="Detail">Valeur de la seconde colonne pour un remplacement.</param>
/// <param name="ConfirmDetail">Précision entre parenthèses dans la confirmation de suppression.</param>
/// <param name="DateColumnWidth">Largeur relative (étoile) de la colonne date.</param>
/// <param name="DetailColumnWidth">Largeur relative (étoile) de la seconde colonne.</param>
public record ReplacementHistory<TReplacement>(
    string Header,
    string NoHistoryText,
    string DetailHeader,
    Func<TReplacement, string> Detail,
    Func<TReplacement, string> ConfirmDetail,
    double DateColumnWidth = 1.6,
    double DetailColumnWidth = 1)
    where TReplacement : IDatedReplacement;

public interface IDialogService
{
    void ShowMessage(string title, string message);
    bool ShowConfirm(string title, string message);
    bool EditFields(string title, List<EditField> fields);
    List<FilterRef>? PickFilterLinks(List<FilterPickItem> items);
    void ShowFilterHistory(string locationLabel, string location, string dimension, List<FilterReplacement> replacements);

    /// <summary>Historique d'un élément suivi sans périodicité fixe ; la suppression d'un enregistrement
    /// depuis la fenêtre est immédiatement enregistrée en base.</summary>
    void ShowReplacementHistory<TReplacement>(ReplacementHistory<TReplacement> history, List<TReplacement> replacements)
        where TReplacement : class, IDatedReplacement;
}

public class DialogService : IDialogService
{
    public void ShowMessage(string title, string message)
    {
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public bool ShowConfirm(string title, string message)
    {
        return MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public bool EditFields(string title, List<EditField> fields)
    {
        var window = new DynamicEditWindow(title, fields) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true;
    }

    public List<FilterRef>? PickFilterLinks(List<FilterPickItem> items)
    {
        var window = new FilterLinkWindow(items) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.SelectedFilters : null;
    }

    public void ShowFilterHistory(string locationLabel, string location, string dimension, List<FilterReplacement> replacements)
    {
        var window = new FilterHistoryWindow(locationLabel, location, dimension, replacements) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    public void ShowReplacementHistory<TReplacement>(ReplacementHistory<TReplacement> history, List<TReplacement> replacements)
        where TReplacement : class, IDatedReplacement
    {
        var rows = replacements
            .Where(r => r.DateChanged.HasValue)
            .Select(r => new ReplacementHistoryRow
            {
                Id = r.Id,
                Date = r.DateChanged!.Value,
                Detail = history.Detail(r),
                ConfirmDetail = history.ConfirmDetail(r)
            })
            .ToList();

        void DeleteFromDb(int id)
        {
            if (App.Db.Set<TReplacement>().Find(id) is not { } tracked) return;
            App.Db.Set<TReplacement>().Remove(tracked);
            App.Db.SaveChanges();
        }

        var window = new ReplacementHistoryWindow(history.Header, history.NoHistoryText, history.DetailHeader,
            history.DateColumnWidth, history.DetailColumnWidth, rows, DeleteFromDb) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }
}
