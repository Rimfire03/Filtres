using System.Windows;
using FiltresApp.Core.Models;
using FiltresApp.ViewModels;
using FiltresApp.Views.Dialogs;

namespace FiltresApp.Services;

public interface IDialogService
{
    void ShowMessage(string title, string message);
    bool ShowConfirm(string title, string message);
    bool EditFields(string title, List<EditField> fields);
    (int Month, DateOnly Date, int Quantity)? PickReplacement(int defaultMonth, int defaultQuantity);
    (DateOnly Date, int Quantity)? PickSimpleReplacement(int defaultQuantity);
    List<FilterRef>? PickFilterLinks(List<FilterPickItem> items);
    void ShowFilterHistory(string locationLabel, string location, string dimension, List<FilterReplacement> replacements);
    void ShowOpacimetricHistory(string location, string dimension, List<OpacimetricReplacement> replacements);
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

    public (int Month, DateOnly Date, int Quantity)? PickReplacement(int defaultMonth, int defaultQuantity)
    {
        var month = defaultMonth;
        var date = DateOnly.FromDateTime(DateTime.Today);
        var qty = defaultQuantity;

        var fields = new List<EditField>
        {
            EditField.MonthComboField("Mois du remplacement", () => month, v => month = v),
            EditField.DateField("Date du changement réalisé", () => date, v => date = v ?? date),
            EditField.IntField("Quantité changée", () => qty, v => qty = v)
        };

        return EditFields("Enregistrer un remplacement", fields) ? (month, date, qty) : null;
    }

    public (DateOnly Date, int Quantity)? PickSimpleReplacement(int defaultQuantity)
    {
        var date = DateOnly.FromDateTime(DateTime.Today);
        var qty = defaultQuantity;

        var fields = new List<EditField>
        {
            EditField.DateField("Date du changement", () => date, v => date = v ?? date),
            EditField.IntField("Quantité changée", () => qty, v => qty = v)
        };

        return EditFields("Enregistrer un remplacement", fields) ? (date, qty) : null;
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

    public void ShowOpacimetricHistory(string location, string dimension, List<OpacimetricReplacement> replacements)
    {
        var window = new OpacimetricHistoryWindow(location, dimension, replacements) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }
}
